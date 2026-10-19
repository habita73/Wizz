namespace Wizz.Application.UseCases.Backup;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;

/// <summary>إنشاء نسخة احتياطية من قاعدة البيانات.</summary>
public sealed class BackupDatabaseUseCase
{
    private readonly IBackupService _backup;

    public BackupDatabaseUseCase(IBackupService backup) => _backup = backup;

    public async Task<Result<BackupResult>> ExecuteAsync(CancellationToken ct = default)
    {
        var result = await _backup.CreateBackupAsync(ct);
        if (!result.Success)
            return Result<BackupResult>.Failure(result.ErrorMessage ?? "فشل إنشاء النسخة الاحتياطية");

        return Result<BackupResult>.Success(result);
    }
}