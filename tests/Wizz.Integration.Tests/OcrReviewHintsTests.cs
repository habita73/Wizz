using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية لملاحظات المراجعة: كلمة رقمية وسطر شعار محتمل،
/// مع إثبات أنها لا تغيّر الحكم ولا النص ولا حالة الكتابة.
/// </summary>
public sealed class OcrReviewHintsTests
{
    [Fact]
    public void Analyze_DetectsNumberWordAndLogoLikeLine()
    {
        var content = new HashSet<int> { 0 };

        var hints = OcrReviewHints.Analyze("عشرة حليب 12.5\nWizz store", content);

        Assert.Equal(2, hints.Count);
        Assert.Equal(ReviewHintKind.NumberWord, hints[0].Kind);
        Assert.Equal("عشرة", hints[0].Token);
        Assert.Equal(1, hints[0].LineNumber);
        Assert.Equal(ReviewHintKind.LogoLike, hints[1].Kind);
        Assert.Equal(2, hints[1].LineNumber);
    }

    [Fact]
    public void Analyze_NormalProductLines_ProduceNoHints()
    {
        var content = new HashSet<int> { 0, 1 };

        var hints = OcrReviewHints.Analyze("حليب 12.5\nزيت 5", content);

        Assert.Empty(hints);
    }

    [Fact]
    public void Analyze_NullOrBlank_ReturnsEmpty()
    {
        Assert.Empty(OcrReviewHints.Analyze(null, new HashSet<int>()));
        Assert.Empty(OcrReviewHints.Analyze("  \n  ", new HashSet<int>()));
    }

    [Fact]
    public void Analyze_CapsHintCount()
    {
        var text = string.Join('\n', Enumerable.Repeat("عشرة", 15));

        var hints = OcrReviewHints.Analyze(text, new HashSet<int>());

        Assert.Equal(12, hints.Count);
    }

    [Fact]
    public void Note_StatesReviewOnlyLimits()
    {
        Assert.Contains("للمراجعة", OcrReviewHints.Note);
        Assert.Contains("لا تُغيّر", OcrReviewHints.Note);
        Assert.Contains("لا تُكتب", OcrReviewHints.Note);
    }

    [Fact]
    public void DecisionReport_CarriesReviewHintsWithoutChangingVerdict()
    {
        var candidates = new[]
        {
            new PerceptionCandidate("حليب", 12.5m, "حليب 12.5", 1.0, PerceptionSource.Embedded)
        };
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };
        var adjudication = PerceptionAdjudicator.Adjudicate(candidates, known);

        var report = DecisionReportBuilder.Build("حليب 12.5\nعشرة\nWizz store", adjudication);

        Assert.NotEmpty(report.ReviewHints);
        Assert.Contains(report.ReviewHints, hint => hint.Kind == ReviewHintKind.NumberWord);
        Assert.Contains(report.ReviewHints, hint => hint.Kind == ReviewHintKind.LogoLike);
        Assert.Equal(adjudication.AcceptedCount, report.AcceptedCount);
        Assert.Equal(adjudication.RejectedCount, report.RejectedCount);
        Assert.False(report.WrittenToDatabase);
        Assert.Contains(report.PartLines, line => line.Text == "عشرة");
        Assert.Contains(report.PartLines, line => line.Text == "Wizz store");
    }
}
