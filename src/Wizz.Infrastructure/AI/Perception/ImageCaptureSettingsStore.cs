namespace Wizz.Infrastructure.AI.Perception;

using System.Text.Json;
using Wizz.Application.Abstractions;
using Wizz.Application.AI.Perception;

/// <summary>
/// يحفظ تخصيصات التقاط الصورة في ملف JSON محلي داخل مجلد بيانات المستخدم، بنفس نمط
/// حالة الوحدات الاختيارية. لا قاعدة بيانات، لا ترحيل، ولا إرسال لأي جهة.
/// تعذّر القراءة أو الكتابة يعود بالقيم الافتراضية ولا يوقف التطبيق.
/// </summary>
public sealed class ImageCaptureSettingsStore : IImageCaptureSettingsStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private ImageCaptureSettings _settings;

    public ImageCaptureSettingsStore() : this(ResolvePath())
    {
    }

    public ImageCaptureSettingsStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("مسار حفظ إعدادات التقاط الصورة مطلوب", nameof(path));
        _path = path;
        _settings = Load(path);
    }

    internal static string ResolvePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("WIZZ_IMAGE_CAPTURE_SETTINGS_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir)) baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Wizz", "image-capture-settings.json");
    }

    public ImageCaptureSettings Get()
    {
        lock (_gate)
        {
            return _settings;
        }
    }

    public void Save(ImageCaptureSettings settings)
    {
        lock (_gate)
        {
            if (_settings == settings) return;
            _settings = settings;
            SaveToFile();
        }
    }

    private void SaveToFile()
    {
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            // الشدة تُكتب بالاسم لا برقمها، ليظل الملف مقروءًا عند فحصه يدويًا.
            var payload = new
            {
                _settings.EnhanceEnabled,
                _settings.PreviewBeforeAnalysis,
                _settings.ShowEnhancedPreview,
                EnhanceLevel = _settings.EnhanceLevel.ToString()
            };
            File.WriteAllText(_path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // تعذّر الحفظ لا يوقف التطبيق؛ تبقى القيم في الذاكرة لهذه الجلسة.
        }
    }

    private static ImageCaptureSettings Load(string path)
    {
        var fallback = ImageCaptureSettings.Default;
        try
        {
            if (!File.Exists(path)) return fallback;
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return fallback;

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return fallback;

            bool Read(string name, bool defaultValue) =>
                document.RootElement.TryGetProperty(name, out var value)
                && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                    ? value.GetBoolean()
                    : defaultValue;

            // الملفات المكتوبة قبل إضافة المستويات بلا هذا الحقل؛ تعود إلى الشدة السابقة.
            ImageEnhanceLevel ReadLevel()
            {
                if (!document.RootElement.TryGetProperty(nameof(ImageCaptureSettings.EnhanceLevel), out var value))
                    return fallback.EnhanceLevel;

                if (value.ValueKind == JsonValueKind.String
                    && Enum.TryParse(value.GetString(), ignoreCase: true, out ImageEnhanceLevel parsed)
                    && Enum.IsDefined(parsed))
                    return parsed;

                if (value.ValueKind == JsonValueKind.Number
                    && value.TryGetInt32(out var number)
                    && Enum.IsDefined((ImageEnhanceLevel)number))
                    return (ImageEnhanceLevel)number;

                return fallback.EnhanceLevel;
            }

            return new ImageCaptureSettings(
                Read(nameof(ImageCaptureSettings.EnhanceEnabled), fallback.EnhanceEnabled),
                Read(nameof(ImageCaptureSettings.PreviewBeforeAnalysis), fallback.PreviewBeforeAnalysis),
                Read(nameof(ImageCaptureSettings.ShowEnhancedPreview), fallback.ShowEnhancedPreview),
                ReadLevel());
        }
        catch
        {
            // ملف تالف أو غير مقروء — نبدأ بالقيم الافتراضية.
            return fallback;
        }
    }
}
