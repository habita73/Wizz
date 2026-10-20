namespace Wizz.Infrastructure.AI;

using Microsoft.Extensions.Logging;
using OllamaSharp;
using Wizz.Application.Abstractions;

/// <summary>
/// موفر النموذج المحلي عبر Ollama.
/// مصمم للأجهزة الضعيفة: فحص سريع للتوفر، اختيار تلقائي لأخف نموذج مثبّت،
/// ومهلة توليد قصيرة حتى لا تتجمد الواجهة.
/// </summary>
public sealed class LocalLlmProvider : ILlmProvider, IDisposable
{
    /// <summary>مهلة استكشاف Ollama — قصيرة حتى لا تتجمد الواجهة عند غيابه.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);

    /// <summary>مهلة توليد الرد — تفشل بلطف بدل تعليق الطلب.</summary>
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(60);

    /// <summary>ترتيب تفضيلي للنماذج الخفيفة المناسبة للأجهزة المتوسطة والضعيفة.</summary>
    private static readonly string[] PreferredModels =
    {
        "llama3.2:1b",
        "qwen2.5:0.5b",
        "qwen2:0.5b",
        "gemma2:2b",
        "phi3:mini",
        "qwen2.5:1.5b",
        "llama3.2:3b",
        "llama3.2",
        "llama3"
    };

    private readonly OllamaApiClient _client;
    private readonly ILogger<LocalLlmProvider>? _logger;
    private string? _activeModel;
    private bool _modelResolved;

    public string ProviderName => _activeModel is null
        ? "Ollama (محلي)"
        : $"Ollama (محلي) — {_activeModel}";

    public LocalLlmProvider(string baseUrl, string model, ILogger<LocalLlmProvider>? logger = null)
    {
        _activeModel = string.IsNullOrWhiteSpace(model) ? null : model;
        _logger = logger;
        _client = new OllamaApiClient(new Uri(baseUrl), _activeModel ?? "llama3.2:1b");
    }

    /// <summary>متاح يعني: Ollama يستجيب وهناك نموذج واحد على الأقل مثبّت.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var models = await ListModelsSafeAsync(ct);
        if (models.Count == 0) return false;

        if (!_modelResolved || _activeModel is null || !models.Any(m => SameModel(m.Name, _activeModel)))
        {
            ResolveLightestModel(models);
        }

        return _activeModel is not null;
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_activeModel))
            throw new InvalidOperationException("لا يوجد نموذج محلي محدد. شغّل Ollama ثم ثبّت نموذجاً خفيفاً مثل llama3.2:1b.");

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(CompletionTimeout);

            var request = new OllamaSharp.Models.GenerateRequest
            {
                Model = _activeModel,
                Prompt = prompt,
                Stream = false
            };

            var fullResponse = string.Empty;
            await foreach (var chunk in _client.GenerateAsync(request, timeout.Token))
            {
                if (chunk?.Response != null)
                    fullResponse += chunk.Response;
            }
            return fullResponse;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger?.LogWarning("انتهت مهلة استجابة Ollama بعد {Seconds} ثانية", CompletionTimeout.TotalSeconds);
            throw new TimeoutException("انتهت مهلة استجابة المساعد المحلي. جرّب نموذجاً أخف مثل llama3.2:1b.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "خطأ في استكمال Ollama: {Message}", ex.Message);
            throw;
        }
    }

    public Task<string> CompleteWithContextAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var fullPrompt = $"{systemPrompt}\n\nالمستخدم: {userPrompt}\nالمساعد:";
        return CompleteAsync(fullPrompt, ct);
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        var models = await ListModelsSafeAsync(ct);
        if (models.Count == 0) return Array.Empty<string>();

        // الأخف أولاً حتى تكون التجربة سريعة على الأجهزة المتوسطة
        return models
            .OrderBy(PreferenceRank)
            .ThenBy(m => m.Size)
            .Select(m => m.Name)
            .ToList();
    }

    public async Task SetModelAsync(string modelName, CancellationToken ct = default)
    {
        var models = await ListModelsSafeAsync(ct);
        var match = models.FirstOrDefault(m => SameModel(m.Name, modelName));
        if (match is null)
            throw new ArgumentException($"النموذج '{modelName}' غير متاح محلياً. ثبّته أولاً بـ: ollama pull {modelName}", nameof(modelName));

        _activeModel = match.Name;
        _client.SelectedModel = match.Name;
        _modelResolved = true;
    }

    private async Task<List<ModelInfo>> ListModelsSafeAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);

            var models = await _client.ListLocalModelsAsync(timeout.Token);
            return models
                .Where(m => !string.IsNullOrWhiteSpace(m.Name))
                .Select(m => new ModelInfo(m.Name!, m.Size))
                .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Ollama غير متاح: {Message}", ex.Message);
            return new List<ModelInfo>();
        }
    }

    /// <summary>
    /// يختار النموذج الأنسب تلقائياً: النموذج المُعدّ إن كان مثبّتاً،
    /// وإلا أول نموذج من القائمة المفضلة الخفيفة، وإلا أصغر نموذج مثبّت حجماً.
    /// </summary>
    private void ResolveLightestModel(IReadOnlyList<ModelInfo> models)
    {
        if (models.Count == 0) return;

        ModelInfo? chosen = null;

        if (!string.IsNullOrWhiteSpace(_activeModel))
        {
            chosen = models.FirstOrDefault(m => SameModel(m.Name, _activeModel));
        }

        chosen ??= models
            .Where(m => PreferenceRank(m) < PreferredModels.Length)
            .OrderBy(PreferenceRank)
            .ThenBy(m => m.Size)
            .FirstOrDefault();

        // لا يوجد نموذج من القائمة المفضلة → الأصغر حجماً هو الأسرع عملياً
        chosen ??= models.OrderBy(m => m.Size).First();

        if (!string.Equals(chosen.Name, _activeModel, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("تم اختيار النموذج المحلي '{Model}' تلقائياً (الحجم: {Size} بايت).", chosen.Name, chosen.Size);
        }

        _activeModel = chosen.Name;
        _client.SelectedModel = chosen.Name;
        _modelResolved = true;
    }

    private static int PreferenceRank(ModelInfo model)
    {
        var normalized = model.Name.Trim().ToLowerInvariant();
        for (var i = 0; i < PreferredModels.Length; i++)
        {
            if (normalized.StartsWith(PreferredModels[i], StringComparison.Ordinal))
                return i;
        }
        return PreferredModels.Length;
    }

    private static bool SameModel(string a, string b)
        => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)
           || string.Equals(StripTag(a), StripTag(b), StringComparison.OrdinalIgnoreCase);

    private static string StripTag(string model)
    {
        var idx = model.IndexOf(':');
        return idx > 0 ? model[..idx] : model;
    }

    public void Dispose() => _client?.Dispose();

    private sealed record ModelInfo(string Name, long Size);
}
