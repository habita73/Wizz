namespace Wizz.Infrastructure.AI.Perception;

using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Wizz.Application.Abstractions;
using Wizz.Application.AI.Perception;

/// <summary>
/// استخراج النص من صورة ورقة عبر نموذج عند طلب المستخدم، بمسارين منفصلين:
/// محلي عبر Ollama (بلا خروج الصورة من الجهاز) وسحابي مجاني فقط بعد تفعيل مفتاحي السحابة والتشارك.
/// لا يكتب في قاعدة البيانات ولا يستخدم مسار القراءة الروتيني.
/// </summary>
public sealed class ModelDocumentExtractor : IModelDocumentExtractor
{
    private const int MaxImageBytes = 20 * 1024 * 1024;
    private const int CloudMaxTokens = 8000;
    private const int CloudRetryMaxTokens = 16000;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(180);
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    private const string ExtractionInstruction =
        "أنت ناسخ نصوص من صور فواتير عربية مطبوعة. " +
        "انسخ الحروف العربية كما تظهر في الصورة حرفًا بحرف؛ لا تبدّلها بكلمات مشابهة، ولا تُصحّح الإملاء، " +
        "ولا تترجم، ولا تكتب بالحروف اللاتينية. " +
        "احتفظ بترتيب الأسطر من اليمين إلى اليسار، وانسخ الأرقام والمبالغ كما هي. " +
        "تجاهل أي كتابة يدوية. " +
        "أخرج النص الخام فقط، سطرًا بسطر، بلا مقدمة ولا خاتمة ولا تنسيق ولا تعليق.";

    private readonly IAiSettingsProvider _settings;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ModelDocumentExtractor>? _logger;

