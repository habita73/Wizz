using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية: الرموز التي تشبه الأرقام لا تُعدّ أسماء منتجات،
/// والنص الخام نفسه يبقى كما قرأه التعرّف في السطر المصدر.
/// </summary>
public sealed class GarbledTokenExtractionTests
{
    [Fact]
    public void Extract_DropsDigitLikeTokenFromProductName_ButKeepsRawLine()
    {
        var candidates = PerceptionCandidateExtractor.Extract("i.ooo احمر تفاح 450.00");

        var candidate = Assert.Single(candidates);
        Assert.Equal("احمر تفاح", candidate.ProductName);
        Assert.Equal(450m, candidate.Price);
        Assert.Equal("i.ooo احمر تفاح 450.00", candidate.RawLine);
    }

    [Fact]
    public void Extract_DenseReceiptRow_DoesNotNameGarbledTokensAsProducts()
    {
        var candidates = PerceptionCandidateExtractor.Extract("ماء ارز oo 180.00 1.000 كغ ll80");

        Assert.NotEmpty(candidates);
        Assert.All(candidates, candidate =>
        {
            Assert.DoesNotContain("oo", candidate.ProductName ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain("ll", candidate.ProductName ?? string.Empty, StringComparison.Ordinal);
        });
        Assert.Contains(candidates, candidate => candidate.ProductName == "ماء ارز" && candidate.Price == 180m);
        Assert.Contains(candidates, candidate => candidate.ProductName == "ارز" && candidate.Price == 180m);
    }

    [Fact]
    public void Extract_LineMadeOnlyOfDigitLikeSymbols_ProducesNoCandidate()
    {
        var candidates = PerceptionCandidateExtractor.Extract("iiiiiiiiiiiiiiiiiiii");

        Assert.Empty(candidates);
    }

    [Fact]
    public void Extract_KeepsRealArabicNamesThatContainNoDigits()
    {
        var candidates = PerceptionCandidateExtractor.Extract("حليب معقم 240.00");

        var candidate = Assert.Single(candidates);
        Assert.Equal("حليب معقم", candidate.ProductName);
    }
}
