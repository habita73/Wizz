using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>اختبارات ببيانات اصطناعية لقراءة الكمية وتوحيد الأسطر والاقتراحات القريبة.</summary>
public sealed class OcrSmarterExtractionTests
{
    [Fact]
    public void Quantity_ArabicDigit_IsRead()
    {
        Assert.Equal(2m, OcrQuantityParser.Parse("أرز ٢", "أرز", null));
    }

    [Fact]
    public void Quantity_PriceIsIgnored()
    {
        Assert.Equal(2m, OcrQuantityParser.Parse("أرز 2 12.5", "أرز", 12.5m));
    }

    [Fact]
    public void Quantity_OnlyPrice_DefaultsToOne()
    {
        Assert.Equal(1m, OcrQuantityParser.Parse("أرز 12.5", "أرز", 12.5m));
    }

    [Fact]
    public void Quantity_AmbiguousNumbers_DefaultsToOne()
    {
        Assert.Equal(1m, OcrQuantityParser.Parse("أرز 2 3 12.5", "أرز", 12.5m));
    }

    [Fact]
    public void Quantity_OutOfRange_DefaultsToOne()
    {
        Assert.Equal(1m, OcrQuantityParser.Parse("أرز 9999", "أرز", null));
    }

    [Fact]
    public void Merge_KeepsDifferentNamesSeparate()
    {
        var lines = new[]
        {
            new OcrDraftLine("أرز", 12.5m, 2m),
            new OcrDraftLine("الأرز", 12.5m, 3m),
            new OcrDraftLine("خبز", 5m, 1m),
        };

        var merged = OcrDraftLineMerger.Merge(lines);

        // «أرز» و«الأرز» مختلفان بعد التطبيع؛ الأول يُطابق تامًا والثاني يُعرض كاقتراح قريب.
        Assert.Equal(3, merged.Count);
        Assert.Equal(2m, merged[0].Quantity);
        Assert.Equal(3m, merged[1].Quantity);
        Assert.Equal(1m, merged[2].Quantity);
    }

    [Fact]
    public void Merge_ExactDuplicates_SumsQuantities()
    {
        var lines = new[]
        {
            new OcrDraftLine("أرز", 12.5m, 2m),
            new OcrDraftLine("أرز", 12.5m, 3m),
        };

        var merged = OcrDraftLineMerger.Merge(lines);

        Assert.Single(merged);
        Assert.Equal(5m, merged[0].Quantity);
    }

    [Fact]
    public void Suggest_NearName_IsOfferedForReview()
    {
        var products = new[] { "أرز", "خبز" };

        var suggestions = OcrProductMatcher.Suggest("الارز", products, p => p);

        Assert.Single(suggestions);
        Assert.Equal("أرز", suggestions[0]);
    }

    [Fact]
    public void Suggest_ExactName_IsNotOffered()
    {
        var products = new[] { "أرز", "خبز" };

        var suggestions = OcrProductMatcher.Suggest("أرز", products, p => p);

        Assert.DoesNotContain("أرز", suggestions);
    }
}
