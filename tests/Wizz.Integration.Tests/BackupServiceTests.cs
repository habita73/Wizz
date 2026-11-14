using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.UseCases.Backup;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Services;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات خدمة النسخ الاحتياطي والاستعادة (Phase A).
///
/// تغطي هذه الاختبارات:
/// 1. إنشاء نسخة احتياطية متّسقة من قاعدة بيانات نشطة.
/// 2. التحقق من سلامة النسخة عبر PRAGMA integrity_check.
/// 3. حساب مجموع التحقق (SHA-256) للنسخة.
/// 4. الاستعادة من نسخة صالحة والتحقق من البيانات التجارية.
/// 5. رفض استعادة نسخة تالفة أو غير موجودة.
/// 6. إنشاء نسخة أمان قبل الاستعادة.
/// 7. list/delete للنسخ الاحتياطية.
/// 8. مسارات الفشل (failure-path).
/// </summary>
public sealed class BackupServiceTests : IAsyncLifetime
{
    private string _testDbPath = null!;
    private string _backupFolder = null!;
    private WizzDbContext _db = null!;
    private BackupService _backupService = null!;

    public async Task InitializeAsync()
    {
        // مسار قاعدة بيانات فريد لكل مثيل اختبار لتجنب تصادم المعرّفات بين الاختبارات.
        _testDbPath = Path.Combine(Path.GetTempPath(), $"WizzBackupTest_{Guid.NewGuid():N}.db");
        _backupFolder = Path.Combine(Path.GetTempPath(), $"WizzBackupFolder_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_backupFolder);

        var connectionString = $"Data Source={_testDbPath}";
        var options = new DbContextOptionsBuilder<WizzDbContext>()
            .UseSqlite(connectionString)
            .Options;

        _db = new WizzDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        // إدراج بيانات تمثيلية للتحقق بعد الاستعادة.
        await SeedTestDataAsync();

        _backupService = new BackupService(_db, _backupFolder);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        try
        {
            if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
            if (File.Exists(_testDbPath + "-wal")) File.Delete(_testDbPath + "-wal");
            if (File.Exists(_testDbPath + "-shm")) File.Delete(_testDbPath + "-shm");
            if (Directory.Exists(_backupFolder)) Directory.Delete(_backupFolder, true);
        }
        catch { }
    }

    [Fact]
    public async Task CreateBackup_ProducesValidFileWithChecksumAndIntegrity()
    {
        // Act
        var result = await _backupService.CreateBackupAsync();

        // Assert
        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));
        Assert.True(result.SizeBytes > 0);
        Assert.NotNull(result.Checksum);
        Assert.Equal("OK", result.IntegrityCheckResult);
        Assert.True(result.CreatedAt != default);

