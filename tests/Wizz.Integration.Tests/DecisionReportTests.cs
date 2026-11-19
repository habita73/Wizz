using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات تقرير القرار ببيانات اصطناعية فقط: التقرير ناقل لأحكام المحكّم،
/// يُعيد تركيب الأسطر ويفصل الأصناف عن بيانات الوثيقة دون اختراع حكم أو زيادة دقة.
/// </summary>
public sealed class DecisionReportTests
{
    [Fact]
    public void DecisionReport_MapsReasonsToCategoriesAndStoreKnowledge()
    {
        var candidates = new[]
        {
            new PerceptionCandidate("حليب", 12.5m, "حليب 12.5", 1, PerceptionSource.Embedded),
            new PerceptionCandidate("مجهول", 5.5m, "مجهول 5.5", 1, PerceptionSource.Embedded),
            new PerceptionCandidate("زيت", 130.5m, "زيت 130.5", 1, PerceptionSource.Embedded)
        };
        var known = new Dictionary<string, IReadOnlyList<decimal>>
        {
            ["حليب"] = new[] { 12.5m },
            ["زيت"] = new[] { 140m }
        };
        var adjudication = PerceptionAdjudicator.Adjudicate(candidates, known);

        var report = DecisionReportBuilder.Build("حليب 12.5\nمجهول 5.5\nزيت 130.5", adjudication);

        Assert.Equal(3, report.Goods.Count);
        Assert.Equal(GoodsDecisionCategory.Registered, report.Goods[0].Category);
        Assert.Equal(GoodsDecisionCategory.Unknown, report.Goods[1].Category);
        Assert.Equal(GoodsDecisionCategory.Conflicting, report.Goods[2].Category);

        // «معروف في السجل» مشتق من نص السبب: قبول أو اختلاف سعر كلاهما يفيد بمعرفة الاسم.
        Assert.Equal(StoreNameKnowledge.Known, report.Goods[0].StoreKnowledge);
        Assert.Equal(StoreNameKnowledge.Unknown, report.Goods[1].StoreKnowledge);
        Assert.Equal(StoreNameKnowledge.Known, report.Goods[2].StoreKnowledge);

        // سبب المحكّم يُنقل حرفيًا، ولا يُعاد صياغته داخل التقرير.
        Assert.Equal(PerceptionAdjudicator.AcceptedReason, report.Goods[0].Verdict);
        Assert.Equal(PerceptionAdjudicator.UnknownProductReason, report.Goods[1].Verdict);
        Assert.Equal(PerceptionAdjudicator.PriceMismatchReason, report.Goods[2].Verdict);

        Assert.Equal(1, report.RegisteredCount);
        Assert.Equal(1, report.UnknownCount);
        Assert.Equal(1, report.ConflictingCount);
        Assert.Equal(adjudication.AcceptedCount, report.AcceptedCount);
        Assert.Equal(adjudication.RejectedCount, report.RejectedCount);
        Assert.Equal(3, report.CandidateTotal);
        Assert.Equal(0, report.MetadataCandidateCount);
        Assert.False(report.WrittenToDatabase);
    }

    [Fact]
    public void DecisionReport_MissingPriceIsConflicting_AndLeavesKnowledgeUndetermined()
    {
        var candidates = new[]
        {
            new PerceptionCandidate("حليب", null, "حليب 12.5", 0.5, PerceptionSource.Embedded)
        };
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };
        var adjudication = PerceptionAdjudicator.Adjudicate(candidates, known);

        var report = DecisionReportBuilder.Build("حليب 12.5", adjudication);

