using Wizz.Application.AI;
using Wizz.Application.AI.Tools;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات أدوات النظام للمساعد المدمج: الوقت/التاريخ، الأقراص، قوالب الطباعة،
/// معلومات النظام، وسجل الأدوات (BuiltInToolRegistry).
/// </summary>
public sealed class BuiltInToolsTests
{
    private static BuiltInToolContext Context(string raw)
        => new()
        {
            Question = Normalize(raw),
            RawQuestion = raw,
            SnapshotFactory = _ => Task.FromResult(TestSnapshot())
        };

    /// <summary>تطبيع مبسّط يطابق منطق المساعد المدمج (همزات/تاء مربوطة).</summary>
    private static string Normalize(string text)
        => text.Trim().ToLowerInvariant()
            .Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا')
            .Replace('ى', 'ي').Replace('ة', 'ه');

    private static StoreSnapshot TestSnapshot() => new(
        TodaySales: 0, TodayCount: 0, TodayPaid: 0, TodayRemaining: 0,
        YesterdaySales: 0, YesterdayCount: 0, YesterdayPaid: 0, YesterdayRemaining: 0,
        WeekSales: 0, WeekCount: 0, MonthSales: 0, MonthCount: 0,
        LifetimeSales: 0, LifetimeCount: 0, DebtsTotal: 0, Debts: new List<DebtItem>(),
        ProductCount: 0, ActiveProductCount: 0, StoreName: "متجر الاختبار",
        StoreAddress: null, StorePhone: null,
        TopProducts: new List<TopProduct>(), Products: new List<ProductInfo>());

    // ---- GetTimeTool ----

    [Fact]
    public async Task GetTime_AnswersTimeQuestion()
    {
        var tool = new GetTimeTool(() => new DateTime(2026, 10, 22, 14, 30, 0), TimeZoneInfo.Utc);

        var answer = await tool.TryAnswerAsync(Context("كم الساعة الآن؟"));

        Assert.NotNull(answer);
        Assert.Contains("14:30", answer);
        Assert.Contains("2026", answer);
    }

    [Fact]
    public async Task GetTime_AnswersDateQuestion()
    {
        var tool = new GetTimeTool(() => new DateTime(2026, 1, 5, 9, 0, 0), TimeZoneInfo.Utc);

        var answer = await tool.TryAnswerAsync(Context("ما تاريخ اليوم؟"));

        Assert.NotNull(answer);
        Assert.Contains("2026", answer);
    }

    [Fact]
    public async Task GetTime_UnrelatedQuestion_ReturnsNull()
    {
        var tool = new GetTimeTool();

        Assert.Null(await tool.TryAnswerAsync(Context("قائمة الديون")));
    }

    // ---- GetDriveInfoTool ----

    [Fact]
    public async Task GetDriveInfo_ReportsFreeAndUsed()
    {
        // 100 GB إجمالي، 5 GB حر → مستخدم 95% + تنبيه.
        var tool = new GetDriveInfoTool(() => new List<DriveSpace>
        {
            new("C:\\", "NTFS", 100L * 1024 * 1024 * 1024, 5L * 1024 * 1024 * 1024, true)
        });

        var answer = await tool.TryAnswerAsync(Context("كم مساحة القرص المتبقية؟"));

        Assert.NotNull(answer);
        Assert.Contains("حالة الأقراص", answer);
        Assert.Contains("C:", answer);
        Assert.Contains("95%", answer);
        Assert.Contains("شبه ممتلئ", answer);
    }

    [Fact]
    public async Task GetDriveInfo_HealthyDrive_NoWarning()
    {
        var tool = new GetDriveInfoTool(() => new List<DriveSpace>
        {
            new("D:\\", "NTFS", 500L * 1024 * 1024 * 1024, 300L * 1024 * 1024 * 1024, true)
        });

        var answer = await tool.TryAnswerAsync(Context("مساحة القرص"));

        Assert.NotNull(answer);
        Assert.DoesNotContain("شبه ممتلئ", answer);
    }

    [Fact]
    public async Task GetDriveInfo_NoDrives_ReportsClearly()
    {
        var tool = new GetDriveInfoTool(() => Array.Empty<DriveSpace>());

        var answer = await tool.TryAnswerAsync(Context("كم مساحة القرص المتبقية؟"));

        Assert.NotNull(answer);
        Assert.Contains("لم أجد أقراصاً", answer);
    }

    [Fact]
    public async Task GetDriveInfo_UnrelatedQuestion_ReturnsNull()
    {
        var tool = new GetDriveInfoTool();

        Assert.Null(await tool.TryAnswerAsync(Context("أفضل المنتجات")));
    }

    // ---- GetPrintTemplatesTool ----

