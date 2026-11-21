using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Configuration;
using Wizz.Application.Abstractions;
using Wizz.Application.AI.Perception;
using Wizz.Infrastructure.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>اختبارات ببيانات اصطناعية لمساري استخراج النص من الصورة عبر نموذج.</summary>
public sealed class ModelDocumentExtractorTests
{
    private static readonly byte[] SyntheticPng =
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

    [Fact]
    public void FreeModelGuard_SeparatesFreeFromPaid()
    {
        Assert.True(FreeCloudModelGuard.IsFree("meta-llama/llama-3.2-11b-vision-instruct:free"));
        Assert.False(FreeCloudModelGuard.IsFree("qwen/qwen3-32b"));
        Assert.False(FreeCloudModelGuard.IsFree(null));
    }

    [Fact]
    public async Task Cloud_WithoutConsent_DoesNotBuildAnyRequest()
    {
        var settings = new StubSettings { IsCloudEnabled = false, CloudSharingEnabled = true };
        var handler = new StubHandler((_, _) => Success("{}"));
        var extractor = CreateExtractor(settings, handler, "model:free");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(SyntheticPng, DocumentModelRoute.Cloud));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Cloud_WithPaidVisionModel_DoesNotBuildAnyRequest()
    {
        var settings = new StubSettings { IsCloudEnabled = true, CloudSharingEnabled = true, ApiKey = "synthetic-key" };
        var handler = new StubHandler((_, _) => Success("{}"));
        var extractor = CreateExtractor(settings, handler, "paid/vision-model");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(SyntheticPng, DocumentModelRoute.Cloud));

