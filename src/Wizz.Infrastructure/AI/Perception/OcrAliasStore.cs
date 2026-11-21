namespace Wizz.Infrastructure.AI.Perception;

using System.Text.Json;
using Wizz.Application.AI;
using Wizz.Application.AI.Perception;

/// <summary>
/// يحفظ مرادفات الأسماء التي أكّدها المستخدم في ملف JSON محلي داخل مجلد بيانات المستخدم.
/// لا قاعدة بيانات، لا إرسال، ولا أي بيانات من الفاتورة غير اسم الصنف نفسه.
/// </summary>
public sealed class OcrAliasStore : IOcrAliasStore
{
    private const int MaxAliases = 500;

    private readonly string _path;
    private readonly object _gate = new();
    private List<OcrAlias> _aliases;

    public OcrAliasStore() : this(ResolvePath())
    {
    }

    public OcrAliasStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("مسار حفظ الأسماء مطلوب", nameof(path));
        _path = path;
        _aliases = Load(path);
    }

    internal static string ResolvePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("WIZZ_OCR_ALIAS_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir)) baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Wizz", "ocr-name-aliases.json");
    }

    public IReadOnlyList<OcrAlias> GetAll()
    {
        lock (_gate)
        {
            return _aliases.Select(a => a with { }).ToArray();
        }
    }

    public string? Resolve(string? rawName)
    {
        var key = TextNormalizer.Normalize(rawName);
        if (key.Length == 0) return null;

        lock (_gate)
        {
            return _aliases.FirstOrDefault(a => string.Equals(TextNormalizer.Normalize(a.RawName), key, StringComparison.Ordinal))?.ProductName;
        }
    }

    public void Remember(string rawName, string productName)
    {
        var raw = rawName?.Trim();
        var product = productName?.Trim();
        if (string.IsNullOrWhiteSpace(raw) || string.IsNullOrWhiteSpace(product)) return;

        lock (_gate)
        {
            var key = TextNormalizer.Normalize(raw);
            var index = _aliases.FindIndex(a => string.Equals(TextNormalizer.Normalize(a.RawName), key, StringComparison.Ordinal));
            if (index >= 0)
            {
                var old = _aliases[index];
                _aliases[index] = old with { ProductName = product, Confirmations = old.Confirmations + 1 };
            }
            else
            {
                if (_aliases.Count >= MaxAliases) _aliases.RemoveAt(0);
                _aliases.Add(new OcrAlias(raw, product, 1));
            }

            SaveToFile();
        }
    }

    public void Forget(string rawName)
    {
        var key = TextNormalizer.Normalize(rawName);
        if (key.Length == 0) return;

        lock (_gate)
        {
            _aliases.RemoveAll(a => string.Equals(TextNormalizer.Normalize(a.RawName), key, StringComparison.Ordinal));
            SaveToFile();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _aliases.Clear();
            SaveToFile();
        }
    }

    private void SaveToFile()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_path, JsonSerializer.Serialize(_aliases, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // تعذّر الحفظ لا يوقف التطبيق؛ تبقى القائمة في الذاكرة لهذه الجلسة.
        }
    }

    private static List<OcrAlias> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new List<OcrAlias>();
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return new List<OcrAlias>();
            var items = JsonSerializer.Deserialize<List<OcrAlias>>(json);
            return items?.Where(a => !string.IsNullOrWhiteSpace(a.RawName) && !string.IsNullOrWhiteSpace(a.ProductName)).ToList()
                   ?? new List<OcrAlias>();
        }
        catch
        {
            // ملف تالف — نبدأ بقائمة فارغة ولا نوقف التطبيق.
            return new List<OcrAlias>();
        }
    }
}
