using Wizz.Application.AI;
using Wizz.Application.AI.Tools;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات المرحلة ١ (تقوية المدمج) ببيانات اصطناعية فقط:
/// أدوات القراءة الآمنة، التحليلات الحتمية، مرادفات اللهجة المشتركة،
/// وحماية أرقام الأدوات من الاستبدال بنموذج لغوي.
/// لا يستخدم أي اختبار قاعدة بيانات تاجر حقيقية.
/// </summary>
public sealed class Phase1EmbeddedTests
{
    private static BuiltInToolContext Context(string raw, StoreSnapshot? snapshot = null)
        => new()
        {
            Question = TextNormalizer.Normalize(raw),
            RawQuestion = raw,
            SnapshotFactory = _ => Task.FromResult(snapshot ?? Snapshot())
        };

    private static StoreSnapshot Snapshot() => new(
        TodaySales: 200m, TodayCount: 2, TodayPaid: 200m, TodayRemaining: 0m,
        YesterdaySales: 100m, YesterdayCount: 1, YesterdayPaid: 100m, YesterdayRemaining: 0m,
        WeekSales: 700m, WeekCount: 5,
        PreviousWeekSales: 400m, PreviousWeekCount: 4,
        MonthSales: 900m, MonthCount: 9,
        LifetimeSales: 900m, LifetimeCount: 9, DebtsTotal: 150m,
        Debts: new List<DebtItem>
        {
            new(Guid.NewGuid(), "10", DateTime.UtcNow.AddDays(-40), 100m, DateTime.UtcNow.AddDays(-10), true, 10),
            new(Guid.NewGuid(), "11", DateTime.UtcNow.AddDays(-5), 50m, DateTime.UtcNow.AddDays(20), false, 0)
        },
        ProductCount: 2, ActiveProductCount: 2, StoreName: "متجر اصطناعي",
        StoreAddress: null, StorePhone: null,
        TopProducts: new List<TopProduct> { new("تفاح أحمر", 4m, 600m) },
        Products: new List<ProductInfo>
        {
            new("تفاح أحمر", "FRUIT-1", 150m, 120m, "كجم", true),
            new("حليب كامل", "MILK-1", 80m, 70m, "علبة", true)
        });

    // ---- التحليلات الحتمية ----

    [Fact]
    public async Task AverageSale_Today_UsesDeterministicDivision()
    {
        var answer = await new EmbeddedAnalyticsTool().TryAnswerAsync(Context("متوسط قيمة البيع اليوم"));

        Assert.NotNull(answer);
        Assert.Contains("100.00", answer);
        Assert.Contains("عدد العمليات: 2", answer);
    }

    [Fact]
    public async Task AverageSale_Week_UsesWeekSnapshotNotToday()
    {
        var answer = await new EmbeddedAnalyticsTool().TryAnswerAsync(Context("متوسط المبيعات آخر 7 أيام"));

        Assert.NotNull(answer);
        Assert.Contains("140.00", answer);
        Assert.Contains("آخر 7 أيام", answer);
    }

    [Fact]
    public async Task Comparison_Day_ReportsIncreaseFromSnapshot()
    {
        var answer = await new EmbeddedAnalyticsTool().TryAnswerAsync(Context("فرق المبيعات بين اليوم وأمس"));

        Assert.NotNull(answer);
        Assert.Contains("زيادة", answer);
        Assert.Contains("100.00", answer);
        Assert.Contains("أمس", answer);
    }

    [Fact]
    public async Task Comparison_Week_UsesPreviousWeekSnapshot()
    {
        var answer = await new EmbeddedAnalyticsTool().TryAnswerAsync(Context("فرق مبيعات الأسبوع"));

        Assert.NotNull(answer);
        Assert.Contains("الأسبوع السابق", answer);
        Assert.Contains("300.00", answer);
    }

    [Fact]
    public async Task OverdueDebts_CountsOnlyOverdue()
    {
        var answer = await new EmbeddedAnalyticsTool().TryAnswerAsync(Context("الديون المتأخرة"));

        Assert.NotNull(answer);
        Assert.Contains("1 عملية", answer);
        Assert.Contains("100.00", answer);
        Assert.DoesNotContain("150.00", answer);
    }

    [Fact]
    public async Task PeriodTotal_ExplicitQuestion_UsesNamedPeriod()
    {
        var answer = await new EmbeddedAnalyticsTool().TryAnswerAsync(Context("إجمالي مبيعات أمس"));

        Assert.NotNull(answer);
        Assert.Contains("أمس", answer);
        Assert.Contains("100.00", answer);
    }

    // ---- أدوات القراءة الآمنة واللهجة ----

    [Fact]
    public async Task ProductLookup_DialectPrice_ReturnsBothPrices()
    {
        var answer = await new ProductLookupTool().TryAnswerAsync(Context("شحال سعر تفاح"));

        Assert.NotNull(answer);
        Assert.Contains("تفاح أحمر", answer);
        Assert.Contains("150.00", answer);
        Assert.Contains("120.00", answer);
    }

    [Fact]
    public async Task ProductLookup_ShowMarker_ReturnsMatchingProduct()
    {
        var answer = await new ProductLookupTool().TryAnswerAsync(Context("دلني على حليب"));

        Assert.NotNull(answer);
        Assert.Contains("حليب كامل", answer);
        Assert.Contains("80.00", answer);
    }

    [Fact]
    public async Task ProductLookup_UnrelatedQuestion_ReturnsNull()
    {
        Assert.Null(await new ProductLookupTool().TryAnswerAsync(Context("كم الساعة")));
    }

    // ---- منع استيلاء الأدوات على الأسئلة القائمة ----

    [Fact]
    public async Task Registry_DoesNotStealExistingSalesQuestion()
    {
        var answer = await BuiltInToolRegistry.CreateDefault().TryAnswerAsync(Context("ما هي مبيعات اليوم؟"));

        Assert.Null(answer);
    }

    [Fact]
    public void Registry_DefaultContainsReadOnlyEmbeddedTools()
    {
        var names = BuiltInToolRegistry.CreateDefault().Tools.Select(t => t.Name).ToList();

        Assert.Contains("EmbeddedAnalytics", names);
        Assert.Contains("ProductLookup", names);
    }

    [Fact]
    public void ToolAnswers_AreProtectedAsPrecisionAnswers()
    {
        Assert.True(BuiltInAssistant.IsPrecisionAnswer(new BuiltInAnswer("رقم", null, "أدوات")));
    }

    [Fact]
    public void DialectSynonyms_SharedCore_IsNormalizedArabic()
    {
        Assert.Equal("البضاعه", TextNormalizer.Normalize("البضاعة"));
        Assert.Contains("البضاعه", DialectSynonyms.Goods);
        Assert.Contains("شحال", DialectSynonyms.Price);
        Assert.Contains("دلني", DialectSynonyms.Show);
    }

    [Fact]
    public void DialectSynonyms_EveryEntry_MatchesNormalizedForm()
    {
        Assert.All(DialectSynonyms.Goods, s => Assert.Equal(TextNormalizer.Normalize(s), s));
        Assert.All(DialectSynonyms.Show, s => Assert.Equal(TextNormalizer.Normalize(s), s));
        Assert.All(DialectSynonyms.Price, s => Assert.Equal(TextNormalizer.Normalize(s), s));
    }

    [Fact]
    public void DialectSynonyms_Goods_MatchNormalizedQuestion()
    {
        var q = TextNormalizer.Normalize("كم عدد البضاعة؟");

        Assert.True(TextNormalizer.HasAny(q, DialectSynonyms.Goods));
    }
}
