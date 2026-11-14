namespace Wizz.Infrastructure.Services;

using System.Data;
using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wizz.Application.Abstractions;
using Wizz.Infrastructure.Data;

/// <summary>خدمة النسخ الاحتياطي والاستعادة لـ SQLite باستخدام آلية النسخ المتسقة (SQLite Backup API).</summary>
public sealed class BackupService : IBackupService
{
    /// <summary>بادئة أسماء ملفات النسخ الاحتياطي العادية.</summary>
    private const string BackupFilePrefix = "wizz_backup_";

    /// <summary>بادئة نسخة الأمان التي تُنشأ تلقائيًا قبل أي استعادة.</summary>
    private const string SafetyBackupPrefix = "wizz_prerestore_";

    /// <summary>الجداول الأساسية التي يجب التحقق من وجودها بعد النسخ أو قبل الاستعادة.</summary>
    private static readonly HashSet<string> RequiredTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sales", "Products", "Stores", "Users", "StockMovements", "SaleItems", "UnitsOfMeasure", "DeviceRegistrations", "Subscriptions", "CashierShifts", "AuditLogs",
    };

    private readonly WizzDbContext _db;
    private readonly string _backupFolder;
    private readonly ILogger<BackupService>? _logger;

    public BackupService(WizzDbContext db, IConfiguration? configuration = null, ILogger<BackupService>? logger = null)
        : this(db, ResolveBackupFolder(configuration), logger)
    {
    }

    public BackupService(WizzDbContext db, string backupFolder, ILogger<BackupService>? logger = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _logger = logger;
        _backupFolder = string.IsNullOrWhiteSpace(backupFolder)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups")
            : Path.GetFullPath(backupFolder);

        Directory.CreateDirectory(_backupFolder);

        _logger?.LogInformation("Backup service initialized. Backup folder: {BackupFolder}", _backupFolder);
    }

    /// <inheritdoc />
    public async Task<BackupResult> CreateBackupAsync(CancellationToken ct = default)
    {
        var startedAt = DateTime.UtcNow;
        var backupPath = Path.Combine(
            _backupFolder,
            $"{BackupFilePrefix}{startedAt:yyyyMMdd_HHmmss_fff}.db");

        try
        {
            _logger?.LogInformation("Starting backup creation: {BackupPath}", backupPath);

            var sourcePath = GetDatabasePath();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                _logger?.LogWarning("Source database file not found: {SourcePath}", sourcePath);
                return new BackupResult(false, null, "ملف قاعدة البيانات غير موجود", 0, startedAt);
            }

            // 1. منح المعاملات القصيرة وقتًا للانتهاء قبل بدء اللقطة.
            await WaitForActiveTransactionsAsync(ct);

            // 2. توفير معاملات WAL إلى الملف الرئيسي لضمان لقطة متسقة.
            if (!await CheckpointWalAsync(ct))
            {
                _logger?.LogWarning("WAL checkpoint completed with warnings, continuing backup");
            }

            // 3. استخدام واجهة النسخ المتسقة من SQLite (SQLite Backup API).
            using var sourceConn = new SqliteConnection($"Data Source={sourcePath};Pooling=False");
            await using var destConn = new SqliteConnection($"Data Source={backupPath};Pooling=False");

            await sourceConn.OpenAsync(ct);
            await destConn.OpenAsync(ct);

            // تعيين وضع journal للنسخة الاحتياطية إلى DELETE (لا WAL) لضمان ملف مستقل.
            await using (var pragmaCmd = destConn.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA journal_mode = DELETE";
                await pragmaCmd.ExecuteNonQueryAsync(ct);
            }

            sourceConn.BackupDatabase(destConn);

            await sourceConn.CloseAsync();
            await destConn.CloseAsync();

            _logger?.LogInformation("Backup database copy completed, verifying integrity");

            // 4. التحقق من سلامة الملف المنسوخ (PRAGMA integrity_check).
            var integrity = await VerifyIntegrityAsync(backupPath, ct);
            if (!integrity.Success)
            {
                _logger?.LogError("Backup integrity check failed: {Error}", integrity.Error);
                TryDelete(backupPath);
                return new BackupResult(false, null, $"فشل التحقق من سلامة النسخة الاحتياطية: {integrity.Error}", 0, startedAt);
            }

            // 5. التحقق من وجود الجداول الأساسية في النسخة الاحتياطية.
            if (!await VerifyRequiredTablesAsync(backupPath, ct))
            {
                _logger?.LogError("Backup missing required tables");
                TryDelete(backupPath);
                return new BackupResult(false, null, "النسخة الاحتياطية تفتقر إلى جداول أساسية", 0, startedAt);
            }

            // 6. حساب مجموع التحقق (SHA-256) للملف المنسوخ.
            var checksum = await CalculateChecksumAsync(backupPath, ct);

            var info = new FileInfo(backupPath);
            var duration = DateTime.UtcNow - startedAt;

            _logger?.LogInformation(
                "Backup created successfully: {BackupPath}, Size: {Size} bytes, Duration: {Duration}ms",
                backupPath,
                info.Length,
                duration.TotalMilliseconds);

            return new BackupResult(true, backupPath, null, info.Length, startedAt)
            {
                Checksum = checksum,
                IntegrityCheckResult = integrity.Details,
            };
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("Backup operation cancelled");
            TryDelete(backupPath);
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Backup creation failed");
            TryDelete(backupPath);
            return new BackupResult(false, null, $"فشل النسخ: {ex.Message}", 0, startedAt);
        }
    }

    /// <inheritdoc />
    public async Task<RestoreResult> RestoreAsync(string backupPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            return new RestoreResult(false, "ملف النسخة الاحتياطية غير موجود", 0);
        }

        var targetPath = GetDatabasePath();
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            return new RestoreResult(false, "تعذر تحديد مسار قاعدة البيانات الهدف", 0);
        }

        if (string.Equals(Path.GetFullPath(backupPath), targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return new RestoreResult(false, "ملف النسخة الاحتياطية هو نفسه ملف قاعدة البيانات الحالي", 0);
        }

        // 1. التحقق من صلاحية النسخة قبل لمس قاعدة البيانات الحالية.
        var integrity = await VerifyIntegrityAsync(backupPath, ct);
        if (!integrity.Success)
        {
            return new RestoreResult(false, $"النسخة الاحتياطية غير صالحة: {integrity.Error}", 0);
        }

        if (!await VerifyRequiredTablesAsync(backupPath, ct))
        {
            return new RestoreResult(false, "النسخة الاحتياطية تفتقر إلى جداول أساسية", 0);
        }

        string? safetyPath = null;

        try
        {
            _logger?.LogInformation("Starting restore from {BackupPath} into {TargetPath}", backupPath, targetPath);

            // 2. توفير WAL وإغلاق الاتصالات قبل استبدال الملف.
            await CheckpointWalAsync(ct);
            await _db.Database.CloseConnectionAsync();
            SqliteConnection.ClearAllPools();

            // 3. إنشاء نسخة أمان من القاعدة الحالية قبل الاستعادة.
            if (File.Exists(targetPath))
            {
                safetyPath = Path.Combine(
                    _backupFolder,
                    $"{SafetyBackupPrefix}{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.db");
                File.Copy(targetPath, safetyPath, overwrite: true);
            }

            // 4. استبدال قاعدة البيانات مع إزالة ملفات WAL/SHM القديمة.
            DeleteSidecarFiles(targetPath);
            File.Copy(backupPath, targetPath, overwrite: true);
            DeleteSidecarFiles(targetPath);
            SqliteConnection.ClearAllPools();

            // 5. إعادة فتح الاتصال والتحقق من سلامة النتيجة.
            await _db.Database.OpenConnectionAsync(ct);

            var restoredIntegrity = await VerifyIntegrityAsync(targetPath, ct);
            if (!restoredIntegrity.Success)
            {
                throw new InvalidOperationException($"قاعدة البيانات بعد الاستعادة غير صالحة: {restoredIntegrity.Error}");
            }

            var restoredCount = await CountCoreEntitiesAsync(ct);

            _logger?.LogInformation(
                "Restore completed successfully. Entities: {Count}, SafetyBackup: {SafetyPath}",
                restoredCount,
                safetyPath);

            return new RestoreResult(true, null, restoredCount);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Restore failed, attempting rollback");

            // 6. محاولة التراجع إلى نسخة الأمان عند فشل الاستعادة.
            if (!string.IsNullOrWhiteSpace(safetyPath) && File.Exists(safetyPath))
            {
                try
                {
                    await _db.Database.CloseConnectionAsync();
                    SqliteConnection.ClearAllPools();
                    DeleteSidecarFiles(targetPath);
                    File.Copy(safetyPath, targetPath, overwrite: true);
                    DeleteSidecarFiles(targetPath);
                    SqliteConnection.ClearAllPools();
                    _logger?.LogInformation("Rollback from safety backup completed");
                }
                catch (Exception rollbackEx)
                {
                    _logger?.LogError(rollbackEx, "Rollback failed after restore failure");
                }
            }

            return new RestoreResult(false, $"فشل الاستعادة: {ex.Message}", 0);
        }
        finally
        {
            if (_db.Database.GetDbConnection().State != ConnectionState.Open)
            {
                await _db.Database.OpenConnectionAsync(ct);
            }
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken ct = default)
    {
        var files = Directory
            .GetFiles(_backupFolder, $"{BackupFilePrefix}*.db")
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.CreationTimeUtc)
            .Select(file => new BackupInfo(
                file.FullName,
                file.CreationTimeUtc,
                file.Length,
                $"نسخة {file.Name}"))
            .ToList();

        return Task.FromResult<IReadOnlyList<BackupInfo>>(files);
    }

    /// <inheritdoc />
    public Task DeleteBackupAsync(string backupPath, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }

        return Task.CompletedTask;
    }

    private static string ResolveBackupFolder(IConfiguration? configuration)
    {
        var configuredFolder = configuration?["Backup:Folder"];
        return string.IsNullOrWhiteSpace(configuredFolder)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups")
            : Path.GetFullPath(configuredFolder);
    }

    private string? GetDatabasePath()
    {
        var connectionString = _db.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || string.Equals(dataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Path.GetFullPath(dataSource);
    }

    /// <summary>ينتظر انتهاء المعاملات القصيرة الجارية قبل بدء اللقطة (بحد أقصى قصير).</summary>
    private async Task WaitForActiveTransactionsAsync(CancellationToken ct)
    {
        var sourcePath = GetDatabasePath();
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return;
        }

        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            if (!await IsWriteLockedAsync(sourcePath, ct))
            {
                return;
            }

            await Task.Delay(50, ct);
        }

        _logger?.LogWarning("Database still write-locked after {Attempts} attempts; continuing with consistent backup", maxAttempts);
    }

    private static async Task<bool> IsWriteLockedAsync(string databasePath, CancellationToken ct)
    {
        await using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        await connection.OpenAsync(ct);

        await using var busyCmd = connection.CreateCommand();
        busyCmd.CommandText = "PRAGMA busy_timeout = 0;";
        await busyCmd.ExecuteNonQueryAsync(ct);

        await using var beginCmd = connection.CreateCommand();
        beginCmd.CommandText = "BEGIN IMMEDIATE;";
        try
        {
            await beginCmd.ExecuteNonQueryAsync(ct);
        }
        catch (SqliteException)
        {
            return true;
        }

        await using var rollbackCmd = connection.CreateCommand();
        rollbackCmd.CommandText = "ROLLBACK;";
        await rollbackCmd.ExecuteNonQueryAsync(ct);

        return false;
    }

    /// <summary>ينفّذ WAL checkpoint؛ يعيد false عند وجود قرّاء نشطين بدل إفشال العملية.</summary>
    private async Task<bool> CheckpointWalAsync(CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        var openedHere = false;

        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await _db.Database.OpenConnectionAsync(ct);
                openedHere = true;
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            var result = await command.ExecuteScalarAsync(ct);

            var busy = result switch
            {
                long value => value,
                int value => value,
                _ => 0L,
            };

            return busy == 0;
        }
        catch (SqliteException ex)
        {
            _logger?.LogWarning(ex, "WAL checkpoint failed");
            return false;
        }
        finally
        {
            if (openedHere)
            {
                await _db.Database.CloseConnectionAsync();
            }
        }
    }

    /// <summary>يتحقق من تكامل الملف عبر PRAGMA integrity_check.</summary>
    private static async Task<(bool Success, string? Details, string? Error)> VerifyIntegrityAsync(
        string databasePath,
        CancellationToken ct)
    {
        try
        {
            await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
            await connection.OpenAsync(ct);

            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check;";
            var result = await command.ExecuteScalarAsync(ct) as string;

            if (string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                return (true, "OK", null);
            }

            return (false, result, result ?? "نتيجة integrity_check غير معروفة");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>يتحقق من وجود كل الجداول الأساسية في الملف.</summary>
    private static async Task<bool> VerifyRequiredTablesAsync(string databasePath, CancellationToken ct)
    {
        try
        {
            await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
            await connection.OpenAsync(ct);

            var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";

            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                tables.Add(reader.GetString(0));
            }

            return RequiredTables.IsSubsetOf(tables);
        }
        catch
        {
            return false;
        }
    }

    private static async Task<string> CalculateChecksumAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash);
    }

    private async Task<int> CountCoreEntitiesAsync(CancellationToken ct)
    {
        var stores = await _db.Stores.CountAsync(ct);
        var users = await _db.Users.CountAsync(ct);
        var products = await _db.Products.CountAsync(ct);
        var sales = await _db.Sales.CountAsync(ct);

        return stores + users + products + sales;
    }

    private static void DeleteSidecarFiles(string databasePath)
    {
        TryDelete(databasePath + "-wal");
        TryDelete(databasePath + "-shm");
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // الحذف أفضل جهد؛ لا نُفشل العملية الأساسية بسببه.
        }
    }
}
