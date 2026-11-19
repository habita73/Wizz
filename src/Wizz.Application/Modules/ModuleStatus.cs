namespace Wizz.Application.Modules;

/// <summary>مدى توفر الوحدة على هذا الجهاز/الترخيص.</summary>
public enum ModuleAvailability
{
    Available = 0,
    Unavailable = 1,
}

/// <summary>الحالة المعروضة لكل وحدة في شاشة الإعدادات.</summary>
public sealed record ModuleStatus(
    ModuleDescriptor Descriptor,
    bool Enabled,
    ModuleAvailability Availability,
    string? ReasonAr);
