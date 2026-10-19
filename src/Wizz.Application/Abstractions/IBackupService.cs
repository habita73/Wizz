namespace Wizz.Application.Abstractions;

/// <summary>خدمة النسخ الاحتياطي والاستعادة.</summary>
public interface IBackupService
{
    /// <summary>إنشاء نسخة احتياطية — يعيد المسار أو بيانات النسخة.</summary>
    Task<BackupResult> CreateBackupAsync(CancellationToken ct = default);

    /// <summary>استعادة من نسخة احتياطية.</summary>
    Task<RestoreResult> RestoreAsync(string backupPath, CancellationToken ct = default);

    /// <summary>قائمة النسخ الاحتياطية المتاحة.</summary>
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken ct = default);

    /// <summary>حذف نسخة احتياطية.</summary>
    Task DeleteBackupAsync(string backupPath, CancellationToken ct = default);
}

public sealed record BackupResult(bool Success, string? BackupPath, string? ErrorMessage, long SizeBytes, DateTime CreatedAt);

public sealed record RestoreResult(bool Success, string? ErrorMessage, int RestoredEntitiesCount);

public sealed record BackupInfo(string Path, DateTime CreatedAt, long SizeBytes, string Description);