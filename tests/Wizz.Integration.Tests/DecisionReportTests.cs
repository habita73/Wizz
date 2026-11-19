using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات تقرير القرار ببيانات اصطناعية فقط: التقرير ناقل لأحكام المحكّم،
/// لا يُنشئ حكمًا ولا يكتب ولا يُبالغ في دقة أي معلومة.
/// </summary>
public sealed class DecisionReportTests
{
    [Fact]
    public void DecisionReport_MapsAdjudicatorReasonsToThreeCategories()
    {
        var candidates = new[]
        {
            new PerceptionCandidate("حليب", 12.5m, "حليب 12.5", 1, PerceptionSource.Embedded),
            new PerceptionCandidate("مجهول", 5m, "مجهول 5", 1, PerceptionSource.Embedded),
            new PerceptionCandidate("زيت", 130m, "زيت 130", 1, PerceptionSource.Embedded)
        };
        var known = new Dictionary<string, IReadOnlyList<decimal>>
        {
            ["حليب"] = new[] { 12.5m },
            ["زيت"] = new[] { 140m }
        };
        var adjudication = PerceptionAdjudicator.Adjudicate(candidates, known);

        var report = DecisionReportBuilder.Build("حليب 12.5\nمجهول 5\nزيت 130", adjudication);

        Assert.Equal(3, report.Goods.Count);
        Assert.Equal(GoodsDecisionCategory.Registered, report.Goods[0].Category);
        Assert.Equal(GoodsDecisionCategory.Unknown, report.Goods[1].Category);
        Assert.Equal(GoodsDecisionCategory.Conflicting, report.Goods[2].Category);

        // سبب المحكّم يُنقل حرفيًا، ولا يُعاد صياغته داخل التقرير.
        Assert.Equal(PerceptionAdjudicator.AcceptedReason, report.Goods[0].Verdict);
        Assert.Equal(PerceptionAdjudicator.UnknownProductReason, report.Goods[1].Verdict);
        Assert.Equal(PerceptionAdjudicator.PriceMismatchReason, report.Goods[2].Verdict);

        Assert.Equal(1, report.RegisteredCount);
        Assert.Equal(1, report.UnknownCount);
        Assert.Equal(1, report.ConflictingCount);
        Assert.Equal(adjudication.AcceptedCount, report.AcceptedCount);
        Assert.Equal(adjudication.RejectedCount, report.RejectedCount);
        Assert.False(report.WrittenToDatabase);
    }

    [Fact]
    public void DecisionReport_MissingPriceIsConflicting_AndKeepsExactReason()
    {
        var candidates = new[]
        {
            new PerceptionCandidate("حليب", null, "حليب", 0.5, PerceptionSource.Embedded)
        };
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };
        var adjudication = PerceptionAdjudicator.Adjudicate(candidates, known);

        var report = DecisionReportBuilder.Build("حليب", adjudication);

        Assert.Equal(GoodsDecisionCategory.Conflicting, report.Goods[0].Category);
        Assert.Equal(PerceptionAdjudicator.MissingPriceReason, report.Goods[0].Verdict);
        Assert.Equal(DecisionReportBuilder.MissingValue, report.Goods[0].Price);
    }

    [Fact]
    public void DecisionReport_SplitsHeaderContentFooterByPosition()
    {
        // أسطر الرأس تُنتج مرشّحات أيضًا؛ يجب ألّا تُعدّ «محتوى» لمجرد وجود مرشّح لها.
        var candidates = new[]
        {
            new PerceptionCandidate("متجر تجريبي", null, "متجر تجريبي", 0.5, PerceptionSource.Embedded),
            new PerceptionCandidate("الهاتف", 55m, "الهاتف 0555", 0.5, PerceptionSource.Embedded),
            new PerceptionCandidate("حليب", 12.5m, "حليب 12.5", 1, PerceptionSource.Embedded)
        };
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };
        var adjudication = PerceptionAdjudicator.Adjudicate(candidates, known);

        var report = DecisionReportBuilder.Build(
            "متجر تجريبي\nالهاتف 0555\nحليب 12.5\nمجموع 25\nشكرا",
            adjudication);

        Assert.Equal(EstimatedDocumentPart.Header, report.PartLines[0].Part);
        Assert.Equal(EstimatedDocumentPart.Header, report.PartLines[1].Part);
        Assert.Equal(EstimatedDocumentPart.Content, report.PartLines[2].Part);
        Assert.Equal(EstimatedDocumentPart.Footer, report.PartLines[3].Part);
        Assert.Equal(EstimatedDocumentPart.Footer, report.PartLines[4].Part);
    }

    [Fact]
    public void DecisionReport_DetectsTypeFromHeader_AndStaysUnspecifiedWhenCluesConflict()
    {
        var candidates = new[]
        {
            new PerceptionCandidate("حليب", 12.5m, "حليب 12.5", 1, PerceptionSource.Embedded)
        };
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };
        var adjudication = PerceptionAdjudicator.Adjudicate(candidates, known);

        var invoice = DecisionReportBuilder.Build("متجر تجريبي\nرقم الفاتورة 12\nحليب 12.5", adjudication);
        Assert.Equal(EstimatedDocumentType.Invoice, invoice.DocumentType);
        Assert.Equal("فاتورة", DecisionReportBuilder.TypeLabel(invoice.DocumentType));

        var conflict = DecisionReportBuilder.Build("وصل بيع\nفاتورة\nحليب 12.5", adjudication);
        Assert.Equal(EstimatedDocumentType.Unspecified, conflict.DocumentType);
        Assert.Equal("غير محدد", DecisionReportBuilder.TypeLabel(conflict.DocumentType));
    }

    [Fact]
    public void DecisionReport_NoContentLines_AllPartsUnclassified()
    {
        var adjudication = PerceptionAdjudicator.Adjudicate(
            Array.Empty<PerceptionCandidate>(),
            new Dictionary<string, IReadOnlyList<decimal>>());

        var report = DecisionReportBuilder.Build("سطر بلا مرشّح\nآخر", adjudication);

        Assert.All(report.PartLines, line => Assert.Equal(EstimatedDocumentPart.Unclassified, line.Part));
        Assert.Empty(report.Goods);
        Assert.Equal(0, report.AcceptedCount);
        Assert.False(report.WrittenToDatabase);
    }
}
