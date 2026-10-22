namespace Wizz.Infrastructure.AI;

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using Wizz.Application.Abstractions;

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
        _model = modelName; // ملاحظة: هذا 단순، يمكن تحسينه لاحقًا
        return Task.CompletedTask;
    }

    private async Task<string> SendRequestAsync(ChatCompletionRequest request, CancellationToken ct)
    {
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