    [Fact]
    public async Task GetPrintTemplates_ListsDocumentsAndPrinter()
    {
        var tool = new GetPrintTemplatesTool(() => new PrinterInfo("طابعة الاختبار", true, 58));

        var answer = await tool.TryAnswerAsync(Context("ما أنواع الطباعة المتاحة؟"));

        Assert.NotNull(answer);
        Assert.Contains("طابعة الاختبار", answer);
        Assert.Contains("58", answer);
        Assert.Contains("إيصال", answer);
        Assert.Contains("PDF", answer);
    }

    [Fact]
    public async Task GetPrintTemplates_DoesNotStealReceiptInstructionQuestion()
    {
        var tool = new GetPrintTemplatesTool();

        // سؤال «كيف أطبع إيصال» يخصّ المساعد الإجرائي، لا أداة القوالب.
        Assert.Null(await tool.TryAnswerAsync(Context("كيف اطبع ايصال؟")));
    }

    [Fact]
    public async Task GetPrintTemplates_UnrelatedQuestion_ReturnsNull()
    {
        var tool = new GetPrintTemplatesTool();

        Assert.Null(await tool.TryAnswerAsync(Context("مبيعات اليوم")));
    }

    // ---- GetSystemInfoTool ----

    [Fact]
    public async Task GetSystemInfo_ReportsVersionAndSpecs()
    {
        var tool = new GetSystemInfoTool(() => new SystemInfo(
            AppVersion: "0.3.1",
            OsDescription: "Windows 11",
            Architecture: "X64",
            ProcessorCount: 8,
            TotalRamGb: 16.0,
            DotNetVersion: "10.0.0",
            Culture: "ar-DZ",
            IsRtl: true));

        var answer = await tool.TryAnswerAsync(Context("أعطني مواصفات الجهاز"));

        Assert.NotNull(answer);
        Assert.Contains("0.3.1", answer);
        Assert.Contains("Windows 11", answer);
        Assert.Contains("16", answer);
        Assert.Contains("نعم", answer); // RTL
    }

    [Fact]
    public async Task GetSystemInfo_LowRam_AddsAdvisory()
    {
        var tool = new GetSystemInfoTool(() => new SystemInfo(
            "1.0", "Linux", "Arm64", 2, 2.0, "10.0.0", "ar", true));

        var answer = await tool.TryAnswerAsync(Context("كم رام الجهاز؟"));

        Assert.NotNull(answer);
        Assert.Contains("محدودة", answer);
    }

    [Fact]
    public async Task GetSystemInfo_UnrelatedQuestion_ReturnsNull()
    {
        var tool = new GetSystemInfoTool();

        Assert.Null(await tool.TryAnswerAsync(Context("عدد المنتجات")));
    }

    // ---- BuiltInToolRegistry ----

    [Fact]
    public void Registry_DefaultOrdersToolsByPriority()
    {
        var registry = BuiltInToolRegistry.CreateDefault();

        Assert.Equal(4, registry.Tools.Count);
        var orders = registry.Tools.Select(t => t.Order).ToList();
        Assert.Equal(orders.OrderBy(o => o), orders);
    }

    [Fact]
    public async Task Registry_ReturnsFirstMatchingToolAnswer()
    {
        var registry = BuiltInToolRegistry.CreateDefault(
            now: () => new DateTime(2026, 10, 22, 8, 15, 0),
            timeZone: TimeZoneInfo.Utc);

        var answer = await registry.TryAnswerAsync(Context("كم الوقت الآن؟"));

        Assert.NotNull(answer);
        Assert.Contains("08:15", answer);
    }

    [Fact]
    public async Task Registry_NoMatch_ReturnsNull()
    {
        var registry = BuiltInToolRegistry.CreateDefault();

        Assert.Null(await registry.TryAnswerAsync(Context("لون السماء أزرق")));
    }

    [Fact]
    public async Task Registry_FailingTool_DoesNotBreakChain()
    {
        var registry = new BuiltInToolRegistry(new IBuiltInTool[]
        {
            new ThrowingTool(order: 1),
            new GetTimeTool(() => new DateTime(2026, 10, 22, 12, 0, 0), TimeZoneInfo.Utc)
        });

        var answer = await registry.TryAnswerAsync(Context("كم الساعة؟"));

        Assert.NotNull(answer);
        Assert.Contains("12:00", answer);
    }

    [Fact]
    public async Task Registry_AllToolsFail_ReturnsErrorSummary()
    {
        var registry = new BuiltInToolRegistry(new IBuiltInTool[]
        {
            new ThrowingTool(order: 1)
        });

        var answer = await registry.TryAnswerAsync(Context("أي سؤال"));

        Assert.NotNull(answer);
        Assert.Contains("تعذر تنفيذ", answer);
    }

    private sealed class ThrowingTool : IBuiltInTool
    {
        public ThrowingTool(int order) => Order = order;
        public string Name => "Throwing";
        public string Description => "أداة تفشل دائماً (للاختبار).";
        public int Order { get; }
        public Task<string?> TryAnswerAsync(BuiltInToolContext context, CancellationToken ct = default)
            => throw new InvalidOperationException("فشل متعمد");
    }
}
