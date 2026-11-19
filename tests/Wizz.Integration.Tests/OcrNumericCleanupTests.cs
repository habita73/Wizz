using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات احتمالات تصحيح الأرقام ببيانات اصطناعية فقط: قرينة للمراجعة،
/// لا تُغيّر النص المقروء ولا تُصدر حكمًا ولا تلمس أسماء المنتجات.
/// </summary>
public sealed class OcrNumericCleanupTests
{
    [Fact]
    public void Analyze_SuggestsCommonConfusionsInsideNumericTokensOnly()
    {
        var analysis = OcrNumericCleanup.Analyze("حليب I.OOO\nزيت OO\nماء ll");

        Assert.True(analysis.HasHints);
        Assert.Equal(3, analysis.Hints.Count);
        Assert.Equal(new[] { 1, 2, 3 }, analysis.Hints.Select(hint => hint.LineNumber));

        Assert.Equal("I.OOO", analysis.Hints[0].Original);
        Assert.Equal("1.000", analysis.Hints[0].Suggested);
        Assert.Equal("حليب I.OOO", analysis.Hints[0].Line);

        Assert.Equal("OO", analysis.Hints[1].Original);
        Assert.Equal("00", analysis.Hints[1].Suggested);

        Assert.Equal("ll", analysis.Hints[2].Original);
        Assert.Equal("11", analysis.Hints[2].Suggested);
    }

    [Fact]
    public void Analyze_LeavesCleanNumbersAndProductNamesUntouched()
    {
        var analysis = OcrNumericCleanup.Analyze("حليب 12.5\nماء 1.000\nزيت 0555");

        Assert.False(analysis.HasHints);
        Assert.Empty(analysis.Hints);
    }

    [Fact]
    public void Analyze_RejectsTokensContainingArabicOrOtherCharacters()
    {
        var analysis = OcrNumericCleanup.Analyze("حليب12.5\nOO%\n12:30");

        Assert.Empty(analysis.Hints);
    }

    [Fact]
    public void Analyze_HandlesNullAndWhitespace()
    {
        Assert.Empty(OcrNumericCleanup.Analyze(null).Hints);
        Assert.Empty(OcrNumericCleanup.Analyze("   \n  ").Hints);
    }

    [Fact]
    public void Note_StatesThatOriginalIsUnchangedAndNoVerdictIsAdded()
    {
        Assert.Contains("للمراجعة", OcrNumericCleanup.Note);
        Assert.Contains("لم يُغيَّر", OcrNumericCleanup.Note);
        Assert.Contains("لم يدخل", OcrNumericCleanup.Note);
    }
}