    public ModelDocumentExtractor(
        IAiSettingsProvider settings,
        IConfiguration configuration,
        HttpClient httpClient,
        ILogger<ModelDocumentExtractor>? logger = null)
    {
        _settings = settings;
        _configuration = configuration;
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DocumentExtractionResult> ExtractAsync(
        byte[] imageBytes,
        DocumentModelRoute route,
        CancellationToken ct = default)
    {
        ValidateImage(imageBytes);
        var base64 = Convert.ToBase64String(imageBytes);
        var mime = DetectMimeType(imageBytes);

        return route switch
        {
            DocumentModelRoute.Local => await ExtractLocalAsync(base64, mime, ct),
            DocumentModelRoute.Cloud => await ExtractCloudAsync(base64, mime, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, "مسار النموذج غير معروف.")
        };
    }

    private async Task<DocumentExtractionResult> ExtractLocalAsync(
        string base64,
        string mime,
        CancellationToken ct)
    {
        var baseUrl = _configuration["Ollama:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("عنوان Ollama غير مضبوط في الإعدادات (Ollama:BaseUrl).");

        var model = _configuration["AI:VisionExtraction:LocalModel"];
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("لم يُضبط نموذج رؤية محلي. أضف AI:VisionExtraction:LocalModel في الإعدادات.");

        await EnsureLocalModelInstalledAsync(baseUrl, model, ct);

        var payload = new
        {
            model,
            messages = new[]
            {
                new { role = "user", content = ExtractionInstruction, images = new[] { base64 } }
            },
            stream = false
        };

        var read = await PostAndReadAsync(
            $"{baseUrl.TrimEnd('/')}/api/chat",
            JsonSerializer.Serialize(payload),
            apiKey: null,
            ct,
            parse: ReadOllamaContent);

        var text = RequireText(read);
        _logger?.LogInformation("استخراج محلي بالنموذج {Model} — {Chars} حرفًا", model, text.Length);
        return new DocumentExtractionResult(text, DocumentModelRoute.Local, model, Array.Empty<string>());
    }

    private async Task<DocumentExtractionResult> ExtractCloudAsync(
        string base64,
        string mime,
        CancellationToken ct)
    {
        // بوابتان مستقلتان + مفتاح + نموذج مجاني: لا يُبنى أي طلب قبل تحقيقها جميعًا.
        if (!_settings.IsCloudEnabled)
            throw new InvalidOperationException("النموذج السحابي غير مفعّل. فعّله صراحةً من إعدادات الذكاء الاصطناعي أولًا.");
        if (!_settings.CloudSharingEnabled)
            throw new InvalidOperationException("إرسال البيانات للسحابة غير مفعّل. فعّل مفتاح التشارك صراحةً أولًا.");

        var provider = _configuration["AI:PreferredCloudProvider"];
        if (string.IsNullOrWhiteSpace(provider)) provider = "openrouter";

        var section = _configuration.GetSection($"AI:CloudProviders:{provider}");
        var baseUrl = section["BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "https://openrouter.ai/api/v1";

        var apiKey = _settings.GetCloudApiKey();
        if (string.IsNullOrWhiteSpace(apiKey)) apiKey = section["ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("لا يوجد مفتاح API سحابي. أضفه من إعدادات الذكاء الاصطناعي أولًا.");

        // الإعداد المحفوظ من واجهة الإعدادات هو المرجع الأول، وملف التطبيق fallback للتوافق.
        var model = _settings.CloudVisionModel;
        if (string.IsNullOrWhiteSpace(model)) model = section["VisionModel"];
        if (!FreeCloudModelGuard.IsFree(model))
            throw new InvalidOperationException(FreeCloudModelGuard.BlockedMessage);

        var url = $"{baseUrl.TrimEnd('/')}/chat/completions";
        var read = await PostAndReadAsync(
            url,
            BuildCloudRequestJson(model!, mime, base64, CloudMaxTokens, lowReasoning: true),
            apiKey,
            ct,
            parse: ReadChatContent);

        var text = read.Text.Trim();
        if (text.Length == 0 && read.BudgetExhausted)
        {
            // توثيق OpenRouter: رموز التفكير تُحصى ضمن max_tokens نفسه على Chat Completions،
            // فإذا استهلكها النموذج عاد content فارغًا مع finish_reason = length.
            _logger?.LogWarning(
                "التفكير استهلك سقف الإخراج في استخراج الصورة بالنموذج {Model}؛ محاولة واحدة بسقف أعلى وتفكير أقل",
                model);
            var retry = await PostAndReadAsync(
                url,
                BuildCloudRequestJson(model!, mime, base64, CloudRetryMaxTokens, lowReasoning: true),
                apiKey,
                ct,
                parse: ReadChatContent);
            text = retry.Text.Trim();
            if (text.Length == 0)
                throw new InvalidOperationException(ReasoningBudgetMessage(model!, retry));
        }
        else if (text.Length == 0)
        {
            throw new InvalidOperationException("أعاد النموذج نصًا فارغًا.");
        }

        _logger?.LogInformation("استخراج سحابي بالنموذج {Model} — {Chars} حرفًا", model, text.Length);
        return new DocumentExtractionResult(
            text,
            DocumentModelRoute.Cloud,
            model!,
            new[] { "أُرسلت صورة الورقة إلى مزوّد سحابي بعد موافقتك. راجع النص قبل استخدامه." });
    }

    private async Task EnsureLocalModelInstalledAsync(string baseUrl, string model, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ProbeTimeout);
            using var response = await _httpClient.GetAsync(
                $"{baseUrl.TrimEnd('/')}/api/tags",
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (!response.IsSuccessStatusCode) return; // يعتمد على خطأ Ollama الواضح في طلبChat

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            if (!doc.RootElement.TryGetProperty("models", out var models)) return;
            var installed = models.EnumerateArray()
                .Select(m => m.TryGetProperty("name", out var n) ? n.GetString() : null)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
            if (installed.Count > 0 && !installed.Any(n => SameModelName(n!, model)))
            {
                throw new InvalidOperationException(
                    $"النموذج '{model}' غير مثبّت في Ollama. ثبّته بأمر: ollama pull {model}");
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // فحص التوفر اختياري؛ الطلب نفسه سيعطي خطأً أوضح.
        }
        catch (JsonException)
        {
            // استجابة غير متوقعة من /api/tags؛ الطلب نفسه سيعطي خطأً أوضح.
        }
    }

    private async Task<CompletionRead> PostAndReadAsync(
        string url,
        string json,
        string? apiKey,
        CancellationToken ct,
        Func<JsonDocument, CompletionRead> parse)
    {
        const int transportAttempts = 2;
        var body = string.Empty;

        for (var attempt = 0; attempt < transportAttempts; attempt++)
        {
            // HttpRequestMessage لا يُعاد إرساله؛ يُبنى طلب مستقل لكل محاولة.
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(RequestTimeout);

            try
            {
                using var response = await _httpClient.SendAsync(request, timeout.Token);
                body = await response.Content.ReadAsStringAsync(timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    _logger?.LogWarning("فشل استدعاء النموذج {Url}: {Status}", url, response.StatusCode);
                    throw new HttpRequestException($"أعاد النموذج خطأ: {(int)response.StatusCode} — {Truncate(body)}");
                }
                break;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // العميل نفسه بلا مهلة ثابتة؛ مهلة الطلب هي المُتحكِّم، ونُظهر سببًا مفهومًا بدل رسالة إلغاء عامة.
                throw new TimeoutException(
                    "استهلك استخراج الصورة الوقت المسموح له دون أن يعيد النموذج إجابة. " +
                    "جرّب صورة أصغر أو نموذج رؤية مجانيًا أسرع. لم تُكتب أي بيانات.");
            }
            catch (HttpRequestException ex) when (HasSocketFailure(ex) && attempt < transportAttempts - 1)
            {
                // حلّ اسم النطاق أو الاتصال قد يفشل لحظيًا؛ محاولة واحدة بعد فاصل قصير.
                _logger?.LogWarning(ex, "عارض شبكة مؤقت أثناء استدعاء {Url}؛ إعادة محاولة واحدة", url);
                await Task.Delay(TimeSpan.FromMilliseconds(900), ct);
            }
            catch (HttpRequestException ex) when (HasSocketFailure(ex))
            {
                throw new InvalidOperationException(
                    "تعذّر الاتصال بمزوّد النموذج بعد محاولتين بسبب عارض في الشبكة أو في حلّ اسم النطاق. " +
                    "تحقّق من الإنترنت ثم أعد المحاولة. لم تُكتب أي بيانات.", ex);
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return parse(doc);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("تعذّر قراءة استجابة النموذج.");
        }
    }

    /// <summary>هل الخطأ ناتج عن فشل شبكة أو حلّ اسم نطاق يمكن إعادة محاولته؟</summary>
    private static bool HasSocketFailure(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is SocketException) return true;
        }
        return false;
    }

    /// <summary>رسالة واضحة حين يستهلك التفكير سقف الإخراج في المحاولتين.</summary>
    private static string ReasoningBudgetMessage(string model, CompletionRead read)
    {
        var usage = read.ReasoningTokens is int r && read.CompletionTokens is int c
            ? $" (استهلك التفكير {r} من {c} رمزًا)"
            : string.Empty;
        return $"استهلك التفكير سقف الإخراج في النموذج {model}{usage} مرتين متتاليتين " +
               $"({CloudMaxTokens} ثم {CloudRetryMaxTokens} رمزًا)، فلم يبقَ نصٌّ قابل للقراءة. " +
               "جرّب نموذج رؤية مجانيًا أقل تفكيرًا من إعدادات الذكاء الاصطناعي، أو قسّم الورقة إلى صور أصغر. لم تُكتب أي بيانات.";
    }

    /// <summary>يبني طلب السحابة؛ تقليل التفكير من المحاولة الأولى Best-effort إن قبلَه المزوّد،
    /// كي لا يستهلك التفكير سقف الإخراج قبل النص ويضطرّ الطلب إلى محاولة ثانية بطيئة.</summary>
    private static string BuildCloudRequestJson(string model, string mime, string base64, int maxTokens, bool lowReasoning)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = ExtractionInstruction },
                        new { type = "image_url", image_url = new { url = $"data:{mime};base64,{base64}" } }
                    }
                }
            },
            ["temperature"] = 0.0,
            ["max_tokens"] = maxTokens
        };
        if (lowReasoning)
            payload["reasoning"] = new { effort = "low" };
        return JsonSerializer.Serialize(payload);
    }

    private static string RequireText(CompletionRead read)
    {
        if (string.IsNullOrWhiteSpace(read.Text))
            throw new InvalidOperationException("أعاد النموذج نصًا فارغًا.");
        return read.Text.Trim();
    }

    private static CompletionRead ReadOllamaContent(JsonDocument doc)
        => doc.RootElement.TryGetProperty("message", out var message)
           && message.TryGetProperty("content", out var content)
            ? new CompletionRead(content.GetString() ?? string.Empty, null, null, null)
            : new CompletionRead(string.Empty, null, null, null);

    private static CompletionRead ReadChatContent(JsonDocument doc)
    {
        if (!doc.RootElement.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
            return new CompletionRead(string.Empty, null, null, null);

        var choice = choices[0];
        var message = choice.GetProperty("message");
        var text = message.TryGetProperty("content", out var content) ? content.GetString() ?? string.Empty : string.Empty;

        string? finishReason = null;
        if (choice.TryGetProperty("finish_reason", out var finish) && finish.ValueKind == JsonValueKind.String)
            finishReason = finish.GetString();

        int? completionTokens = null;
        int? reasoningTokens = null;
        if (doc.RootElement.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("completion_tokens", out var total) && total.TryGetInt32(out var t))
                completionTokens = t;
            if (usage.TryGetProperty("completion_tokens_details", out var details)
                && details.ValueKind == JsonValueKind.Object
                && details.TryGetProperty("reasoning_tokens", out var reasoning) && reasoning.TryGetInt32(out var r))
                reasoningTokens = r;
        }

        return new CompletionRead(text, finishReason, completionTokens, reasoningTokens);
    }

    /// <summary>النص المقروء مع إشارات التشخيص التي يوفّرها المزوّد عند امتلاء سقف الإخراج.</summary>
    private sealed record CompletionRead(
        string Text,
        string? FinishReason,
        int? CompletionTokens,
        int? ReasoningTokens)
    {
        public bool BudgetExhausted => string.Equals(FinishReason, "length", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateImage(byte[] imageBytes)
    {
        if (imageBytes is null || imageBytes.Length == 0)
            throw new ArgumentException("لا توجد صورة لاستخراج النص منها.", nameof(imageBytes));
        if (imageBytes.Length > MaxImageBytes)
            throw new ArgumentException("الصورة أكبر من الحد المسموح (٢٠ ميغابايت).", nameof(imageBytes));
    }

    private static string DetectMimeType(byte[] bytes)
    {
        if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        if (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        return "image/jpeg";
    }

    private static bool SameModelName(string a, string b)
        => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase)
           || string.Equals(StripTag(a), StripTag(b), StringComparison.OrdinalIgnoreCase);

    private static string StripTag(string model)
    {
        var idx = model.IndexOf(':');
        return idx > 0 ? model[..idx] : model;
    }

    private static string Truncate(string value) => value.Length <= 400 ? value : value[..400];
}
