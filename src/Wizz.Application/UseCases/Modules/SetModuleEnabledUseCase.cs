namespace Wizz.Application.UseCases.Modules;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.Modules;
using Wizz.Domain;

/// <summary>
/// تفعيل أو تعطيل وحدة اختيارية. العملية حساسة وتُسجَّل في سجل التدقيق،
/// والتعطيل لا يحذف بيانات الوحدة.
/// </summary>
public sealed class SetModuleEnabledUseCase
{
    private readonly ModuleCatalog _catalog;
    private readonly IModuleStateStore _store;
    private readonly IModuleGate _gate;
    private readonly IAuditLogger _audit;

    public SetModuleEnabledUseCase(
        ModuleCatalog catalog,
        IModuleStateStore store,
        IModuleGate gate,
        IAuditLogger audit)
    {
        _catalog = catalog;
        _store = store;
        _gate = gate;
        _audit = audit;
    }

    public async Task<Result> ExecuteAsync(
        string moduleId,
        bool enabled,
        Guid? userId,
        string? username,
        CancellationToken ct = default)
    {
        var descriptor = _catalog.Find(moduleId);
        if (descriptor is null)
            return Result.Failure("وحدة غير معروفة");

        var status = _gate.GetStatus(descriptor.Id);
        if (status is null || status.Availability == ModuleAvailability.Unavailable)
            return Result.Failure(status?.ReasonAr ?? "الوحدة غير متاحة");

        if (status.Enabled == enabled)
            return Result.Success();

        _store.SetEnabled(descriptor.Id, enabled);

        await _audit.LogAsync(new AuditEntry(
            enabled ? AuditAction.ModuleEnabled : AuditAction.ModuleDisabled,
            AuditOutcome.Success,
            userId,
            username,
            "Module",
            descriptor.Id,
            $"وحدة: {descriptor.NameAr} — الحالة: {(enabled ? "مفعّلة" : "معطّلة")}"),
            ct);

        return Result.Success();
    }
}
