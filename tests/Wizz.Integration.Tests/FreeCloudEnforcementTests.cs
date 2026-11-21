using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Wizz.Application.Abstractions;
using Wizz.Infrastructure.AI;

namespace Wizz.Integration.Tests;

/// <summary>يمنع التوازي بين الاختبارات التي تبدّل مسار ملف إعدادات الذكاء الاصطناعي.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AiSettingsEnvironmentCollection
{
    public const string Name = "AiSettingsEnvironment";
}

/// <summary>اختبارات ببيانات اصطناعية تثبت أن قاعدة «مجاني فقط» تطبَّق قبل أي طلب سحابي.</summary>
[Collection(AiSettingsEnvironmentCollection.Name)]
public sealed class FreeCloudEnforcementTests
{
    [Fact]
    public async Task Chat_WithPaidModel_DoesNotOpenAnyConnection()
    {
        var handler = new StubHandler();
        var provider = new CloudLlmProvider(
            "https://openrouter.ai/api/v1",
            "synthetic-key",
            "qwen/qwen3-32b",
            "OpenRouter",
            httpClient: new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.CompleteWithContextAsync("نظام", "سؤال"));

        Assert.Contains("ينتهي بـ :free", ex.Message);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Chat_WithFreeModel_SendsRequestAndReturnsText()
    {
        var handler = new StubHandler();
        var provider = new CloudLlmProvider(
            "https://openrouter.ai/api/v1",
            "synthetic-key",
            "liquid/lfm-3-1.7b:free",
            "OpenRouter",
            httpClient: new HttpClient(handler));

        var answer = await provider.CompleteWithContextAsync("نظام", "سؤال");

        Assert.Equal("إجابة تجريبية", answer);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("liquid/lfm-3-1.7b:free", handler.LastBody!, StringComparison.Ordinal);
    }

    [Fact]
    public void AiSettings_PersistedChatModelOverridesConfigModel()
    {
        var path = Path.Combine(Path.GetTempPath(), "wizz-ai-" + Guid.NewGuid().ToString("N") + ".json");
        var previous = Environment.GetEnvironmentVariable("WIZZ_AI_SETTINGS_PATH");
        try
        {
            Environment.SetEnvironmentVariable("WIZZ_AI_SETTINGS_PATH", path);
            var values = new Dictionary<string, string?>
            {
                ["AI:PreferredCloudProvider"] = "openrouter",
                ["AI:CloudProviders:openrouter:BaseUrl"] = "https://openrouter.ai/api/v1",
                ["AI:CloudProviders:openrouter:ApiKey"] = "synthetic-key",
                ["AI:CloudProviders:openrouter:Model"] = "paid/qwen"
            };
            var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            var settings = new AiSettingsProvider(config);
            settings.IsCloudEnabled = true;
            settings.SetCloudApiKey("synthetic-key");
            settings.CloudChatModel = "liquid/lfm-3-1.7b:free";

            // إعادة التحميل من الملف تثبت أن الاختيار بقي بعد إغلاق التطبيق.
            var reloaded = new AiSettingsProvider(config);
            var cloud = Assert.Single(reloaded.GetCloudProvidersConfig()!);

            Assert.Equal("liquid/lfm-3-1.7b:free", cloud.Model);
            Assert.True(cloud.Enabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIZZ_AI_SETTINGS_PATH", previous);
            try { File.Delete(path); } catch { /* تنظيف أفضل جهد */ }
        }
    }

    [Fact]
    public async Task CloudTest_RejectedKey_ReportsInvalidKeyWithoutHidingCatalogResult()
    {
        var handler = new ProbeHandler(HttpStatusCode.Unauthorized, HttpStatusCode.OK);
        var provider = new CloudLlmProvider(
            "https://openrouter.ai/api/v1",
            "synthetic-key",
            "liquid/lfm-3-1.7b:free",
            "OpenRouter",
            httpClient: new HttpClient(handler));

        var (catalogReachable, keyValid, details) = await provider.TestConnectionDetailedAsync();

        Assert.True(catalogReachable);
        Assert.False(keyValid);
        Assert.Contains("رفض المفتاح", details);
        Assert.Contains("User not found.", details);
        Assert.Contains(handler.Paths, p => p.EndsWith("/auth/key", StringComparison.Ordinal));
        Assert.Contains(handler.Paths, p => p.EndsWith("/models", StringComparison.Ordinal));
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task CloudTest_ValidKey_ReportsItWithoutClaimingAnswerQuality()
    {
        var handler = new ProbeHandler(HttpStatusCode.OK, HttpStatusCode.OK);
        var provider = new CloudLlmProvider(
            "https://openrouter.ai/api/v1",
            "synthetic-key",
            "liquid/lfm-3-1.7b:free",
            "OpenRouter",
            httpClient: new HttpClient(handler));

        var (catalogReachable, keyValid, details) = await provider.TestConnectionDetailedAsync();

        Assert.True(catalogReachable);
        Assert.True(keyValid);
        Assert.Contains("المفتاح صالح", details);
        Assert.Contains("لا يثبت صلاحية المفتاح", details);
    }

    [Fact]
    public async Task CloudTest_WithoutKey_DoesNotProbeKeyEndpoint()
    {
        var handler = new ProbeHandler(HttpStatusCode.OK, HttpStatusCode.OK);
        var provider = new CloudLlmProvider(
            "https://openrouter.ai/api/v1",
            "",
            "liquid/lfm-3-1.7b:free",
            "OpenRouter",
            httpClient: new HttpClient(handler));

        var (catalogReachable, keyValid, details) = await provider.TestConnectionDetailedAsync();

        Assert.True(catalogReachable);
        Assert.False(keyValid);
        Assert.DoesNotContain(handler.Paths, p => p.EndsWith("/auth/key", StringComparison.Ordinal));
        Assert.Contains("لم يُدخل مفتاح", details);
    }

    [Fact]
    public async Task CloudTest_ProviderWithoutKeyCheck_LeavesValidityUnknown()
    {
        ILlmProvider provider = new MinimalProvider();

        var (catalogReachable, keyValid, details) = await provider.TestConnectionDetailedAsync();

        Assert.True(catalogReachable);
        Assert.Null(keyValid);
        Assert.Contains("لم يتحقق من صلاحية المفتاح", details);
    }

    private sealed class ProbeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _keyStatus;
        private readonly HttpStatusCode _catalogStatus;

        public ProbeHandler(HttpStatusCode keyStatus, HttpStatusCode catalogStatus)
        {
            _keyStatus = keyStatus;
            _catalogStatus = catalogStatus;
        }

        public List<string> Paths { get; } = new();

        public int CallCount => Paths.Count;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            if (path.EndsWith("/auth/key", StringComparison.Ordinal))
            {
                const string body = "{\"error\":{\"message\":\"User not found.\",\"code\":401}}";
                return Task.FromResult(new HttpResponseMessage(_keyStatus)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            return Task.FromResult(new HttpResponseMessage(_catalogStatus)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class MinimalProvider : ILlmProvider
    {
        public string ProviderName => "مزوّد تجريبي";

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<string> CompleteAsync(string prompt, CancellationToken ct = default) => Task.FromResult("");

        public Task<string> CompleteWithContextAsync(string systemPrompt, string userPrompt, CancellationToken ct = default) => Task.FromResult("");

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task SetModelAsync(string modelName, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            const string json = "{\"choices\":[{\"message\":{\"content\":\"إجابة تجريبية\"}}]}";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        }
    }
}