        Assert.Equal(GoodsDecisionCategory.Conflicting, report.Goods[0].Category);
        Assert.Equal(PerceptionAdjudicator.MissingPriceReason, report.Goods[0].Verdict);
        Assert.Equal(StoreNameKnowledge.NotDetermined, report.Goods[0].StoreKnowledge);
        Assert.Equal(DecisionReportBuilder.MissingValue, report.Goods[0].Price);
        Assert.Equal("يحتاج مراجعة", DecisionReportBuilder.KnowledgeLabel(report.Goods[0].StoreKnowledge));
    }

    [Fact]
    public void DecisionReport_ExcludesHeaderAndFooterCandidatesFromGoods_ButKeepsAdjudicatorCounts()
    {
        // أسطر الرأس تُنتج مرشّحات أيضًا؛ يجب ألّا تُعدّ «أصنافًا» لمجرد وجود مرشّح لها،
        // وأحكامها تظل محسوبة في العدد الكلي الذي صدر عن المدمج.
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

        // صنف واحد فقط من صف المحتوى، والمرشّحان الآخران بيانات وثيقة لا أصناف.
        Assert.Single(report.Goods);
        Assert.Equal("حليب", report.Goods[0].Name);
        Assert.Equal(3, report.CandidateTotal);
        Assert.Equal(2, report.MetadataCandidateCount);
        Assert.Equal(adjudication.AcceptedCount, report.AcceptedCount);
        Assert.Equal(adjudication.RejectedCount, report.RejectedCount);
    }

    [Fact]
    public void DecisionReport_MergesDuplicateCandidatesFromSameReconstructedLine()
    {
        var candidates = new[]
        {
            new PerceptionCandidate("ماء", 180m, "ماء 1.000", 0.8, PerceptionSource.Embedded),
            new PerceptionCandidate("ماء", 180m, "ماء 1.000", 0.8, PerceptionSource.Embedded),
            new PerceptionCandidate("ارز", 180m, "ماء 1.000", 0.8, PerceptionSource.Embedded)
        };
        var adjudication = PerceptionAdjudicator.Adjudicate(
            candidates, new Dictionary<string, IReadOnlyList<decimal>>());

        var report = DecisionReportBuilder.Build("ماء 1.000", adjudication);

        Assert.Equal(2, report.Goods.Count);
        Assert.All(report.Goods, item => Assert.Equal("ماء 1.000", item.SourceLine));
        Assert.Equal(2, report.Goods.Single(item => item.Name == "ماء").CandidateCount);
        Assert.Equal(1, report.Goods.Single(item => item.Name == "ارز").CandidateCount);
        Assert.Equal(3, report.CandidateTotal);
        Assert.Equal(0, report.MetadataCandidateCount);
        Assert.Equal(3, report.RejectedCount);
    }

    [Fact]
    public void DecisionReport_GroupsGoodsByReconstructedSourceLine()
    {
        var candidates = new[]
        {
            new PerceptionCandidate("حليب", 12.5m, "حليب 12.5", 1, PerceptionSource.Embedded),
            new PerceptionCandidate("زيت", 140.5m, "زيت 140.5", 1, PerceptionSource.Embedded)
        };
        var adjudication = PerceptionAdjudicator.Adjudicate(
            candidates, new Dictionary<string, IReadOnlyList<decimal>>());

        var report = DecisionReportBuilder.Build("حليب 12.5\nزيت 140.5", adjudication);

        var groups = report.Goods.GroupBy(item => item.SourceLine).ToList();
        Assert.Equal(2, groups.Count);
        Assert.Contains(groups, group => group.Key == "حليب 12.5" && group.Single().Name == "حليب");
        Assert.Contains(groups, group => group.Key == "زيت 140.5" && group.Single().Name == "زيت");
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
        Assert.Equal(0, report.CandidateTotal);
        Assert.False(report.WrittenToDatabase);
    }

    [Fact]
    public void DecisionReport_ExcludesTotalsAndTaxLinesFromGoods()
    {
        var candidates = new[]
        {
            new PerceptionCandidate("حليب", 12.5m, "حليب 12.5", 1, PerceptionSource.Embedded),
            new PerceptionCandidate("مجموع", 201.59m, "المواد: 1061.0 مجموع", 0.8, PerceptionSource.Embedded),
            new PerceptionCandidate("ضريبه tva", 19m, "ضريبة 201.59 :%19 tva", 0.8, PerceptionSource.Embedded)
        };
        var adjudication = PerceptionAdjudicator.Adjudicate(
            candidates, new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } });

        var report = DecisionReportBuilder.Build(
            "حليب 12.5\nالمواد: 1061.0 مجموع\nضريبة 201.59 :%19 tva",
            adjudication);

        Assert.Single(report.Goods);
        Assert.Equal("حليب", report.Goods[0].Name);
        Assert.Equal(EstimatedDocumentPart.Footer, report.PartLines[1].Part);
        Assert.Equal(EstimatedDocumentPart.Footer, report.PartLines[2].Part);
        // أحكام المدمج على المرشّحات المستبعدة من العرض باقية في العدد الكلي.
        Assert.Equal(3, report.CandidateTotal);
        Assert.Equal(adjudication.RejectedCount, report.RejectedCount);
    }

    [Fact]
    public void DecisionReport_SuggestsNearKnownNamesWithoutChangingVerdict()
    {
        var candidates = new[]
        {
            new PerceptionCandidate("حليب", 12.5m, "حليب 12.5", 1, PerceptionSource.Embedded),
            new PerceptionCandidate("حليب معقم", 9.5m, "حليب معقم 9.5", 1, PerceptionSource.Embedded)
        };
        var known = new Dictionary<string, IReadOnlyList<decimal>> { ["حليب"] = new[] { 12.5m } };
        var adjudication = PerceptionAdjudicator.Adjudicate(candidates, known);

        var report = DecisionReportBuilder.Build("حليب 12.5\nحليب معقم 9.5", adjudication);

        var near = report.Goods.Single(item => item.Name == "حليب معقم");
        Assert.Equal(PerceptionAdjudicator.UnknownProductReason, near.Verdict);
        Assert.False(near.Accepted);
        Assert.Contains("حليب", near.NearKnownNames);

        // التلميح لم يضف قبولًا ولا غيّر عدد أحكام المدمج.
        Assert.Equal(adjudication.AcceptedCount, report.AcceptedCount);
        Assert.Equal(adjudication.RejectedCount, report.RejectedCount);
        Assert.Equal(1, report.UnknownCount);
    }

    [Fact]
    public void DecisionReport_StatesLimitsOfAvailabilityAndNameMatching()
    {
        Assert.Contains("لا يعني توفر كمية في المخزون", DecisionReportBuilder.StockNote);
        Assert.Contains("المطابقة الحالية للأسماء حرفية", DecisionReportBuilder.NameMatchingNote);
        Assert.Contains("تلميح للمراجعة فقط", DecisionReportBuilder.NearNameNote);
        Assert.Equal("معروف في بيانات المتجر", DecisionReportBuilder.KnowledgeLabel(StoreNameKnowledge.Known));
        Assert.Equal("غير معروف في بيانات المتجر", DecisionReportBuilder.KnowledgeLabel(StoreNameKnowledge.Unknown));
    }
}
