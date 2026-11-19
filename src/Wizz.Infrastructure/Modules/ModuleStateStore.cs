namespace Wizz.Infrastructure.Modules;

using System.Text.Json;
using Wizz.Application.Abstractions;

/// <summary>
/// حفظ حالة الوحدات في ملف محلي داخل مجلد بيانات المستخدم، بنفس نمط
/// إعدادات الذكاء الاصطناعي. لا قاعدة بيانات ولا ترحيل في هذه المرحلة.
/// </summary>
public sealed class ModuleStateStore : IModuleStateStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, bool> _states;

    public ModuleStateStore() : this(ResolvePath())
    {
    }

    public ModuleStateStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("مسار حفظ حالة الوحدات مطلوب", nameof(path));
        _path = path;
        _states = Load(path);
    }

    internal static string ResolvePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("WIZZ_MODULE_SETTINGS_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir)) baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Wizz", "module-settings.json");
    }

    public bool IsEnabled(string moduleId)
    {
        if (string.IsNullOrWhiteSpace(moduleId)) return false;
        lock (_gate)
        {
            return _states.TryGetValue(moduleId.Trim(), out var enabled) && enabled;
        }
    }

    public IReadOnlyDictionary<string, bool> GetAll()
    {
        lock (_gate)
        {
            return new Dictionary<string, bool>(_states, StringComparer.Ordinal);
        }
    }

    public void SetEnabled(string moduleId, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(moduleId)) throw new ArgumentException("معرّف الوحدة مطلوب", nameof(moduleId));
        var id = moduleId.Trim();
        lock (_gate)
        {
            if (_states.TryGetValue(id, out var current) && current == enabled) return;
            _states[id] = enabled;
            Save();
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(_states, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch
        {
            // تعذّر الحفظ لا يجب أن يوقف التطبيق أو يخفي الحالة في الذاكرة.
        }
    }

    private static Dictionary<string, bool> Load(string path)
    {
        var result = new Dictionary<string, bool>(StringComparer.Ordinal);
        try
        {
            if (!File.Exists(path)) return result;
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return result;

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return result;

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name)) continue;
                if (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    result[property.Name.Trim()] = property.Value.GetBoolean();
                }
            }
        }
        catch
        {
            // ملف تالف أو غير مقروء — نبدأ بالحالة الافتراضية: كل الوحدات مطفأة.
            return new Dictionary<string, bool>(StringComparer.Ordinal);
        }

        return result;
    }
}
