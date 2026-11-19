using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية للمطابقة التقريبية المقيدة داخل المحكّم:
/// سبب القبول التقريبي صادر عن المحكّم، ولا كتابة ولا تغيير في ثوابت السعر.
/// </summary>
public sealed class ApproximateNameAdjudicationTests
{
    [Fact]
    public void Adjudicate_AcceptsKnownDescriptorWithItsOwnReason()
    {
        var candidate = new PerceptionCandidate("حليب معقم", 12.5m, "حليب معقم 12.5", 1, PerceptionSource.Embedded);
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };

        var result = PerceptionAdjudicator.Adjudicate(new[] { candidate }, known);

        var verdict = Assert.Single(result.Candidates);
        Assert.True(verdict.Accepted);
        Assert.Equal(PerceptionAdjudicator.ApproximateNameAcceptedReason, verdict.Reason);
        Assert.Equal(1, result.AcceptedCount);
        Assert.False(result.WrittenToDatabase);
    }

    [Fact]
    public void Adjudicate_AcceptsReorderedNameTokens()
    {
        var candidate = new PerceptionCandidate("مطحونة قهوة", 250m, "مطحونة قهوة 250", 1, PerceptionSource.Embedded);
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["قهوة"] = new[] { 250m } };

        var result = PerceptionAdjudicator.Adjudicate(new[] { candidate }, known);

        Assert.True(Assert.Single(result.Candidates).Accepted);
    }

    [Fact]
    public void Adjudicate_RejectsAmbiguousNameInsteadOfGuessing()
    {
        var candidate = new PerceptionCandidate("حليب", 12.5m, "حليب 12.5", 1, PerceptionSource.Embedded);
        var known = new Dictionary<string, IReadOnlyList<decimal>>
        {
            ["حليب معقم"] = new[] { 12.5m },
            ["حليب طازج"] = new[] { 12.5m }
        };

        var result = PerceptionAdjudicator.Adjudicate(new[] { candidate }, known);

        Assert.False(Assert.Single(result.Candidates).Accepted);
        Assert.Equal(PerceptionAdjudicator.UnknownProductReason, result.Candidates[0].Reason);
    }

    [Fact]
    public void Adjudicate_ApproximateNameWithWrongPrice_KeepsPriceMismatchReason()
    {
        var candidate = new PerceptionCandidate("حليب معقم", 99m, "حليب معقم 99", 1, PerceptionSource.Embedded);
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };

        var result = PerceptionAdjudicator.Adjudicate(new[] { candidate }, known);

        Assert.False(Assert.Single(result.Candidates).Accepted);
        Assert.Equal(PerceptionAdjudicator.PriceMismatchReason, result.Candidates[0].Reason);
    }

    [Fact]
    public void Adjudicate_DoesNotApproveUnlistedDescriptor()
    {
        var candidate = new PerceptionCandidate("حليب مملح", 12.5m, "حليب مملح 12.5", 1, PerceptionSource.Embedded);
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };

        var result = PerceptionAdjudicator.Adjudicate(new[] { candidate }, known);

        Assert.False(Assert.Single(result.Candidates).Accepted);
        Assert.Equal(PerceptionAdjudicator.UnknownProductReason, result.Candidates[0].Reason);
    }

    [Fact]
    public void Adjudicate_ExactMatchKeepsOriginalReason()
    {
        var candidate = new PerceptionCandidate("حليب", 12.5m, "حليب 12.5", 1, PerceptionSource.Embedded);
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };

        var result = PerceptionAdjudicator.Adjudicate(new[] { candidate }, known);

        Assert.Equal(PerceptionAdjudicator.AcceptedReason, Assert.Single(result.Candidates).Reason);
    }

    [Fact]
    public void DecisionReport_ShowsApproximateReasonVerbatim_AsRegisteredAndKnown()
    {
        var candidate = new PerceptionCandidate("حليب معقم", 12.5m, "حليب معقم 12.5", 1, PerceptionSource.Embedded);
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };
        var adjudication = PerceptionAdjudicator.Adjudicate(new[] { candidate }, known);

        var report = DecisionReportBuilder.Build("حليب معقم 12.5", adjudication);

        var good = Assert.Single(report.Goods);
        Assert.Equal(PerceptionAdjudicator.ApproximateNameAcceptedReason, good.Verdict);
        Assert.True(good.Accepted);
        Assert.Equal(GoodsDecisionCategory.Registered, good.Category);
        Assert.Equal(StoreNameKnowledge.Known, good.StoreKnowledge);
        Assert.Equal(adjudication.AcceptedCount, report.AcceptedCount);
        Assert.False(report.WrittenToDatabase);
    }
}
