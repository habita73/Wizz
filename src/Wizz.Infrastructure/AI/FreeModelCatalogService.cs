namespace Wizz.Infrastructure.AI;

using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wizz.Application.AI;
using Wizz.Application.AI.Perception;

/// <summary>
/// يقرأ كتالوج النماذج العام من نقطة <c>/models</c> في مزوّد OpenRouter.
/// يُرسل GET عامًا فقط: بلا مفتاح API، بلا سؤال، بلا صورة، بلا بيانات متجر.
/// يحتفظ بلقطة محلية بتوقيتها، وعند الفشل يُبقي اللقطة السابقة ويسجّل السبب.
/// الترتيب تخمين بحسب البيانات العامة (مجاني، ساري، يدعم الصورة/النص، سياق أكبر)
/// وليس قياسًا لدقة العربية على وصل حقيقي.
/// </summary>
public sealed class FreeModelCatalogService : IFreeModelCatalogService
{
    private static readonly TimeSpan CatalogTimeout = TimeSpan.FromSeconds(12);
    private static readonly int MaxCandidates = 8;

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FreeModelCatalogService>? _logger;
    private readonly string _cachePath;
    private readonly object _gate = new();
    private FreeModelCatalogSnapshot? _cached;

    public FreeModelCatalogService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<FreeModelCatalogService>? logger = null,
        string? cachePath = null)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _cachePath = string.IsNullOrWhiteSpace(cachePath) ? ResolveCachePath() : cachePath;
        _cached = LoadCache();
    }

    public FreeModelCatalogSnapshot? Cached
    {
        get { lock (_gate) return _cached; }
    }

    public async Task<FreeModelCatalogSnapshot?> RefreshAsync(CancellationToken ct = default)
    {
        var provider = _configuration["AI:PreferredCloudProvider"];
        if (string.IsNullOrWhiteSpace(provider)) provider = "openrouter";
        var baseUrl = _configuration[$"AI:CloudProviders:{provider}:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "https://openrouter.ai/api/v1";
        var source = baseUrl.TrimEnd('/') + "/models";

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(CatalogTimeout);
            using var response = await _httpClient.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                return RecordFailure(source, $"المصدر أعاد {response.StatusCode}");
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var snapshot = ParseCatalog(json, source, DateTimeOffset.UtcNow);
            lock (_gate)
            {
                _cached = snapshot;
                SaveCache(snapshot);
            }
            _logger?.LogInformation("تحديث كتالوج النماذج: {Chat} محادثة، {Vision} رؤية", snapshot.ChatCandidates.Count, snapshot.VisionCandidates.Count);
            return snapshot;
        }
        catch (OperationCanceledException)
        {
            return RecordFailure(source, "انتهت مهلة قراءة الكتالوج");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "تعذّرت قراءة كتالوج النماذج العامة");
            return RecordFailure(source, ex.Message);
        }
    }

    private FreeModelCatalogSnapshot RecordFailure(string source, string reason)
    {
        lock (_gate)
        {
            if (_cached is not null)
            {
                _cached = _cached with { LastError = reason, Source = source };
                return _cached;
            }
            var empty = new FreeModelCatalogSnapshot(
                DateTimeOffset.UtcNow, source,
                Array.Empty<FreeModelSuggestion>(), Array.Empty<FreeModelSuggestion>(), reason);
            _cached = empty;
            return empty;
        }
    }

    /// <summary>تحليل استجابة الكتالوج العامة. داخلي ليُختبر ببيانات اصطناعية بلا شبكة.</summary>
    internal static FreeModelCatalogSnapshot ParseCatalog(string json, string source, DateTimeOffset fetchedAt)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return new FreeModelCatalogSnapshot(fetchedAt, source,
                Array.Empty<FreeModelSuggestion>(), Array.Empty<FreeModelSuggestion>(), "استجابة الكتالوج بلا قائمة نماذج");
        }

        var all = new List<FreeModelSuggestion>();
        foreach (var item in data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var id = GetString(item, "id");
            if (!FreeCloudModelGuard.IsFree(id)) continue;

            var pricing = item.TryGetProperty("pricing", out var p) ? p : default;
            if (pricing.ValueKind == JsonValueKind.Object
                && (!IsZeroPrice(pricing, "prompt") || !IsZeroPrice(pricing, "completion")))
            {
                continue;
            }

            if (item.TryGetProperty("expiration_date", out var exp)
                && exp.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(exp.GetString(), out var expires)
                && expires <= fetchedAt)
            {
                continue;
            }

            var context = item.TryGetProperty("context_length", out var ctx) && ctx.TryGetInt64(out var n) ? n : 0;
            var supportsImage = HasImageInput(item);
            all.Add(new FreeModelSuggestion(id!, GetString(item, "name") ?? id!, context, supportsImage, fetchedAt));
        }

        // ترتيب بحسب بيانات الكتالوج فقط: نماذج غير تجريبية أولاً، ثم سياق أكبر، ثم الاسم.
        static IReadOnlyList<FreeModelSuggestion> Rank(IEnumerable<FreeModelSuggestion> items) => items
            .OrderBy(s => IsPreview(s.ModelId) ? 1 : 0)
            .ThenByDescending(s => s.ContextLength)
            .ThenBy(s => s.ModelId, StringComparer.OrdinalIgnoreCase)
            .Take(MaxCandidates)
            .ToArray();

        var chat = Rank(all);
        var vision = Rank(all.Where(s => s.SupportsImage));
        return new FreeModelCatalogSnapshot(fetchedAt, source, chat, vision, null);
    }

    private static bool IsPreview(string? id)
        => !string.IsNullOrWhiteSpace(id)
           && (id.Contains("preview", StringComparison.OrdinalIgnoreCase)
               || id.Contains("exp", StringComparison.OrdinalIgnoreCase));

    private static bool IsZeroPrice(JsonElement pricing, string key)
    {
        if (!pricing.TryGetProperty(key, out var value)) return true;
        if (value.ValueKind == JsonValueKind.Number) return value.TryGetDecimal(out var d) && d == 0;
        var raw = value.GetString();
        return string.IsNullOrWhiteSpace(raw) || (decimal.TryParse(raw, out var parsed) && parsed == 0);
    }

    private static bool HasImageInput(JsonElement item)
    {
        if (!item.TryGetProperty("architecture", out var arch) || arch.ValueKind != JsonValueKind.Object) return false;
        if (arch.TryGetProperty("input_modalities", out var mods) && mods.ValueKind == JsonValueKind.Array)
        {
            foreach (var m in mods.EnumerateArray())
            {
                if (string.Equals(m.GetString(), "image", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        if (arch.TryGetProperty("modality", out var modality)
            && modality.ValueKind == JsonValueKind.String
            && modality.GetString()?.Contains("image", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }
        return false;
    }

    private static string? GetString(JsonElement obj, string key)
        => obj.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string ResolveCachePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("WIZZ_MODEL_CATALOG_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir)) baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Wizz", "free-model-catalog.json");
    }

    private FreeModelCatalogSnapshot? LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return null;
            var json = File.ReadAllText(_cachePath);
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonSerializer.Deserialize<FreeModelCatalogSnapshot>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "تعذّت قراءة ملف كتالوج النماذج المحلي");
            return null;
        }
    }

    private void SaveCache(FreeModelCatalogSnapshot snapshot)
    {
        try
        {
            var directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_cachePath, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "تعذّ حفظ ملف كتالوج النماذج المحلي");
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
