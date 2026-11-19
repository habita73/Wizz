namespace Wizz.Application.Modules;

/// <summary>مجموعة الوحدة داخل كتالوج الإعدادات.</summary>
public enum ModuleGroup
{
    Sales = 0,
    Data = 1,
    Documents = 2,
    Integrations = 3,
}

/// <summary>
/// مؤهّل ترخيص تقني فقط: لا يحمل سعرًا ولا اسم باقة ولا حدّ حصّة.
/// </summary>
public enum ModuleLicenseCapability
{
    None = 0,
    Included = 1,
    RequiresEntitlement = 2,
}

/// <summary>
/// بطاقة تعريف وحدة اختيارية. الحالة الافتراضية لأي وحدة «مطفأة»،
/// والوحدة لا تكتب في البيانات المالية أو المخزونية تلقائيًا.
/// </summary>
public sealed class ModuleDescriptor
{
    public string Id { get; }
    public string NameAr { get; }
    public string DescriptionAr { get; }
    public ModuleGroup Group { get; }
    public string Version { get; }
    public IReadOnlyList<string> DependsOn { get; }
    public IReadOnlyList<string> RequiredPermissions { get; }
    public IReadOnlyList<string> RequirementsAr { get; }
    public ModuleLicenseCapability LicenseCapability { get; }

    public ModuleDescriptor(
        string id,
        string nameAr,
        string descriptionAr,
        ModuleGroup group,
        string version,
        IEnumerable<string>? dependsOn = null,
        IEnumerable<string>? requiredPermissions = null,
        IEnumerable<string>? requirementsAr = null,
        ModuleLicenseCapability licenseCapability = ModuleLicenseCapability.None)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("معرّف الوحدة مطلوب", nameof(id));
        if (string.IsNullOrWhiteSpace(nameAr)) throw new ArgumentException("اسم الوحدة مطلوب", nameof(nameAr));
        if (string.IsNullOrWhiteSpace(descriptionAr)) throw new ArgumentException("وصف الوحدة مطلوب", nameof(descriptionAr));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("إصدار الوحدة مطلوب", nameof(version));

        var normalizedId = id.Trim();
        if (normalizedId.Any(char.IsWhiteSpace))
            throw new ArgumentException("معرّف الوحدة لا يقبل مسافات", nameof(id));

        Id = normalizedId;
        NameAr = nameAr.Trim();
        DescriptionAr = descriptionAr.Trim();
        Group = group;
        Version = version.Trim();
        DependsOn = Freeze(dependsOn);
        RequiredPermissions = Freeze(requiredPermissions);
        RequirementsAr = Freeze(requirementsAr);
        LicenseCapability = licenseCapability;

        if (DependsOn.Contains(Id, StringComparer.Ordinal))
            throw new ArgumentException("لا يجوز أن تعتمد الوحدة على نفسها", nameof(dependsOn));
    }

    private static IReadOnlyList<string> Freeze(IEnumerable<string>? values)
    {
        if (values is null) return Array.Empty<string>();
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList()
            .AsReadOnly();
    }
}
