namespace Wizz.Application.UseCases.Backup;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;

/// <summary>استعادة قاعدة بيانات من نسخة احتياطية.</summary>
public sealed class RestoreDatabaseUseCase
{
    private readonly IBackupService _backup;

    public RestoreDatabaseUseCase(IBackupService backup) => _backup = backup;

    public async Task<Result<RestoreResult>> ExecuteAsync(string backupPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath))
            return Result<RestoreResult>.Failure("مسار النسخة الاحتياطية مطلوب");

        var result = await _backup.RestoreAsync(backupPath, ct);
        if (!result.Success)
            return Result<RestoreResult>.Failure(result.ErrorMessage ?? "فشل استعادة النسخة الاحتياطية");

        return Result<RestoreResult>.Success(result);
    }
}