namespace Wizz.Application.Modules;

using Wizz.Application.Abstractions;

/// <summary>
/// بوابة تشغيل الوحدات. تحمي من تفعيل وحدة غير معروفة أو تحتاج مؤهّلًا
/// غير موجود، حتى لو حاول الملف المحلي تفعيلها.
/// </summary>
public sealed class ModuleGate : IModuleGate
{
    private readonly ModuleCatalog _catalog;
    private readonly IModuleStateStore _store;

    public ModuleGate(ModuleCatalog catalog, IModuleStateStore store)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public bool IsEnabled(string moduleId)
    {
        var descriptor = _catalog.Find(moduleId);
        if (descriptor is null) return false;
        if (UnavailabilityReason(descriptor) is not null) return false;
        return _store.IsEnabled(descriptor.Id);
    }

    public ModuleStatus? GetStatus(string moduleId)
    {
        var descriptor = _catalog.Find(moduleId);
        return descriptor is null ? null : BuildStatus(descriptor);
    }

    public IReadOnlyList<ModuleStatus> GetAllStatuses()
        => _catalog.All.Select(BuildStatus).ToList().AsReadOnly();

    private ModuleStatus BuildStatus(ModuleDescriptor descriptor)
    {
        var reason = UnavailabilityReason(descriptor);
        var enabled = reason is null && _store.IsEnabled(descriptor.Id);
        return new ModuleStatus(
            descriptor,
            enabled,
            reason is null ? ModuleAvailability.Available : ModuleAvailability.Unavailable,
            reason);
    }

    private static string? UnavailabilityReason(ModuleDescriptor descriptor)
        => descriptor.LicenseCapability == ModuleLicenseCapability.RequiresEntitlement
            ? "تتطلب مؤهّل ترخيص، ونظام المؤهّلات لم يُفعّل بعد."
            : null;
}
