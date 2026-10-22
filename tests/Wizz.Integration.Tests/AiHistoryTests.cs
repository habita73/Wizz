using Wizz.Application.AI.History;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات تاريخ أسئلة المساعد: عَدّاد التكرار، أعلى 5، التطبيع (همزات/أرقام/تشكيل)،
/// الحد الأقصى، المسح، والاقتراحات السياقية.
/// </summary>
public sealed class AiHistoryTests
{
    private static DateTime At(int minutes) => new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);

    // ---- Record / Count ----

    [Fact]
    public void Record_NewQuestion_AddsWithCountOne()
    {
        var svc = new AiHistoryService();

        svc.Record("مبيعات اليوم");

        var e = Assert.Single(svc.Entries);
        Assert.Equal("مبيعات اليوم", e.Question);
        Assert.Equal(1, e.Count);
    }

    [Fact]
    public void Record_SameQuestion_IncrementsCountAndDoesNotDuplicate()
    {
        var svc = new AiHistoryService();

        svc.Record("قائمة الديون");
        svc.Record("قائمة الديون");
        svc.Record("قائمة الديون");

        var e = Assert.Single(svc.Entries);
        Assert.Equal(3, e.Count);
    }

    [Fact]
    public void Record_NormalizedEquivalents_IncrementSameEntry()
    {
        var svc = new AiHistoryService();

        // اختلاف الهمزة/التاء المربوطة/التشكيل/المسافات لا يُنشئ سؤالاً جديداً.
        svc.Record("أفضل المنتجات");
        svc.Record("افضل المنتجات");
        svc.Record("أفضل  المنتجات ");

        Assert.Single(svc.Entries);
        Assert.Equal(3, svc.Entries[0].Count);
    }

    [Fact]
    public void Record_DifferentQuestions_AreSeparateEntries()
    {
        var svc = new AiHistoryService();

        svc.Record("أفضل المنتجات");
        svc.Record("افضل المنتجات من فضلك");

        Assert.Equal(2, svc.Entries.Count);
    }

    [Fact]
    public void Record_BlankQuestion_IsIgnored()
    {
        var svc = new AiHistoryService();

        svc.Record("   ");
        svc.Record("");

        Assert.Equal(0, svc.Count);
    }

    [Fact]
    public void Record_LongQuestion_IsTruncated()
    {
        var svc = new AiHistoryService();
        svc.Record(new string('س', 500));

        Assert.Equal(AiHistoryService.MaxQuestionLength, svc.Entries[0].Question.Length);
    }

    [Fact]
    public void Record_MultiLine_IsCollapsedToOneLine()
    {
        var svc = new AiHistoryService();
        svc.Record("سطر أول\nسطر ثانٍ\nسطر ثالث");

        Assert.Equal("سطر أول سطر ثانٍ سطر ثالث", svc.Entries[0].Question);
    }

    // ---- Top / ordering ----

    [Fact]
    public void Top_OrdersByCountThenRecency()
    {
        var svc = new AiHistoryService();
        svc.Record("أ", At(1));
        svc.Record("ب", At(2));
        svc.Record("ب", At(3));
        svc.Record("ج", At(4));
        svc.Record("ج", At(5));
        svc.Record("ج", At(6));

        var top = svc.Top(2);

        Assert.Equal(2, top.Count);
        Assert.Equal("ج", top[0].Question);
        Assert.Equal("ب", top[1].Question);
    }

    [Fact]
    public void Top_TieBreaksByMostRecent()
    {
        var svc = new AiHistoryService();
        svc.Record("قديم", At(1));
        svc.Record("حديث", At(10));

        var top = svc.Top(5);

        Assert.Equal("حديث", top[0].Question);
        Assert.Equal("قديم", top[1].Question);
    }

    [Fact]
    public void Top_NonPositive_ReturnsEmpty()
    {
        var svc = new AiHistoryService();
        svc.Record("سؤال");

        Assert.Empty(svc.Top(0));
        Assert.Empty(svc.Top(-3));
    }

    // ---- Pruning ----

    [Fact]
    public void Record_ExceedingMax_KeepsMostFrequent()
    {
        var svc = new AiHistoryService();

        // سؤال متكرر جداً
        for (var i = 0; i < 5; i++) svc.Record("مهم", At(i));

        // نملأ ما يفوق الحد الأقصى بأسئلة لمرة واحدة
        for (var i = 0; i < AiHistoryService.MaxEntries + 10; i++)
            svc.Record($"عابر {i}", At(100 + i));

        Assert.True(svc.Count <= AiHistoryService.MaxEntries);
        Assert.Contains(svc.Entries, e => e.Question == "مهم");
    }

    // ---- Clear / Remove ----

    [Fact]
    public void Clear_RemovesEverything()
    {
        var svc = new AiHistoryService();
        svc.Record("أ");
        svc.Record("ب");

        svc.Clear();

        Assert.Equal(0, svc.Count);
    }

    [Fact]
    public void Remove_DeletesMatchingNormalizedEntry()
    {
        var svc = new AiHistoryService();
        svc.Record("قائمة المنتجات");

        var removed = svc.Remove("قائمه المنتجات");

        Assert.True(removed);
        Assert.Equal(0, svc.Count);
    }

    [Fact]
    public void Remove_Unknown_ReturnsFalse()
    {
        var svc = new AiHistoryService();
        Assert.False(svc.Remove("غير موجود"));
    }

    // ---- Constructor seeding ----

    [Fact]
    public void Constructor_SeedsFromStoredEntries_AndPrunes()
    {
        var seed = Enumerable.Range(0, AiHistoryService.MaxEntries + 5)
            .Select(i => new AiHistoryEntry($"س{i}", 1, At(i)))
            .ToList();

        var svc = new AiHistoryService(seed);

        Assert.True(svc.Count <= AiHistoryService.MaxEntries);
    }

    // ---- KeyOf / Clean ----

    [Fact]
    public void KeyOf_NormalizesArabicDigitsAndHamza()
    {
        Assert.Equal(AiHistoryService.KeyOf("افضل 5"), AiHistoryService.KeyOf("أفضل ٥"));
    }

    [Fact]
    public void Clean_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, AiHistoryService.Clean(null));
        Assert.Equal(string.Empty, AiHistoryService.Clean("   "));
    }

    // ---- QuickQuestionSuggestions ----

    [Fact]
    public void Suggestions_NoHistory_ReturnsContextDefaults()
    {
        var list = QuickQuestionSuggestions.Build(
            QuickQuestionSuggestions.Context.Debt, history: null, max: 4);

        Assert.Equal(4, list.Count);
        Assert.Contains("قائمة الديون", list);
    }

    [Fact]
    public void Suggestions_HistoryFirst_ThenContextWithoutDuplicates()
    {
        var svc = new AiHistoryService();
        svc.Record("سؤالي المتكرر", At(1));
        svc.Record("سؤالي المتكرر", At(2));

        var list = QuickQuestionSuggestions.Build(
            QuickQuestionSuggestions.Context.Debt, svc, max: 4);

        Assert.Equal("سؤالي المتكرر", list[0]);
        // لا تكرار
        Assert.Equal(list.Count, list.Distinct().Count());
        // ضمن الحد
        Assert.True(list.Count <= 4);
    }

    [Fact]
    public void Suggestions_RespectsMax()
    {
        var list = QuickQuestionSuggestions.Build(
            QuickQuestionSuggestions.Context.General, null, max: 2);

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public void Suggestions_MaxZero_ReturnsEmpty()
    {
        Assert.Empty(QuickQuestionSuggestions.Build(
            QuickQuestionSuggestions.Context.General, null, max: 0));
    }

    [Theory]
    [InlineData("/sales", QuickQuestionSuggestions.Context.Sales)]
    [InlineData("/products", QuickQuestionSuggestions.Context.Products)]
    [InlineData("/reports", QuickQuestionSuggestions.Context.Reports)]
    [InlineData("/settings", QuickQuestionSuggestions.Context.Settings)]
    [InlineData("/ai", QuickQuestionSuggestions.Context.General)]
    [InlineData(null, QuickQuestionSuggestions.Context.General)]
    public void ContextFromRoute_MapsKnownRoutes(string? route, QuickQuestionSuggestions.Context expected)
    {
        Assert.Equal(expected, QuickQuestionSuggestions.ContextFromRoute(route));
    }
}
