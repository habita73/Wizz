namespace Wizz.Application.Abstractions;

using Wizz.Application.Modules;

/// <summary>
/// بوابة التشغيل: الوحدة المطفأة أو غير المتاحة لا تُسجَّل خدماتها
/// ولا تُستهلك مواردها على الجهاز.
/// </summary>
public interface IModuleGate
{
    bool IsEnabled(string moduleId);
    ModuleStatus? GetStatus(string moduleId);
    IReadOnlyList<ModuleStatus> GetAllStatuses();
}
