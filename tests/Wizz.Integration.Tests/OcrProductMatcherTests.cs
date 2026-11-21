using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية لمطابقة الاسم المقروء بمنتج حقيقي: تطابق تام فقط، بلا تخمين.
/// </summary>
public sealed class OcrProductMatcherTests
{
    private sealed record FakeProduct(string Name);

    private static readonly FakeProduct[] Products =
    {
        new("حليب كامل الدسم"),
        new("خبز"),
        new("ماء معدني"),
    };

    [Fact]
    public void Match_ExactName_ReturnsProduct()
    {
        var product = OcrProductMatcher.Match("خبز", Products, p => p.Name);

        Assert.NotNull(product);
        Assert.Equal("خبز", product!.Name);
    }

    [Fact]
    public void Match_NormalizesArabicVariants()
    {
        var product = OcrProductMatcher.Match("حليب كامل الدسم", Products, p => p.Name);
        var withHamza = OcrProductMatcher.Match("حليب كامل الدّسم", Products, p => p.Name);

        Assert.Equal("حليب كامل الدسم", product?.Name);
        Assert.Equal("حليب كامل الدسم", withHamza?.Name);
    }

    [Fact]
    public void Match_UnknownName_ReturnsNull()
    {
        Assert.Null(OcrProductMatcher.Match("صنف غير موجود", Products, p => p.Name));
    }

    [Fact]
    public void Match_AmbiguousName_ReturnsNull()
    {
        var ambiguous = new[] { new FakeProduct("خبز"), new FakeProduct("خبز") };

        Assert.Null(OcrProductMatcher.Match("خبز", ambiguous, p => p.Name));
    }

    [Fact]
    public void Match_EmptyOrWhitespace_ReturnsNull()
    {
        Assert.Null(OcrProductMatcher.Match(null, Products, p => p.Name));
        Assert.Null(OcrProductMatcher.Match("  ", Products, p => p.Name));
    }
}