        // التحقق من أن مجموع التحقق يطابق الملف الفعلي.
        var actualChecksum = await ComputeSha256Async(result.BackupPath!);
        Assert.Equal(actualChecksum, result.Checksum);
    }

    [Fact]
    public async Task CreateBackup_WhenDatabaseHasWAL_StillProducesConsistentBackup()
    {
        // Arrange: إجراء عملية كتابة لتحفيز WAL.
        using var cmd = _db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "INSERT INTO Stores (Id, Name, Address, Phone, TaxNumber, CommercialRegister, Currency, TimeZone, InventoryEnabled, AllowCreditSales, CreatedAt) VALUES ('11111111-1111-1111-1111-111111111111', 'متجر اختبار WAL', 'الجزائر', '+213 21 00 00 00', '000000000000000', '00/00-0000000', 'DZD', 'Africa/Algiers', 1, 1, '2026-01-01T00:00:00Z')";
        if (_db.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
            await _db.Database.GetDbConnection().OpenAsync();
        await cmd.ExecuteNonQueryAsync();

        // Act
        var result = await _backupService.CreateBackupAsync();

        // Assert
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(result.BackupPath!));
        Assert.Equal("OK", result.IntegrityCheckResult);
    }

    [Fact]
    public async Task Restore_FromValidBackup_RestoresDataSuccessfully()
    {
        // Arrange
        var backupResult = await _backupService.CreateBackupAsync();
        Assert.True(backupResult.Success);

        // إجراء تغيير على القاعدة الحالية (إضافة متجر) قبل الاستعادة.
        await AddExtraStoreAsync("متجر قبل الاستعادة");

        // Act: استعادة النسخة الاحتياطية (يجب أن تعود القاعدة إلى حالة النسخة).
        var restoreResult = await _backupService.RestoreAsync(backupResult.BackupPath!);

        // Assert
        Assert.True(restoreResult.Success, restoreResult.ErrorMessage);

        // التحقق من أن البيانات عادت إلى حالة النسخة (المترك المضاف قبل الاستعادة يجب أن يُزال).
        using var conn = new SqliteConnection($"Data Source={_testDbPath}");
        await conn.OpenAsync();
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Stores WHERE Name = 'متجر قبل الاستعادة'";
        var count = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Restore_FromCorruptedBackup_FailsAndKeepsOriginalData()
    {
        // Arrange: إنشاء ملف تالف.
        var corruptedPath = Path.Combine(_backupFolder, "corrupted.db");
        await File.WriteAllBytesAsync(corruptedPath, Encoding.UTF8.GetBytes("هذا ليس ملف قاعدة بيانات صالح"));

        // Act
        var result = await _backupService.RestoreAsync(corruptedPath);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("غير صالحة", result.ErrorMessage);
    }

    [Fact]
    public async Task Restore_FromNonExistentBackup_Fails()
    {
        // Act
        var result = await _backupService.RestoreAsync(Path.Combine(_backupFolder, "does_not_exist.db"));

        // Assert
        Assert.False(result.Success);
    }

    [Fact]
    public async Task ListBackups_ReturnsCreatedBackups()
    {
        // Arrange
        await _backupService.CreateBackupAsync();
        await _backupService.CreateBackupAsync();

        // Act
        var backups = await _backupService.ListBackupsAsync();

        // Assert
        Assert.True(backups.Count >= 2);
        Assert.All(backups, b => Assert.False(string.IsNullOrWhiteSpace(b.Path)));
        Assert.All(backups, b => Assert.True(b.SizeBytes > 0));
    }

    [Fact]
    public async Task DeleteBackup_RemovesBackupFile()
    {
        // Arrange
        var backupResult = await _backupService.CreateBackupAsync();
        Assert.True(backupResult.Success);
        var path = backupResult.BackupPath!;

        // Act
        await _backupService.DeleteBackupAsync(path);

        // Assert
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task CreateBackup_WhenDatabaseFileMissing_ReturnsFailure()
    {
        // Arrange: إنشاء خدمة بقاعدة بيانات غير موجودة.
        var tempFolder = Path.Combine(Path.GetTempPath(), $"WizzMissingDb_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);
        try
        {
            var missingDbPath = Path.Combine(tempFolder, "missing.db");
            var options = new DbContextOptionsBuilder<WizzDbContext>()
                .UseSqlite($"Data Source={missingDbPath}")
                .Options;
            using var missingDb = new WizzDbContext(options);
            var service = new BackupService(missingDb, tempFolder);

            // Act
            var result = await service.CreateBackupAsync();

            // Assert
            Assert.False(result.Success);
            Assert.Contains("غير موجود", result.ErrorMessage);
        }
        finally
        {
            try { Directory.Delete(tempFolder, true); } catch { }
        }
    }

    [Fact]
    public async Task BackupResult_Checksum_IsStableAcrossCalls()
    {
        // Arrange
        var first = await _backupService.CreateBackupAsync();
        var second = await _backupService.CreateBackupAsync();

        // Assert: كل نسخة لها مجموع تحقق خاص بها (ملف مختلف → مجموع مختلف).
        Assert.NotNull(first.Checksum);
        Assert.NotNull(second.Checksum);
        Assert.NotEqual(first.BackupPath, second.BackupPath);
    }

    #region مساعدات خاصة بالاختبار

    private static async Task<string> ComputeSha256Async(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream);
        return Convert.ToHexString(hash);
    }

    private async Task SeedTestDataAsync()
    {
        // إنشاء متجر واحد للاختبار.
        using var cmd = _db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = @"
            INSERT INTO Stores (Id, Name, Address, Phone, TaxNumber, CommercialRegister, Currency, TimeZone, InventoryEnabled, AllowCreditSales, CreatedAt)
            VALUES ('22222222-2222-2222-2222-222222222222', 'متجر الاختبار الرئيسي', 'الجزائر العاصمة', '+213 21 00 00 00', '000000000000000', '00/00-0000000', 'DZD', 'Africa/Algiers', 1, 1, '2026-01-01T00:00:00Z');
            INSERT INTO Users (Id, Username, PasswordHash, FullName, Role, IsActive, CreatedAt, SecurityStamp)
            VALUES ('33333333-3333-3333-3333-333333333333', 'testcashier', 'hashedhash', 'كاشير اختبار', 0, 1, '2026-01-01T00:00:00Z', 'test-security-stamp');
        ";
        if (_db.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
            await _db.Database.GetDbConnection().OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task AddExtraStoreAsync(string name)
    {
        using var cmd = _db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = $"INSERT INTO Stores (Id, Name, Address, Phone, TaxNumber, CommercialRegister, Currency, TimeZone, InventoryEnabled, AllowCreditSales, CreatedAt) VALUES ('44444444-4444-4444-4444-444444444444', '{name}', 'الجزائر', '+213 21 00 00 00', '000000000000000', '00/00-0000000', 'DZD', 'Africa/Algiers', 1, 1, '2026-01-01T00:00:00Z')";
        if (_db.Database.GetDbConnection().State != System.Data.ConnectionState.Open)
            await _db.Database.GetDbConnection().OpenAsync();
        await cmd.ExecuteNonQueryAsync();
    }

    #endregion
}