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
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>مهلة توليد الرد — تفشل بلطف بدل تعليق الطلب.</summary>
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(60);

    /// <summary>ترتيب تفضيلي للنماذج الخفيفة التي تدعم العربية بشكل معقول.</summary>
    private static readonly string[] PreferredModels =
    {
        "qwen2.5:1.5b",
        "qwen2.5:3b",
        "qwen2.5:0.5b",
        "gemma2:2b",
        "llama3.2:3b",
        "phi3:mini",
        "llama3.2:1b",
        "llama3.2",
        "llama3"
    };

    private readonly OllamaApiClient _client;
    private readonly IAiSettingsProvider? _settings;
    private readonly ILogger<LocalLlmProvider>? _logger;
    private string? _activeModel;
    private bool _modelResolved;
    private string? _resolvedLevel;

    public string ProviderName => _activeModel is null
        ? "Ollama (محلي)"
        : $"Ollama (محلي) — {_activeModel}";

    public LocalLlmProvider(string baseUrl, string model, IAiSettingsProvider? settings = null, ILogger<LocalLlmProvider>? logger = null)
    {
        _activeModel = string.IsNullOrWhiteSpace(model) ? null : model;
        _settings = settings;
        _logger = logger;
        _client = new OllamaApiClient(new Uri(baseUrl), _activeModel ?? "qwen2.5:1.5b");
    }

    /// <summary>متاح يعني: Ollama يستجيب وهناك نموذج واحد على الأقل مثبّت.</summary>
    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        var models = await ListModelsSafeAsync(ct);
        if (models.Count == 0) return false;

        var currentLevel = _settings?.LocalModelLevel ?? "light";
        if (!_modelResolved || _resolvedLevel != currentLevel || _activeModel is null || !models.Any(m => SameModel(m.Name, _activeModel)))
        {
            ResolveLightestModel(models, currentLevel);
        }

        return _activeModel is not null;
    }

    /// <summary>
    /// اختبار الاتصال بـ Ollama وإرجاع رسالة تشخيصية واضحة للمستخدم.
    /// </summary>
    public async Task<(bool Success, string Message)> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);

            var models = await _client.ListLocalModelsAsync(timeout.Token);
            var modelNames = models.Where(m => !string.IsNullOrWhiteSpace(m.Name)).Select(m => m.Name).ToList();

            if (modelNames.Count == 0)
            {
                return (false, "Ollama متصل لكن لا توجد نماذج مثبّتة. ثبت نموذجاً بأمر: ollama pull qwen2.5:1.5b");
            }

            return (true, $"Ollama متصل بنجاح. النماذج المثبّتة: {string.Join(", ", modelNames.Take(5))}");
        }
        catch (Exception ex)
        {
            var msg = ex.Message.ToLowerInvariant();
            if (msg.Contains("connection refused") || msg.Contains("connection refused"))
            {
                return (false, "لا يمكن الاتصال بـ Ollama. تأكد أن Ollama يعمل: شغّل أمر 'ollama serve' أو ثبّت Ollama من ollama.com");
            }
            if (msg.Contains("timeout") || msg.Contains("timed out"))
            {
                return (false, "انتهت مهلة الاتصال بـ Ollama. قد يكون مشغولاً أو غير مستجيب.");
            }
            return (false, $"خطأ في الاتصال بـ Ollama: {ex.Message}");
        }
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_activeModel))
            throw new InvalidOperationException("لا يوجد نموذج محلي محدد. شغّل Ollama ثم ثبّت نموذجاً خفيفاً يدعم العربية مثل qwen2.5:1.5b.");

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
            throw new TimeoutException("انتهت مهلة استجابة المساعد المحلي. جرّب نموذجاً أخف مثل qwen2.5:1.5b.");
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
    private void ResolveLightestModel(IReadOnlyList<ModelInfo> models, string level)
    {
        if (models.Count == 0) return;

        ModelInfo? chosen = null;

        if (!string.IsNullOrWhiteSpace(_activeModel))
        {
            chosen = models.FirstOrDefault(m => SameModel(m.Name, _activeModel));
        }

        if (chosen is null && !string.IsNullOrWhiteSpace(_settings?.PreferredLocalModel))
        {
            chosen = models.FirstOrDefault(m => SameModel(m.Name, _settings.PreferredLocalModel!));
        }

        if (chosen is null)
        {
            if (level == "medium")
            {
                // متوسط: أكبر نموذج معقول (≤ 12 غيغابايت) لتحسين جودة الإجابة
                chosen = models
                    .Where(m => m.Size <= 12L * 1024 * 1024 * 1024)
                    .OrderByDescending(m => m.Size)
                    .FirstOrDefault();
            }

            chosen ??= models
                .Where(m => PreferenceRank(m) < PreferredModels.Length)
                .OrderBy(PreferenceRank)
                .ThenBy(m => m.Size)
                .FirstOrDefault();

            // لا يوجد نموذج من القائمة المفضلة → الأصغر حجماً هو الأسرع عملياً
            chosen ??= models.OrderBy(m => m.Size).First();
        }

        if (!string.Equals(chosen.Name, _activeModel, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("تم اختيار النموذج المحلي '{Model}' تلقائياً (الحجم: {Size} بايت، المستوى: {Level}).", chosen.Name, chosen.Size, level);
        }

        _activeModel = chosen.Name;
        _client.SelectedModel = chosen.Name;
        _modelResolved = true;
        _resolvedLevel = level;
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
