using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.AI;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.Sales;
using Wizz.Domain;
using Wizz.Infrastructure.AI;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Repositories;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات موجه النماذج المستقل (Model Router):
/// التأكد من توجيه الأسئلة للمسار الصحيح (مدمج / محلي / سحابي / لا شيء)
/// والسلوك الصحيح عند تجاوز الحصة أو تعطل النموذج.
/// </summary>
public sealed class ModelRouterTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WizzDbContext _db = null!;
    private UnitOfWork _unitOfWork = null!;
    private BuiltInAssistant _assistant = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<WizzDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new WizzDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        _unitOfWork = new UnitOfWork(_db);
        _assistant = new BuiltInAssistant(_unitOfWork.Sales, _unitOfWork.Products, _unitOfWork.Store);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private IModelRouter CreateRouter(
        ILlmProvider? localLlm = null,
        bool cloudEnabled = false,
        int? monthlyLimit = null,
        bool ollamaAvailable = false)
    {
        var settings = new FakeAiSettingsProvider(cloudEnabled, monthlyLimit);
        var device = new FakeDeviceEvaluator(ollamaAvailable);
        var quota = new QuotaTracker(monthlyLimit);
        var fallback = new FallbackPolicy(2);
        var factory = new FakeProviderFactory();

        return new ModelRouter(_assistant, localLlm, settings, device, quota, fallback, factory);
    }

    [Fact]
    public async Task DirectQuestion_UsesBuiltInRoute()
    {
        // dispositivo بدون نموذج محلي، بدون سحابة
        var router = CreateRouter(localLlm: null, cloudEnabled: false);

        var answer = await router.RouteAsync(new AiQueryInputDto("ما هي مبيعات اليوم؟", false, false, 10));

        Assert.Equal(RouteType.BuiltIn, answer.RouteType);
        Assert.Contains("مبيعات اليوم حتى الآن", answer.Answer);
        Assert.False(answer.IsFallback);
    }

    [Fact]
    public async Task NoRouteAvailable_ReturnsHint()
    {
        var router = CreateRouter(localLlm: null, cloudEnabled: false);

        var answer = await router.RouteAsync(new AiQueryInputDto("اشرح لي الفرق بين المحاسبة النقدية والاستحقاق", false, false, 10));

        Assert.Equal(RouteType.None, answer.RouteType);
        Assert.NotNull(answer.Answer);
        Assert.Contains("لم أفهم", answer.Answer);
    }

    [Fact]
    public async Task HasCapableRoute_BuiltInAlwaysTrue()
    {
        var router = CreateRouter(localLlm: null, cloudEnabled: false);

        var hasRoute = await router.HasCapableRouteAsync(new AiQueryInputDto("قائمة الديون", false, false, 10));

        Assert.True(hasRoute);
    }

    [Fact]
    public async Task QuotaExceeded_CloudNotAvailable()
    {
        // حصة 0 تعني أن أي استخدام يتجاوز الحصة فوراً
        var router = CreateRouter(localLlm: null, cloudEnabled: true, monthlyLimit: 0);

        var status = await router.GetStatusAsync();

        Assert.False(status.CloudAvailable);
        Assert.Contains("المدمج", status.SummaryMessage);
    }

    [Fact]
    public async Task GetStatus_ReturnsCorrectSummary()
    {
        var router = CreateRouter(localLlm: null, cloudEnabled: false);

        var status = await router.GetStatusAsync();

        Assert.True(status.BuiltInAvailable);
        Assert.False(status.LocalAvailable);
        Assert.False(status.CloudAvailable);
        Assert.Equal(1, status.RouteCount);
    }

    /// <summary>إعدادات وهمية للاختبار.</summary>
    private sealed class FakeAiSettingsProvider : IAiSettingsProvider
    {
        private readonly int? _monthlyLimit;

        public FakeAiSettingsProvider(bool cloudEnabled, int? monthlyLimit)
        {
            IsCloudEnabled = cloudEnabled;
            _monthlyLimit = monthlyLimit;
        }

        public bool IsCloudEnabled { get; set; }
        public string? PreferredLocalModel => null;
        public string LocalModelLevel { get; set; } = "light";
        public string? PreferredCloudProvider => null;
        public int? MonthlyFreeTokenLimit => _monthlyLimit;
        public int UsedTokensThisMonth { get; private set; }
        public bool IsQuotaExceeded => _monthlyLimit.HasValue && UsedTokensThisMonth >= _monthlyLimit.Value;
        public void RecordTokenUsage(int tokens) => UsedTokensThisMonth += tokens;
        public void Reset() => UsedTokensThisMonth = 0;

        public string? GetCloudApiKey() => null;
        public void SetCloudApiKey(string apiKey) { }
        public string? GetCloudConfigHash() => "test-hash";
        public IEnumerable<CloudProviderConfig>? GetCloudProvidersConfig() => null;
    }

    /// <summary>مقيّم جهاز وهمي للاختبار.</summary>
    private sealed class FakeProviderFactory : ILlmProviderFactory
    {
        public ILlmProvider? CreateCloudProvider(CloudProviderConfig config) => null;
    }

    private sealed class FakeDeviceEvaluator : IDeviceCapabilityEvaluator
    {
        public void InvalidateCache() { }

        private readonly bool _ollamaAvailable;

        public FakeDeviceEvaluator(bool ollamaAvailable)
        {
            _ollamaAvailable = ollamaAvailable;
        }

        public Task<DeviceCapabilityAssessment> AssessAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new DeviceCapabilityAssessment(
                Tier: _ollamaAvailable ? DeviceCapabilityTier.Light : DeviceCapabilityTier.None,
                TotalRamBytes: 8L * 1024 * 1024 * 1024,
                OllamaAvailable: _ollamaAvailable,
                InternetAvailable: true,
                Reason: _ollamaAvailable ? "Ollama متاح" : "Ollama غير متاح"));
        }
    }

    [Fact]
    public void CloudLlmProvider_ProviderName_ReturnsConfiguredName()
    {
        var provider = new CloudLlmProvider(
            "https://openrouter.ai/api/v1",
            "test-key",
            "qwen/qwen3-32b",
            "OpenRouter (سحابي مجاني)");

        Assert.Equal("OpenRouter (سحابي مجاني)", provider.ProviderName);
    }

    [Fact]
    public async Task ModelRouter_WithCloudDisabled_CloudNotAvailable()
    {
        var router = CreateRouter(localLlm: null, cloudEnabled: true, monthlyLimit: null);

        var status = await router.GetStatusAsync();

        Assert.True(status.BuiltInAvailable);
        Assert.False(status.LocalAvailable);
        Assert.False(status.CloudAvailable);
        Assert.Equal(1, status.RouteCount);
    }
}
