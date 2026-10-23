using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.AI;
using Wizz.Application.AI.Routing;
using Wizz.Application.DTOs;
using Wizz.Domain;
using Wizz.Infrastructure.AI;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Repositories;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات دمج أولوية المسارات في الموجّه:
/// تغيير ترتيب الأولوية يغيّر المسار المختار فعلياً، مع تسجيل قرار مفهوم.
/// </summary>
public sealed class ModelRouterPriorityTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WizzDbContext _db = null!;
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
        var unitOfWork = new UnitOfWork(_db);
        _assistant = new BuiltInAssistant(unitOfWork.Sales, unitOfWork.Products, unitOfWork.Store);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private ModelRouter CreateRouter(
        AutoRoutePriority? priority = null,
        RouteDecisionLog? log = null,
        ILlmProvider? localLlm = null,
        bool ollamaAvailable = true)
    {
        var settings = new FakeAiSettingsProvider();
        var device = new FakeDeviceEvaluator(ollamaAvailable);
        var quota = new QuotaTracker(null);
        var fallback = new FallbackPolicy(2);
        var factory = new FakeProviderFactory();

        return new ModelRouter(
            _assistant,
            localLlm,
            settings,
            device,
            quota,
            fallback,
            factory,
            memory: null,
            preferences: null,
            priority: priority,
            decisionLog: log);
    }

    [Fact]
    public async Task DefaultPriority_UsesBuiltIn_EvenWhenLocalIsAvailable()
    {
        var router = CreateRouter(
            priority: AutoRoutePriority.Default,
            localLlm: new FakeLocalProvider("إجابة محلية"));

        var result = await router.RouteAsync(new AiQueryInputDto("ما هي مبيعات اليوم؟", true, true, 10));

        Assert.Equal(RouteType.BuiltIn, result.RouteType);
        Assert.NotNull(result.Decision);
        Assert.Equal(RouteType.BuiltIn, result.Decision!.Chosen);

        // المسار السريع: المدمج أولاً وأجاب → لا فحص للمحلي (توفير زمن الاتصال)
        Assert.False(result.Decision.Candidates.Single(c => c.Route == RouteType.Local).Checked);
    }

    [Fact]
    public async Task LocalFirst_AnswersViaLocalModel_AndLogsDecision()
    {
        var priority = AutoRoutePriority.Default.Moved(RouteType.Local, 0);
        var log = new RouteDecisionLog();
        var router = CreateRouter(
            priority: priority,
            log: log,
            localLlm: new FakeLocalProvider("إجابة محلية"));

        // سؤال حر لا يعرفه المدمج → يطبَّق ترتيب الأولوية
        var result = await router.RouteAsync(new AiQueryInputDto("اكتب نصاً تعريفياً عن تنظيم الرفوف", true, true, 10));

        Assert.Equal(RouteType.Local, result.RouteType);
        Assert.Equal("إجابة محلية", result.Answer);
        Assert.NotNull(result.Decision);
        Assert.Equal(RouteType.Local, result.Decision!.Chosen);
        Assert.Contains("محلي", result.Decision.Reason);

        Assert.Equal(1, log.Count);
        var entry = log.Recent(1)[0];
        Assert.Equal("اكتب نصاً تعريفياً عن تنظيم الرفوف", entry.Question);
        Assert.Equal(RouteType.Local, entry.Route);
    }

    [Fact]
    public async Task LocalFirst_WhenLocalFails_WithFreeformQuestion_ReturnsNoRoute()
    {
        var priority = AutoRoutePriority.Default.Moved(RouteType.Local, 0);
        var router = CreateRouter(
            priority: priority,
            localLlm: new FailingLocalProvider());

        var result = await router.RouteAsync(new AiQueryInputDto("اكتب نصاً تعريفياً عن تنظيم الرفوف", true, true, 10));

        Assert.Equal(RouteType.None, result.RouteType);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task LocalFirst_WhenOllamaUnavailable_DoesNotUseLocalModel()
    {
        var priority = AutoRoutePriority.Default.Moved(RouteType.Local, 0);
        var router = CreateRouter(
            priority: priority,
            localLlm: new FakeLocalProvider("إجابة محلية"),
            ollamaAvailable: false);

        var result = await router.RouteAsync(new AiQueryInputDto("اكتب نصاً تعريفياً عن تنظيم الرفوف", true, true, 10));

        Assert.Equal(RouteType.None, result.RouteType);
        Assert.NotNull(result.Decision);
        Assert.False(result.Decision!.Candidates.Single(c => c.Route == RouteType.Local).Eligible);
    }

    [Fact]
    public async Task PrecisionQuestion_StaysBuiltIn_EvenWhenLocalIsFirst()
    {
        var priority = AutoRoutePriority.Default.Moved(RouteType.Local, 0);
        var router = CreateRouter(
            priority: priority,
            localLlm: new FakeLocalProvider("إجابة محلية"));

        var result = await router.RouteAsync(new AiQueryInputDto("ما هي مبيعات اليوم؟", true, true, 10));

        Assert.Equal(RouteType.BuiltIn, result.RouteType);
        Assert.Contains("مبيعات اليوم حتى الآن", result.Answer);
        Assert.NotNull(result.Decision);
        Assert.Equal(RouteType.BuiltIn, result.Decision!.Chosen);
        Assert.Contains("حماية أسئلة الأرقام", result.Decision.Reason);

        // المسار المحلي لم يُفحص أصلاً (توفير الوقت) ولا يمكن أن يفوز على أرقام المدمج
        var local = result.Decision.Candidates.Single(c => c.Route == RouteType.Local);
        Assert.False(local.Checked);
        Assert.False(local.Eligible);
    }

    [Fact]
    public async Task AnalyticalQuestion_WithLocalFirst_StillUsesLocalModel()
    {
        var priority = AutoRoutePriority.Default.Moved(RouteType.Local, 0);
        var router = CreateRouter(
            priority: priority,
            localLlm: new FakeLocalProvider("تحليل محلي"));

        // سؤال تحليلي صريح: الحماية لا تُطبَّق على التحليل
        var result = await router.RouteAsync(
            new AiQueryInputDto("حلل مبيعات اليوم واقترح تحسينات", true, true, 10));

        Assert.Equal(RouteType.Local, result.RouteType);
        Assert.Equal("تحليل محلي", result.Answer);
    }

    [Fact]
    public async Task ForcedRoute_ProducesManualDecisionAndIsLogged()
    {
        var log = new RouteDecisionLog();
        var router = CreateRouter(log: log);

        var result = await router.RouteAsync(
            new AiQueryInputDto("ما هي مبيعات اليوم؟", true, true, 10, RouteType.BuiltIn));

        Assert.Equal(RouteType.BuiltIn, result.RouteType);
        Assert.NotNull(result.Decision);
        Assert.Equal(RouteType.BuiltIn, result.Decision!.Chosen);
        Assert.Empty(result.Decision.Candidates);
        Assert.Contains("يدوي", result.Decision.Reason);
        Assert.Equal(1, log.Count);
        Assert.Contains("يدوي", log.Recent(1)[0].Reason);
    }

    /// <summary>مزوّد محلي وهمي يعيد إجابة ثابتة.</summary>
    private sealed class FakeLocalProvider : ILlmProvider
    {
        private readonly string _answer;

        public FakeLocalProvider(string answer) => _answer = answer;

        public string ProviderName => "Ollama (محلي)";

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<string> CompleteAsync(string prompt, CancellationToken ct = default) => Task.FromResult(_answer);

        public Task<string> CompleteWithContextAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
            => Task.FromResult(_answer);

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "test-model" });

        public Task SetModelAsync(string modelName, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>مزوّد محلي وهمي يفشل دائماً (لمحاكاة تعطّل Ollama بعد فحص Availability).</summary>
    private sealed class FailingLocalProvider : ILlmProvider
    {
        public string ProviderName => "Ollama (محلي)";

        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

        public Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
            => throw new InvalidOperationException("فشل متوقع");

        public Task<string> CompleteWithContextAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
            => throw new InvalidOperationException("فشل متوقع");

        public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Task SetModelAsync(string modelName, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeAiSettingsProvider : IAiSettingsProvider
    {
        public bool IsCloudEnabled { get; set; }

        public string? PreferredLocalModel => null;

        public string LocalModelLevel { get; set; } = "light";

        public string? PreferredCloudProvider => null;

        public int? MonthlyFreeTokenLimit => null;

        public int UsedTokensThisMonth { get; private set; }

        public bool IsQuotaExceeded => false;

        public void RecordTokenUsage(int tokens) => UsedTokensThisMonth += tokens;

        public void Reset() => UsedTokensThisMonth = 0;

        public string? GetCloudApiKey() => null;

        public void SetCloudApiKey(string apiKey)
        {
        }

        public string? GetCloudConfigHash() => "test-hash";

        public IEnumerable<CloudProviderConfig>? GetCloudProvidersConfig() => null;
    }

    private sealed class FakeProviderFactory : ILlmProviderFactory
    {
        public ILlmProvider? CreateCloudProvider(CloudProviderConfig config) => null;
    }

    private sealed class FakeDeviceEvaluator : IDeviceCapabilityEvaluator
    {
        private readonly bool _ollamaAvailable;

        public FakeDeviceEvaluator(bool ollamaAvailable) => _ollamaAvailable = ollamaAvailable;

        public void InvalidateCache()
        {
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
}
