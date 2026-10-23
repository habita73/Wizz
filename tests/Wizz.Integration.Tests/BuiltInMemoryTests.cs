using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.AI;
using Wizz.Application.AI.Memory;
using Wizz.Application.AI.Tools;
using Wizz.Application.DTOs;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Repositories;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات تكامل ذاكرة المحادثة والتفضيلات مع المساعد المدمج:
/// تذكّر الموضوع بعد سؤال السعر، الإجابة عن سؤال المتابعة، ترتيب السعر حسب التفضيل،
/// احترام مفتاح الخصوصية، وتسجيل التبديلات في الموجّه.
/// </summary>
public sealed class BuiltInMemoryTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WizzDbContext _db = null!;
    private UnitOfWork _unitOfWork = null!;
    private ConversationMemory _memory = null!;
    private UserPreferenceStore _preferences = null!;
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

        _memory = new ConversationMemory();
        _preferences = new UserPreferenceStore();
        _assistant = new BuiltInAssistant(
            _unitOfWork.Sales,
            _unitOfWork.Products,
            _unitOfWork.Store,
            BuiltInToolRegistry.CreateDefault(),
            _memory,
            _preferences);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ---- تذكّر الموضوع ----

    [Fact]
    public async Task PriceQuestion_ReturnsSubject_WithoutMutatingMemory()
    {
        await SeedProductAsync("تفاح أحمر", "FRUIT-001", "كجم", 150m, 120m);

        var answer = await _assistant.TryAnswerWithSubjectAsync("كم سعر تفاح أحمر؟");

        Assert.NotNull(answer);
        Assert.Equal("تفاح أحمر", answer!.Subject);
        Assert.Equal("سعر", answer.Intent);

        // تسجيل الموضوع في الذاكرة مسؤولية الموجّه (ModelRouter) لا المساعد المدمج.
        Assert.Null(_memory.LastSubject);
    }

    [Fact]
    public async Task PriceQuestion_WithDefiniteArticle_MatchesProduct()
    {
        await SeedProductAsync("خبز أبيض", "BAKERY-001", "رغيف", 30m, 25m);

        var answer = await _assistant.TryAnswerWithSubjectAsync("بكم سعر الخبز الأبيض؟");

        Assert.NotNull(answer);
        Assert.Equal("خبز أبيض", answer!.Subject);
        Assert.Contains("30.00", answer.Text);
    }

    [Fact]
    public async Task FollowUpQuestion_UsesRememberedSubject()
    {
        await SeedProductAsync("تفاح أحمر", "FRUIT-001", "كجم", 150m, 120m);
        await SeedProductAsync("خبز أبيض", "BAKERY-001", "رغيف", 30m, 25m);

        _memory.SetLastSubject("تفاح أحمر");

        var answer = await _assistant.TryAnswerAsync("كم سعره؟");

        Assert.NotNull(answer);
        Assert.Contains("تفاح أحمر", answer);
        Assert.Contains("لآخر منتج سألت عنه", answer);
        Assert.Contains("150.00", answer);
    }

    [Fact]
    public async Task FollowUpQuestion_WithoutRememberedSubject_AsksForProductName()
    {
        await SeedProductAsync("تفاح أحمر", "FRUIT-001", "كجم", 150m, 120m);

        var answer = await _assistant.TryAnswerAsync("كم سعره؟");

        Assert.NotNull(answer);
        Assert.Contains("اكتب اسم المنتج", answer);
    }

    [Fact]
    public async Task FollowUpQuestion_WhenContextDisabled_DoesNotUseRememberedSubject()
    {
        await SeedProductAsync("تفاح أحمر", "FRUIT-001", "كجم", 150m, 120m);

        _memory.SetLastSubject("تفاح أحمر");
        _preferences.SetRememberContext(false);

        var answer = await _assistant.TryAnswerAsync("كم سعره؟");

        Assert.NotNull(answer);
        Assert.Contains("اكتب اسم المنتج", answer);
        Assert.DoesNotContain("لآخر منتج سألت عنه", answer);
    }

    // ---- تفضيل نوع السعر ----

    [Fact]
    public async Task PriceAnswer_ByDefault_PutsRetailPriceFirst()
    {
        await SeedProductAsync("تفاح أحمر", "FRUIT-001", "كجم", 150m, 120m);

        var answer = await _assistant.TryAnswerAsync("كم سعر تفاح أحمر؟");

        Assert.NotNull(answer);
        var retailIndex = answer!.IndexOf("التجزئة", StringComparison.Ordinal);
        var wholesaleIndex = answer.IndexOf("الجملة", StringComparison.Ordinal);
        Assert.True(retailIndex >= 0);
        Assert.True(wholesaleIndex >= 0);
        Assert.True(retailIndex < wholesaleIndex);
    }

    [Fact]
    public async Task PriceAnswer_WithWholesalePreference_PutsWholesalePriceFirst()
    {
        await SeedProductAsync("تفاح أحمر", "FRUIT-001", "كجم", 150m, 120m);

        _preferences.SetPriceMode(PriceMode.Wholesale);

        var answer = await _assistant.TryAnswerAsync("كم سعر تفاح أحمر؟");

        Assert.NotNull(answer);
        var wholesaleIndex = answer!.IndexOf("الجملة", StringComparison.Ordinal);
        var retailIndex = answer.IndexOf("التجزئة", StringComparison.Ordinal);
        Assert.True(wholesaleIndex >= 0);
        Assert.True(retailIndex >= 0);
        Assert.True(wholesaleIndex < retailIndex);
        Assert.Contains("120.00", answer);
    }

    // ---- تفضيل وحدة القياس ----

    [Fact]
    public async Task ProductList_WithPreferredUnit_PutsMatchingUnitFirst()
    {
        await SeedProductAsync("زيت زيتون", "OIL-001", "لتر", 500m, 450m);
        await SeedProductAsync("سكر أبيض", "SUGAR-001", "كجم", 120m, 100m);

        _preferences.SetPreferredUnit("لتر");

        var answer = await _assistant.TryAnswerAsync("قائمة المنتجات");

        Assert.NotNull(answer);
        Assert.Contains("وحدة القياس المفضّلة: لتر", answer);
        var oilIndex = answer!.IndexOf("زيت زيتون", StringComparison.Ordinal);
        var sugarIndex = answer.IndexOf("سكر أبيض", StringComparison.Ordinal);
        Assert.True(oilIndex >= 0);
        Assert.True(sugarIndex >= 0);
        Assert.True(oilIndex < sugarIndex);
    }

    // ---- تسجيل التبديلات في الموجّه ----

    [Fact]
    public async Task ModelRouter_RecordsTurnAndSubject_InMemory()
    {
        await SeedProductAsync("تفاح أحمر", "FRUIT-001", "كجم", 150m, 120m);

        var router = CreateRouter();

        var result = await router.RouteAsync(new AiQueryInputDto("كم سعر تفاح أحمر؟", false, false, 10));

        Assert.Equal(RouteType.BuiltIn, result.RouteType);
        Assert.Equal(1, _memory.Count);
        Assert.Equal("كم سعر تفاح أحمر؟", _memory.Last!.Question);
        Assert.Equal("تفاح أحمر", _memory.LastSubject);
    }

    [Fact]
    public async Task ModelRouter_WhenContextDisabled_DoesNotRecordTurn()
    {
        await SeedProductAsync("تفاح أحمر", "FRUIT-001", "كجم", 150m, 120m);

        _preferences.SetRememberContext(false);
        var router = CreateRouter();

        var result = await router.RouteAsync(new AiQueryInputDto("كم سعر تفاح أحمر؟", false, false, 10));

        Assert.Equal(RouteType.BuiltIn, result.RouteType);
        Assert.Equal(0, _memory.Count);
        Assert.Null(_memory.LastSubject);
    }

    [Fact]
    public async Task ModelRouter_FailedRoute_IsNotRecorded()
    {
        var router = CreateRouter();

        var result = await router.RouteAsync(new AiQueryInputDto("اشرح لي الفرق بين المحاسبة النقدية والاستحقاق", false, false, 10));

        Assert.Equal(RouteType.None, result.RouteType);
        Assert.Equal(0, _memory.Count);
    }

    // ---- أدوات ----

    private IModelRouter CreateRouter()
    {
        var settings = new FakeAiSettingsProvider();
        var device = new FakeDeviceEvaluator();
        var quota = new QuotaTracker(null);
        var fallback = new FallbackPolicy(2);
        var factory = new FakeProviderFactory();

        return new ModelRouter(
            _assistant, null, settings, device, quota, fallback, factory, _memory, _preferences);
    }

    private async Task<Product> SeedProductAsync(string name, string barcode, string unitSymbol, decimal retail, decimal wholesale)
    {
        if (!await _unitOfWork.Store.ExistsAsync())
        {
            await _unitOfWork.Store.AddAsync(
                new Store("متجر الاختبار", "الجزائر العاصمة", "0550000000", "000000000000000", "00/00-0000000", true));
            await _unitOfWork.SaveChangesAsync();
        }

        var product = new Product(name, retail, wholesale, false, barcode, "وصف للاختبار");
        product.AddUnit("وحدة", unitSymbol, 1m);
        await _unitOfWork.Products.AddAsync(product);
        await _unitOfWork.SaveChangesAsync();
        return product;
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
        public void SetCloudApiKey(string apiKey) { }
        public string? GetCloudConfigHash() => "test-hash";
        public IEnumerable<CloudProviderConfig>? GetCloudProvidersConfig() => null;
    }

    private sealed class FakeProviderFactory : ILlmProviderFactory
    {
        public ILlmProvider? CreateCloudProvider(CloudProviderConfig config) => null;
    }

    private sealed class FakeDeviceEvaluator : IDeviceCapabilityEvaluator
    {
        public void InvalidateCache() { }

        public Task<DeviceCapabilityAssessment> AssessAsync(CancellationToken ct = default)
        {
            return Task.FromResult(new DeviceCapabilityAssessment(
                Tier: DeviceCapabilityTier.None,
                TotalRamBytes: 8L * 1024 * 1024 * 1024,
                OllamaAvailable: false,
                InternetAvailable: false,
                Reason: "اختبار بلا نموذج"));
        }
    }
}
