namespace Wizz.Application.Modules;

/// <summary>
/// كتالوج محلي ثابت للوحدات الاختيارية. لا متجر إضافات عن بُعد،
/// ولا تنزيل تلقائي، ولا تحديث من الشبكة في هذه المرحلة.
/// </summary>
public sealed class ModuleCatalog
{
    private readonly IReadOnlyDictionary<string, ModuleDescriptor> _byId;

    public ModuleCatalog(IEnumerable<ModuleDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var list = descriptors.ToList();
        var duplicates = list
            .GroupBy(descriptor => descriptor.Id, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicates.Count > 0)
            throw new ArgumentException($"معرّفات وحدات مكرّرة: {string.Join("، ", duplicates)}", nameof(descriptors));

        var ids = list.Select(descriptor => descriptor.Id).ToHashSet(StringComparer.Ordinal);
        var missingDependencies = list
            .SelectMany(descriptor => descriptor.DependsOn.Select(dependency => (descriptor.Id, Dependency: dependency)))
            .Where(pair => !ids.Contains(pair.Dependency))
            .Select(pair => $"{pair.Id} ← {pair.Dependency}")
            .ToList();
        if (missingDependencies.Count > 0)
            throw new ArgumentException($"اعتمادات غير معروفة: {string.Join("، ", missingDependencies)}", nameof(descriptors));

        All = list.AsReadOnly();
        _byId = ids.Count == 0
            ? new Dictionary<string, ModuleDescriptor>(StringComparer.Ordinal)
            : list.ToDictionary(descriptor => descriptor.Id, StringComparer.Ordinal);
    }

    /// <summary>الكتالوج الفارغ المستخدم في المرحلة الأولى: الآلية بلا وحدات منقولة بعد.</summary>
    public static ModuleCatalog Empty { get; } = new(Array.Empty<ModuleDescriptor>());

    public IReadOnlyList<ModuleDescriptor> All { get; }

    public ModuleDescriptor? Find(string? moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId)) return null;
        return _byId.GetValueOrDefault(moduleId.Trim());
    }
}
