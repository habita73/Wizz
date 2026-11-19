namespace Wizz.Application.Abstractions;

/// <summary>
/// حفظ حالة تفعيل الوحدات محليًا. غياب السجل لأي وحدة يعني أنها مطفأة.
/// التعطيل لا يحذف بيانات الوحدة.
/// </summary>
public interface IModuleStateStore
{
    bool IsEnabled(string moduleId);
    IReadOnlyDictionary<string, bool> GetAll();
    void SetEnabled(string moduleId, bool enabled);
}
