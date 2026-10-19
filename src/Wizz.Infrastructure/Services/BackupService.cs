namespace Wizz.Infrastructure.Services;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Infrastructure.Data;

/// <summary>خدمة النسخ الاحتياطي والاستعادة لـ SQLite.</summary>
public sealed class BackupService : IBackupService
{
    private readonly WizzDbContext _db;
    private readonly string _backupFolder;

    public BackupService(WizzDbContext db, string? backupFolder = null)
    {
        _db = db;
        _backupFolder = backupFolder ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Backups");
        Directory.CreateDirectory(_backupFolder);
    }

    public async Task<BackupResult> CreateBackupAsync(CancellationToken ct = default)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = $"wizz_backup_{timestamp}.db";
            var backupPath = Path.Combine(_backupFolder, fileName);

            // الحصول على مسار قاعدة البيانات المصدر
            var sourcePath = GetDatabasePath();
            if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath))
                return new BackupResult(false, null, "ملف قاعدة البيانات غير موجود", 0, DateTime.UtcNow);

            // نسخ الملف (SQLite يدعم النسخ المباشر للملف)
            File.Copy(sourcePath, backupPath, true);

            var info = new FileInfo(backupPath);
            return new BackupResult(true, backupPath, null, info.Length, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            return new BackupResult(false, null, $"فشل النسخ: {ex.Message}", 0, DateTime.UtcNow);
        }
    }

    public async Task<RestoreResult> RestoreAsync(string backupPath, CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(backupPath))
                return new RestoreResult(false, "ملف النسخة الاحتياطية غير موجود", 0);

            var targetPath = GetDatabasePath();
            if (string.IsNullOrEmpty(targetPath))
                return new RestoreResult(false, "تعذر تحديد مسار قاعدة البيانات الهدف", 0);

            // إغلاق الاتصالات الحالية
            await _db.Database.CloseConnectionAsync();

            // نسخ ملف النسخة الاحتياطية فوق الملف الحالي
            File.Copy(backupPath, targetPath, true);

            // إعادة فتح الاتصال
            await _db.Database.OpenConnectionAsync();

            return new RestoreResult(true, null, 1); // عدد الكيانات المستعادة تقريبي
        }
        catch (Exception ex)
        {
            return new RestoreResult(false, $"فشل الاستعادة: {ex.Message}", 0);
        }
    }

    public async Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken ct = default)
    {
        var files = Directory.GetFiles(_backupFolder, "wizz_backup_*.db")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupInfo(f.FullName, f.CreationTimeUtc, f.Length, $"نسخة {f.Name}"))
            .ToList();

        return files;
    }

    public async Task DeleteBackupAsync(string backupPath, CancellationToken ct = default)
    {
        if (File.Exists(backupPath))
            File.Delete(backupPath);
    }

    private string? GetDatabasePath()
    {
        var connectionString = _db.Database.GetConnectionString();
        if (string.IsNullOrEmpty(connectionString)) return null;

        // استخراج المسار من connection string: "Data Source=path.db"
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase))
                return trimmed["Data Source=".Length..].Trim();
        }
        return null;
    }
}