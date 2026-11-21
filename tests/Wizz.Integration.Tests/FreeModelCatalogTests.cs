using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Wizz.Infrastructure.AI;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية: قراءة الكتالوج العام لا تُرسل مفتاحًا ولا بيانات،
/// وتصفّي النماذج المجانية السارية فقط، وتُبقي آخر قراءة عند الفشل.
/// </summary>
[Collection(AiSettingsEnvironmentCollection.Name)]
public sealed class FreeModelCatalogTests
{
    private const string SyntheticCatalog = """
    {
      "data": [
        {
          "id": "vendor/paid-vision",
          "name": "Paid Vision",
          "context_length": 900000,
          "pricing": { "prompt": "0.001", "completion": "0.002" },
          "architecture": { "input_modalities": ["text", "image"] }
        },
        {
          "id": "vendor/free-vision:free",
          "name": "Free Vision",
          "context_length": 500000,
          "pricing": { "prompt": "0", "completion": "0" },
          "architecture": { "input_modalities": ["text", "image"] }
        },
        {
          "id": "vendor/free-chat:free",
          "name": "Free Chat",
          "context_length": 700000,
          "pricing": { "prompt": "0", "completion": "0" },
          "architecture": { "input_modalities": ["text"] }
        },
        {
          "id": "vendor/expired-vision:free",
          "name": "Expired",
          "context_length": 999999,
          "pricing": { "prompt": "0", "completion": "0" },
          "expiration_date": "2020-01-01T00:00:00Z",
          "architecture": { "input_modalities": ["text", "image"] }
        },
        {
          "id": "vendor/no-suffix",
          "name": "No Suffix",
          "context_length": 999999,
          "pricing": { "prompt": "0", "completion": "0" },
          "architecture": { "input_modalities": ["text", "image"] }
        }
      ]
    }
    """;

    [Fact]
    public async Task Refresh_FiltersToFreeModelsAndSplitsVisionFromChat()
    {
        var (service, handler) = CreateService(SyntheticCatalog);
        var snapshot = await service.RefreshAsync();

        Assert.False(snapshot.HasError);
        Assert.Equal("vendor/free-chat:free", snapshot.SuggestedChatModel);
        Assert.Equal("vendor/free-vision:free", snapshot.SuggestedVisionModel);
        Assert.DoesNotContain(snapshot.ChatCandidates, c => c.SupportsImage is false && c.ModelId.Contains("paid", StringComparison.OrdinalIgnoreCase));
        Assert.All(snapshot.VisionCandidates, c => Assert.True(c.SupportsImage));
        Assert.All(snapshot.ChatCandidates, c => Assert.EndsWith(":free", c.ModelId, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(snapshot.ChatCandidates, c => c.ModelId.Contains("expired", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Refresh_SendsNoAuthorizationHeaderOnlyPublicGet()
    {
        var (service, handler) = CreateService(SyntheticCatalog);

        await service.RefreshAsync();

        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Get, handler.LastMethod);
        Assert.Null(handler.LastAuthorization);
        Assert.Null(handler.LastBody);
        Assert.EndsWith("/models", handler.LastUrl, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_OnFailure_KeepsLastSnapshotAndRecordsError()
    {
        var (service, handler) = CreateService(SyntheticCatalog);
        var good = await service.RefreshAsync();
        handler.FailWithTimeout = true;

        var afterFailure = await service.RefreshAsync();

        Assert.True(afterFailure.HasError);
        Assert.Equal(good.SuggestedChatModel, afterFailure.SuggestedChatModel);
        Assert.Equal(good.SuggestedVisionModel, afterFailure.SuggestedVisionModel);
    }

    [Fact]
    public void Cached_SurvivesReloadFromLocalFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "wizz-catalog-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var (first, _) = CreateService(SyntheticCatalog, path);
            var snapshot = first.RefreshAsync().GetAwaiter().GetResult();

            var (second, _) = CreateService(SyntheticCatalog, path);
            var reloaded = second.Cached;

            Assert.NotNull(reloaded);
            Assert.Equal(snapshot.SuggestedChatModel, reloaded!.SuggestedChatModel);
            Assert.Equal(snapshot.FetchedAt, reloaded.FetchedAt);
        }
        finally
        {
            try { File.Delete(path); } catch { /* تنظيف أفضل جهد */ }
        }
    }

    [Fact]
    public void AiSettings_ChangingModelNames_ChangesCloudConfigHash()
    {
        var path = Path.Combine(Path.GetTempPath(), "wizz-ai-hash-" + Guid.NewGuid().ToString("N") + ".json");
        var previous = Environment.GetEnvironmentVariable("WIZZ_AI_SETTINGS_PATH");
        try
        {
            Environment.SetEnvironmentVariable("WIZZ_AI_SETTINGS_PATH", path);
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:PreferredCloudProvider"] = "openrouter",
                ["AI:CloudProviders:openrouter:BaseUrl"] = "https://openrouter.ai/api/v1",
                ["AI:CloudProviders:openrouter:ApiKey"] = "synthetic-key"
            }).Build();
            var settings = new AiSettingsProvider(config);
            settings.IsCloudEnabled = true;
            settings.SetCloudApiKey("synthetic-key");
            var before = settings.GetCloudConfigHash();

            settings.CloudChatModel = "vendor/new-chat:free";
            var after = settings.GetCloudConfigHash();

            Assert.NotEqual(before, after);
        }
        finally
        {
            Environment.SetEnvironmentVariable("WIZZ_AI_SETTINGS_PATH", previous);
            try { File.Delete(path); } catch { /* تنظيف أفضل جهد */ }
        }
    }

    private static (FreeModelCatalogService Service, StubHandler Handler) CreateService(string body, string? cachePath = null)
    {
        var handler = new StubHandler(body);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AI:PreferredCloudProvider"] = "openrouter",
            ["AI:CloudProviders:openrouter:BaseUrl"] = "https://openrouter.ai/api/v1"
        }).Build();
        var path = cachePath ?? Path.Combine(Path.GetTempPath(), "wizz-catalog-" + Guid.NewGuid().ToString("N") + ".json");
        var service = new FreeModelCatalogService(new HttpClient(handler), config, cachePath: path);
        return (service, handler);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _body;
        public bool FailWithTimeout { get; set; }
        public int CallCount { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public string? LastUrl { get; private set; }
        public string? LastAuthorization { get; private set; }
        public string? LastBody { get; private set; }

        public StubHandler(string body) => _body = body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CallCount++;
            LastMethod = request.Method;
            LastUrl = request.RequestUri?.ToString();
            LastAuthorization = request.Headers.Authorization?.ToString();
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            if (FailWithTimeout)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json")
            };
        }
    }
}