        Assert.Contains(FreeCloudModelGuard.BlockedMessage, ex.Message);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Cloud_WithFreeVisionModel_SendsImageAndParsesText()
    {
        var settings = new StubSettings { IsCloudEnabled = true, CloudSharingEnabled = true, ApiKey = "synthetic-key" };
        var handler = new StubHandler((_, _) => Success(
            "{\"choices\":[{\"message\":{\"content\":\"أرز 2\\nخبز 1\"}}]}"));
        var extractor = CreateExtractor(settings, handler, "free-vision:free");

        var result = await extractor.ExtractAsync(SyntheticPng, DocumentModelRoute.Cloud);

        Assert.Equal("أرز 2\nخبز 1", result.Text);
        Assert.Equal(DocumentModelRoute.Cloud, result.Route);
        Assert.NotEmpty(result.Notes);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains("image_url", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("Bearer", handler.LastAuthorization, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cloud_PersistedFreeVisionModel_TakesPrecedenceOverConfig()
    {
        var settings = new StubSettings
        {
            IsCloudEnabled = true,
            CloudSharingEnabled = true,
            ApiKey = "synthetic-key",
            CloudVisionModel = "persisted-vision:free"
        };
        var handler = new StubHandler((_, _) => Success(
            "{\"choices\":[{\"message\":{\"content\":\"أرز 2\"}}]}"));
        var extractor = CreateExtractor(settings, handler, "config-vision:free");

        var result = await extractor.ExtractAsync(SyntheticPng, DocumentModelRoute.Cloud);

        Assert.Equal("persisted-vision:free", result.Model);
        Assert.Contains("persisted-vision:free", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Local_SendsNoAuthorizationAndParsesOllamaResponse()
    {
        var settings = new StubSettings();
        var handler = new StubHandler((request, _) =>
            request.RequestUri!.AbsolutePath.EndsWith("/api/tags", StringComparison.Ordinal)
                ? Success("{\"models\":[{\"name\":\"llava:latest\"}]}")
                : Success("{\"message\":{\"content\":\"أرز 2\"}}"));
        var extractor = CreateExtractor(settings, handler, "unused:free", localModel: "llava");

        var result = await extractor.ExtractAsync(SyntheticPng, DocumentModelRoute.Local);

        Assert.Equal("أرز 2", result.Text);
        Assert.Equal(DocumentModelRoute.Local, result.Route);
        Assert.Equal("llava", result.Model);
        Assert.Empty(result.Notes);
        Assert.Null(handler.LastAuthorization);
        Assert.Contains("images", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cloud_WhenReasoningEatsOutputBudget_RetriesOnceWithHigherBudget()
    {
        var settings = new StubSettings { IsCloudEnabled = true, CloudSharingEnabled = true, ApiKey = "synthetic-key" };
        var bodies = new List<string>();
        var handler = new StubHandler((request, ct) =>
        {
            bodies.Add(request.Content!.ReadAsStringAsync(ct).GetAwaiter().GetResult());
            return bodies.Count == 1
                ? Success("{\"choices\":[{\"message\":{\"content\":\"\"},\"finish_reason\":\"length\"}]," +
                          "\"usage\":{\"completion_tokens\":8000,\"completion_tokens_details\":{\"reasoning_tokens\":7999}}}")
                : Success("{\"choices\":[{\"message\":{\"content\":\"أرز 2\"},\"finish_reason\":\"stop\"}]," +
                          "\"usage\":{\"completion_tokens\":42,\"completion_tokens_details\":{\"reasoning_tokens\":10}}}");
        });
        var extractor = CreateExtractor(settings, handler, "reasoning-vision:free");

        var result = await extractor.ExtractAsync(SyntheticPng, DocumentModelRoute.Cloud);

        Assert.Equal("أرز 2", result.Text);
        Assert.Equal(2, handler.CallCount);
        Assert.Contains("\"max_tokens\":8000", bodies[0]);
        Assert.Contains("\"effort\":\"low\"", bodies[0]);
        Assert.Contains("\"max_tokens\":16000", bodies[1]);
        Assert.Contains("\"effort\":\"low\"", bodies[1]);
    }

    [Fact]
    public async Task Cloud_WhenBothAttemptsLoseTextToReasoning_ReportsExactCause()
    {
        var settings = new StubSettings { IsCloudEnabled = true, CloudSharingEnabled = true, ApiKey = "synthetic-key" };
        var call = 0;
        var handler = new StubHandler((_, _) =>
        {
            call++;
            var total = call == 1 ? 8000 : 16000;
            var reasoning = call == 1 ? 7999 : 15990;
            return Success($"{{\"choices\":[{{\"message\":{{\"content\":\"\"}},\"finish_reason\":\"length\"}}]," +
                           $"\"usage\":{{\"completion_tokens\":{total},\"completion_tokens_details\":{{\"reasoning_tokens\":{reasoning}}}}}}}");
        });
        var extractor = CreateExtractor(settings, handler, "reasoning-vision:free");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(SyntheticPng, DocumentModelRoute.Cloud));

        Assert.Contains("استهلك التفكير", ex.Message);
        Assert.Contains("15990", ex.Message);
        Assert.Contains("16000", ex.Message);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Cloud_WhenNetworkFailsOnce_RetriesAndSucceeds()
    {
        var settings = new StubSettings { IsCloudEnabled = true, CloudSharingEnabled = true, ApiKey = "synthetic-key" };
        var call = 0;
        var handler = new StubHandler((_, _) =>
        {
            call++;
            return call == 1
                ? throw TransientNetworkError()
                : Success("{\"choices\":[{\"message\":{\"content\":\"أرز 2\"}}]}");
        });
        var extractor = CreateExtractor(settings, handler, "free-vision:free");

        var result = await extractor.ExtractAsync(SyntheticPng, DocumentModelRoute.Cloud);

        Assert.Equal("أرز 2", result.Text);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task Cloud_WhenNetworkFailsTwice_ReportsClearMessage()
    {
        var settings = new StubSettings { IsCloudEnabled = true, CloudSharingEnabled = true, ApiKey = "synthetic-key" };
        var handler = new StubHandler((_, _) => throw TransientNetworkError());
        var extractor = CreateExtractor(settings, handler, "free-vision:free");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(SyntheticPng, DocumentModelRoute.Cloud));

        Assert.Contains("تعذّر الاتصال بمزوّد النموذج بعد محاولتين", ex.Message);
        Assert.Equal(2, handler.CallCount);
    }

    private static HttpRequestException TransientNetworkError() =>
        new("No such host is known.", new SocketException((int)SocketError.HostNotFound));

    [Fact]
    public async Task Cloud_WhenRequestTimesOut_ReportsClearMessage()
    {
        var settings = new StubSettings { IsCloudEnabled = true, CloudSharingEnabled = true, ApiKey = "synthetic-key" };
        var handler = new StubHandler((_, _) => throw new TaskCanceledException());
        var extractor = CreateExtractor(settings, handler, "free-vision:free");

        var ex = await Assert.ThrowsAsync<TimeoutException>(() =>
            extractor.ExtractAsync(SyntheticPng, DocumentModelRoute.Cloud));

        Assert.Contains("استهلك استخراج الصورة الوقت المسموح", ex.Message);
    }

    [Fact]
    public async Task Cloud_WhenContentIsEmptyWithoutBudgetSignal_DoesNotRetry()
    {
        var settings = new StubSettings { IsCloudEnabled = true, CloudSharingEnabled = true, ApiKey = "synthetic-key" };
        var handler = new StubHandler((_, _) =>
            Success("{\"choices\":[{\"message\":{\"content\":\"\"},\"finish_reason\":\"stop\"}]}"));
        var extractor = CreateExtractor(settings, handler, "free-vision:free");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            extractor.ExtractAsync(SyntheticPng, DocumentModelRoute.Cloud));

        Assert.Equal("أعاد النموذج نصًا فارغًا.", ex.Message);
        Assert.Equal(1, handler.CallCount);
    }

    private static ModelDocumentExtractor CreateExtractor(
        StubSettings settings,
        StubHandler handler,
        string? visionModel,
        string localModel = "llava")
    {
        var values = new Dictionary<string, string?>
        {
            ["Ollama:BaseUrl"] = "http://localhost:11434",
            ["AI:PreferredCloudProvider"] = "test",
            ["AI:CloudProviders:test:BaseUrl"] = "https://example.invalid/v1",
            ["AI:CloudProviders:test:VisionModel"] = visionModel,
            ["AI:VisionExtraction:LocalModel"] = localModel
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ModelDocumentExtractor(settings, config, new HttpClient(handler));
    }

    private static HttpResponseMessage Success(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;
        public int CallCount { get; private set; }
        public string? LastBody { get; private set; }
        public string? LastAuthorization { get; private set; }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            LastAuthorization = request.Headers.Authorization?.ToString();
            return _respond(request, ct);
        }
    }

    private sealed class StubSettings : IAiSettingsProvider
    {
        public bool IsCloudEnabled { get; set; }
        public bool CloudSharingEnabled { get; set; }
        public string? PreferredLocalModel => null;
        public string LocalModelLevel { get; set; } = "light";
        public string? PreferredCloudProvider => "test";
        public int? MonthlyFreeTokenLimit => null;
        public int UsedTokensThisMonth => 0;
        public bool IsQuotaExceeded => false;
        public string? ApiKey { get; set; }
        public void RecordTokenUsage(int tokens) { }
        public string? GetCloudConfigHash() => null;
        public IEnumerable<CloudProviderConfig>? GetCloudProvidersConfig() => null;
        public string? GetCloudApiKey() => ApiKey;
        public void SetCloudApiKey(string apiKey) => ApiKey = apiKey;
        public string? CloudVisionModel { get; set; }
    }
}
