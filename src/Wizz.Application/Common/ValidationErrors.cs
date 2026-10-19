namespace Wizz.Application.Common;

/// <summary>
/// حاوية لأخطاء التحقق — تستخدم في DTOs وواجهات الإدخال.
/// </summary>
public sealed class ValidationErrors : Dictionary<string, List<string>>
{
    public void Add(string field, string error)
    {
        if (!TryGetValue(field, out var list))
        {
            list = new List<string>();
            base[field] = list;
        }
        list.Add(error);
    }

    public bool HasErrors => Count > 0;

    public IReadOnlyList<string> GetAllErrors() => Values.SelectMany(v => v).ToList();

    public override string ToString() => HasErrors
        ? string.Join("; ", this.SelectMany(kvp => kvp.Value.Select(e => $"{kvp.Key}: {e}")))
        : "لا توجد أخطاء";
}