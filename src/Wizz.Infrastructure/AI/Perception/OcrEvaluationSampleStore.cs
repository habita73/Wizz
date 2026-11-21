namespace Wizz.Infrastructure.AI.Perception;

using System.Text.Json;
using Wizz.Application.AI.Perception;

/// <summary>
/// يقرأ عينات التقييم من ملف JSON محلي، ويُنشئه بأمثلة اصطناعية عند أول تشغيل.
/// لا بيانات حقيقية، ولا قاعدة بيانات، ولا إرسال.
/// </summary>
public sealed class OcrEvaluationSampleStore : IOcrEvaluationSampleStore
{
    private static readonly OcrEvaluationSample[] Seeds =
    {
        new("synthetic-1", "خبز 5.00\nحليب 12.50", "خبز", 5.00m),
        new("synthetic-2", "أرز 150\nزيت 85", "أرز", 150m),
        new("synthetic-3", "مناديل 30", "مناديل", 30m),
        new("synthetic-4", "2 تفاح 40", "تفاح", 40m),
        new("synthetic-5", "الارز 12.50", "أرز", 12.50m)
    };

    private readonly string _path;
    private readonly object _gate = new();
    private IReadOnlyList<OcrEvaluationSample>? _cache;

    public OcrEvaluationSampleStore() : this(ResolvePath())
    {
    }

    public OcrEvaluationSampleStore(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("مسار عينات التقييم مطلوب", nameof(path));
        _path = path;
    }

    internal static string ResolvePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("WIZZ_OCR_EVAL_SAMPLES_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir)) baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Wizz", "ocr-evaluation-samples.json");
    }

    public IReadOnlyList<OcrEvaluationSample> GetAll()
    {
        lock (_gate)
        {
            if (_cache is not null) return _cache;

            try
            {
                if (!File.Exists(_path))
                {
                    var directory = Path.GetDirectoryName(_path);
                    if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                    File.WriteAllText(_path, JsonSerializer.Serialize(Seeds, new JsonSerializerOptions { WriteIndented = true }));
                    _cache = Seeds;
                    return _cache;
                }

                var json = File.ReadAllText(_path);
                var items = string.IsNullOrWhiteSpace(json)
                    ? null
                    : JsonSerializer.Deserialize<List<OcrEvaluationSample>>(json);
                _cache = items is { Count: > 0 }
                    ? items.Where(s => !string.IsNullOrWhiteSpace(s.Text) && !string.IsNullOrWhiteSpace(s.ExpectedProduct)).ToArray()
                    : Seeds;
            }
            catch
            {
                // ملف تالف أو تعذّر كتابة — نستخدم العينات الاصطناعية المضمّنة.
                _cache = Seeds;
            }

            return _cache;
        }
    }
}
