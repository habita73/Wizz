namespace Wizz.Infrastructure.AI;

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using Wizz.Application.Abstractions;
using Wizz.Application.AI.Perception;
using Wizz.Application.AI.Privacy;

/// <summary>
/// مزود سحابي مرن يعمل مع أي نقطة نهاية متوافقة مع واجهة OpenAI API
/// (OpenRouter، GroqCloud، Gemini، إلخ).
/// مصمم للخدمات المجانية مع دعم التبديل التلقائي بين المزودين.
/// </summary>
public sealed class CloudLlmProvider : ILlmProvider, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private string _model;
    private readonly string _providerName;
    private readonly ILogger<CloudLlmProvider>? _logger;

    /// <summary>مهلة الاستجابة — تفشل بلطف بدل تعليق الطلب.</summary>
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(60);

    public string ProviderName => _providerName;

    public CloudLlmProvider(
        string baseUrl,
        string apiKey,
        string model,
        string providerName,
        ILogger<CloudLlmProvider>? logger = null,
        HttpClient? httpClient = null)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _model = model;
        _providerName = providerName;
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.Timeout = CompletionTimeout;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            // probe سريع: محاولة جلب قائمة النماذج أو طلب بسيط
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            // نقرأ الترويسات فقط (ResponseHeadersRead) حتى لا ننتظر تنزيل جسم الاستجابة (قائمة كبيرة من النماذج)
            using var response = await _httpClient.GetAsync(
                $"{_baseUrl}/models",
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);
            _logger?.LogDebug("Probe {Url} -> {StatusCode}", $"{_baseUrl}/models", response.StatusCode);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "المزود السحابي غير متاح: {Message} (BaseUrl={BaseUrl})", ex.Message, _baseUrl);
            return false;
        }
    }

    /// <summary>
    /// يفحص نقطتين منفصلتين: نقطة الكتالوج العامة، ونقطة فحص المفتاح لدى المزوّد.
    /// لا تُرسل أي بيانات متجر؛ يُرسل المفتاح فقط إلى مزوّده عند ضغط زر الاختبار يدويًا.
    /// </summary>
    public async Task<(bool CatalogReachable, bool? KeyValid, string Details)> TestConnectionDetailedAsync(CancellationToken ct = default)
    {
        var key = await ProbeApiKeyAsync(ct);
        var catalog = await ProbeCatalogAsync(ct);
        return (catalog.Reachable, key.Valid, $"{catalog.Message} {key.Message}".Trim());
    }

    private async Task<(bool Reachable, string Message)> ProbeCatalogAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await _httpClient.GetAsync(
                $"{_baseUrl}/models",
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);
            return response.IsSuccessStatusCode
                ? (true, "الوصول إلى كتالوج النماذج العامة متاح (وهذا وحده لا يثبت صلاحية المفتاح).")
                : (false, $"كتالوج النماذج أعاد الحالة {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "فشل الوصول إلى كتالوج النماذج: {BaseUrl}", _baseUrl);
            return (false, $"كتالوج النماذج غير متاح: {ex.Message}");
        }
    }

    private async Task<(bool? Valid, string Message)> ProbeApiKeyAsync(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return (false, "لم يُدخل مفتاح API، فلا يمكن تأكيد صلاحيته.");

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await _httpClient.GetAsync(
                $"{_baseUrl}/auth/key",
                HttpCompletionOption.ResponseHeadersRead,
                cts.Token);

            if (response.IsSuccessStatusCode)
                return (true, "المفتاح صالح لدى المزوّد.");

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                var error = await ReadProviderErrorAsync(response, ct);
                return (false, $"المزوّد رفض المفتاح ({(int)response.StatusCode}){(error is null ? "." : $" — {error}")} راجع نسخة المفتاح أو أنشئ مفتاحًا جديدًا لدى المزوّد.");
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
                return (null, "المزوّد لا يوفّر فحصًا منفصلًا للمفتاح، فبقيت الصلاحية غير محسومة.");

            return (null, $"تعذّر فحص المفتاح (الحالة {(int)response.StatusCode})، فبقيت الصلاحية غير محسومة.");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "فشل فحص مفتاح المزوّد: {BaseUrl}", _baseUrl);
            return (null, $"تعذّر فحص المفتاح: {ex.Message}");
        }
    }

    private static async Task<string?> ReadProviderErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (string.IsNullOrWhiteSpace(body)) return null;
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                var text = message.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Length > 160 ? text[..160] + "…" : text;
            }
        }
        catch
        {
            // جسم الخطأ لا يكون JSON دائمًا؛ الاكتفاء بالحالة أوضح من إظهار نص خام.
        }
        return null;
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        var messages = new[]
        {
            new ChatMessage("system", "أنت مساعد نقاط بيع قصير ومباشر. أجب بالعربية ولا تخترع أرقامًا."),
            new ChatMessage("user", prompt)
        };
        var request = new ChatCompletionRequest(_model, messages, 0.3, 500);

        return await SendRequestAsync(request, ct);
    }

    public async Task<string> CompleteWithContextAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var messages = new[]
        {
            new ChatMessage("system", systemPrompt),
            new ChatMessage("user", userPrompt)
        };
        var request = new ChatCompletionRequest(_model, messages, 0.3, 800);

        return await SendRequestAsync(request, ct);
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        // المزود السحابي يقدّم نماذج محددة — نعيد النموذج المُحدّد فقط
        return Task.FromResult<IReadOnlyList<string>>(new[] { _model });
    }

    public Task SetModelAsync(string modelName, CancellationToken ct = default)
    {
        // في المزود السحابي، تغيير النموذج يتطلب إعادة إنشاء العميل مع المفتاح الجديد
        // نضع النموذج الجديد للاستخدام المستقبلي
        _model = modelName; // ملاحظة: هذا تبسيط، يمكن تحسينه لاحقًا
        return Task.CompletedTask;
    }

    private async Task<string> SendRequestAsync(ChatCompletionRequest request, CancellationToken ct)
    {
        // قاعدة «مجاني فقط / صفر تكلفة» مطبَّقة هنا أيضًا قبل فتح أي اتصال، كما في استخراج الصورة.
        if (!FreeCloudModelGuard.IsFree(request.Model))
        {
            _logger?.LogWarning("تم منع طلب سحابي بنموذج غير مجاني: {Model}", request.Model);
            throw new InvalidOperationException(
                "نموذج المحادثة السحابي غير مجاني. اضبط اسمًا ينتهي بـ :free من الإعدادات ← المساعد الذكي قبل استخدام السحابة.");
        }

        // دفاع إضافي: لا يُرسل أي طلب قبل التأكد من خلوّ كل رسائله من البيانات المحظورة.
        // الموجّه ينقّح السؤال أولاً، وهنا فحص نهائي مستقل قبل فتح الاتصال.
        if (!CloudPromptGuard.IsSafeToSend(request.Messages.Select(m => m.Content).ToArray()))
        {
            _logger?.LogWarning("تم منع طلب سحابي يحتوي بيانات غير مسموح بتشاركها.");
            throw new InvalidOperationException(CloudPromptGuard.BlockedMessage);
        }

        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(CompletionTimeout);

        var response = await _httpClient.PostAsync($"{_baseUrl}/chat/completions", content, cts.Token);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger?.LogWarning("خطأ من المزود السحابي {StatusCode}: {Body}", response.StatusCode, errorBody);
            throw new HttpRequestException($"المزود السحابي أعاد خطأ: {response.StatusCode} - {errorBody}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        var responseDoc = JsonDocument.Parse(responseJson);

        var choices = responseDoc.RootElement.GetProperty("choices");
        if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            throw new InvalidOperationException("لم يجد المزود السحابي أي استجابة.");

        var message = choices[0].GetProperty("message");
        var contentText = message.GetProperty("content").GetString() ?? string.Empty;

        return contentText;
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ChatCompletionRequest(
        string Model,
        ChatMessage[] Messages,
        double Temperature,
        int? MaxTokens = null);
}
