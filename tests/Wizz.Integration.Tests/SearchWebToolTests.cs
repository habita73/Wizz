using Wizz.Application.AI;
using Wizz.Application.AI.Memory;
using Wizz.Application.AI.Tools;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات البحث في الإنترنت للمساعد المدمج (المرحلة 7):
/// مغلق افتراضياً، لا يلمس أسئلة المتجر، يُعلن مصدره، ويفشل بلطف دون إنترنت.
/// </summary>
public sealed class SearchWebToolTests
{
    private sealed class FakeSearch : IWebSearch
    {
        private readonly WebSearchResult? _result;
        private readonly Exception? _error;

        public FakeSearch(WebSearchResult? result = null, Exception? error = null)
        {
            _result = result;
            _error = error;
        }

        public int Calls { get; private set; }
        public string? LastQuery { get; private set; }

        public Task<WebSearchResult?> SearchAsync(string query, CancellationToken ct = default)
        {
            Calls++;
            LastQuery = query;
            return _error is not null ? Task.FromException<WebSearchResult?>(_error) : Task.FromResult(_result);
        }
    }

    private static BuiltInToolContext Context(string raw)
        => new()
        {
            Question = TextNormalizer.Normalize(raw),
            RawQuestion = raw,
            SnapshotFactory = _ => Task.FromResult(EmptySnapshot())
        };

    private static StoreSnapshot EmptySnapshot() => new(
        TodaySales: 0, TodayCount: 0, TodayPaid: 0, TodayRemaining: 0,
        YesterdaySales: 0, YesterdayCount: 0, YesterdayPaid: 0, YesterdayRemaining: 0,
        WeekSales: 0, WeekCount: 0, MonthSales: 0, MonthCount: 0,
        LifetimeSales: 0, LifetimeCount: 0, DebtsTotal: 0, Debts: new List<DebtItem>(),
        ProductCount: 0, ActiveProductCount: 0, StoreName: "متجر الاختبار",
        StoreAddress: null, StorePhone: null,
        TopProducts: new List<TopProduct>(), Products: new List<ProductInfo>());

    private static WebSearchResult Sample() => new(
        "ويكيبيديا العربية",
        "عثرت على مقالة:",
        new[] { new WebSearchSnippet("الجزائر", "الجزائر دولة في شمال أفريقيا.", "https://ar.wikipedia.org/wiki/الجزائر") });

    [Fact]
    public async Task Disabled_DoesNotSearch()
    {
        var search = new FakeSearch(Sample());
        var tool = new SearchWebTool(search, () => false);

        var answer = await tool.TryAnswerAsync(Context("ابحث عن تاريخ الجزائر"));

        Assert.Null(answer);
        Assert.Equal(0, search.Calls);
    }

    [Theory]
    [InlineData("ابحث عن سعر تفاح")]
    [InlineData("ابحث عن مبيعات اليوم")]
    [InlineData("من هو أفضل زبون في المتجر؟")]
    [InlineData("بحث عن ديون العملاء")]
    public async Task StoreQuestions_NeverGoToInternet(string question)
    {
        var search = new FakeSearch(Sample());
        var tool = new SearchWebTool(search, () => true);

        var answer = await tool.TryAnswerAsync(Context(question));

        Assert.Null(answer);
        Assert.Equal(0, search.Calls);
    }

    [Fact]
    public async Task ExplicitSearch_ReturnsAnswerWithProviderAndSource()
    {
        var search = new FakeSearch(Sample());
        var tool = new SearchWebTool(search, () => true);

        var answer = await tool.TryAnswerAsync(Context("ابحث عن تاريخ الجزائر"));

        Assert.NotNull(answer);
        Assert.True(SearchWebTool.IsWebAnswer(answer));
        Assert.Contains("ويكيبيديا العربية", answer);
        Assert.Contains("الجزائر", answer);
        Assert.Contains("https://ar.wikipedia.org/wiki/", answer);
        Assert.Contains("ليست من بيانات متجرك", answer);
        Assert.Equal("تاريخ الجزائر", search.LastQuery);
    }

    [Fact]
    public async Task NetworkFailure_ReturnsFriendlyMessage()
    {
        var search = new FakeSearch(error: new HttpRequestException("offline"));
        var tool = new SearchWebTool(search, () => true);

        var answer = await tool.TryAnswerAsync(Context("من هو ابن باديس؟"));

        Assert.NotNull(answer);
        Assert.Contains("تعذّر الوصول إلى الإنترنت", answer);
        Assert.Contains("يعمل محلياً", answer);
    }

    [Fact]
    public async Task NoResults_ExplainsAndSuggestsLocalData()
    {
        var search = new FakeSearch(result: null);
        var tool = new SearchWebTool(search, () => true);

        var answer = await tool.TryAnswerAsync(Context("ابحث عن شيء غير موجود"));

        Assert.NotNull(answer);
        Assert.Contains("لم أجد نتيجة", answer);
        Assert.Contains("متاحة محلياً", answer);
    }

    [Theory]
    [InlineData("ابحث عن تاريخ الجزائر", "تاريخ الجزائر")]
    [InlineData("من هو ابن باديس؟", "ابن باديس")]
    [InlineData("معلومات عن القهوة", "القهوة")]
    [InlineData("ما هي عاصمة الجزائر؟", "عاصمة الجزائر")]
    public void BuildQuery_StripsRequestPrefixes(string question, string expected)
    {
        Assert.Equal(expected, SearchWebTool.BuildQuery(question));
    }

    // ---- مفتاح التفضيلات (7-1) ----

    [Fact]
    public void Preference_InternetAccess_IsOffByDefault_AndSurvivesSnapshot()
    {
        var preferences = new UserPreferenceStore();
        Assert.False(preferences.AllowInternetAccess);

        preferences.SetAllowInternetAccess(true);
        var restored = UserPreferenceStore.FromSnapshot(preferences.ToSnapshot());

        Assert.True(restored.AllowInternetAccess);

        restored.Reset();
        Assert.False(restored.AllowInternetAccess);
    }

    [Fact]
    public void Preference_SnapshotFromOldVersion_KeepsInternetOff()
    {
        var restored = UserPreferenceStore.FromSnapshot(new Dictionary<string, string>
        {
            [UserPreferenceStore.RememberContextKey] = "true"
        });

        Assert.False(restored.AllowInternetAccess);
        Assert.True(restored.RememberContext);
    }
}
