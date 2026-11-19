using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية للعرض الأمين للوثيقة: كل سطر يُنقل حرفيًا وبترتيبه،
/// والصفوف ذات المظهر الجدولي تُجمَّع في جدول نصي بلا تصحيح أو حذف.
/// </summary>
public sealed class FaithfulDocumentViewTests
{
    [Fact]
    public void Build_KeepsEveryLineVerbatimInOrder()
    {
        var lines = new[]
        {
            new DecisionReportPartLine("الخير حت", EstimatedDocumentPart.Header),
            new DecisionReportPartLine("حي 08 ماي 1945", EstimatedDocumentPart.Header),
            new DecisionReportPartLine("شكرا لتسوقكم", EstimatedDocumentPart.Footer)
        };

        var blocks = FaithfulDocumentView.Build(lines);

        Assert.Equal(3, blocks.Count);
        Assert.All(blocks, block => Assert.Equal(FaithfulBlockKind.Line, block.Kind));
        Assert.Equal(new[] { "الخير حت", "حي 08 ماي 1945", "شكرا لتسوقكم" },
            blocks.Select(block => block.Lines[0]));
    }

    [Fact]
    public void Build_ReturnsEmptyForNoLines()
    {
        Assert.Empty(FaithfulDocumentView.Build(Array.Empty<DecisionReportPartLine>()));
    }

    [Fact]
    public void LooksLikeTableRow_RequiresThreeTokensAndTwoDecimals()
    {
        Assert.True(FaithfulDocumentView.LooksLikeTableRow("تفاح أحمر 240.00 كغ 450.00 450.00"));
        Assert.True(FaithfulDocumentView.LooksLikeTableRow("ماء أرز 1.000 كغ 180.00 140.00 2 )"));
        Assert.True(FaithfulDocumentView.LooksLikeTableRow("صنف 12,5 40,00"));
    }

    [Fact]
    public void LooksLikeTableRow_RejectsPhoneNumbersAndSingleDecimals()
    {
        Assert.False(FaithfulDocumentView.LooksLikeTableRow("الهاتف : 89 67 45 023"));
        Assert.False(FaithfulDocumentView.LooksLikeTableRow("المبلغ المدفوع : 140.00 2 دج"));
        Assert.False(FaithfulDocumentView.LooksLikeTableRow("100.00 -"));
        Assert.False(FaithfulDocumentView.LooksLikeTableRow(string.Empty));
        Assert.False(FaithfulDocumentView.LooksLikeTableRow("   "));
    }

    [Fact]
    public void SplitCells_PreservesTokensExactly()
    {
        var line = "ماء أرز 1.000 كغ 180.00 140.00 2 )";

        var cells = FaithfulDocumentView.SplitCells(line);

        Assert.Equal(new[] { "ماء", "أرز", "1.000", "كغ", "180.00", "140.00", "2", ")" }, cells);
        Assert.Equal(line, string.Join(' ', cells));
    }

    [Fact]
    public void Build_GroupsConsecutiveTableRowsOfSamePart()
    {
        var lines = new[]
        {
            new DecisionReportPartLine("اللنتوج الكمية سعر الوحدة المجموع", EstimatedDocumentPart.Header),
            new DecisionReportPartLine("تفاح أحمر 240.00 كغ 450.00 450.00", EstimatedDocumentPart.Content),
            new DecisionReportPartLine("حليب معقم 2.000 حبة 120.00 240.00", EstimatedDocumentPart.Content),
            new DecisionReportPartLine("المجموع الجزئي : 240.00 2", EstimatedDocumentPart.Footer)
        };

        var blocks = FaithfulDocumentView.Build(lines);

        Assert.Equal(3, blocks.Count);
        var table = blocks[1];
        Assert.Equal(EstimatedDocumentPart.Content, table.Part);
        Assert.Equal(FaithfulBlockKind.Table, table.Kind);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal(2, table.Lines.Count);
        Assert.Equal("تفاح أحمر 240.00 كغ 450.00 450.00", table.Lines[0]);
        Assert.Equal("حليب معقم 2.000 حبة 120.00 240.00", table.Lines[1]);
    }

    [Fact]
    public void Build_DoesNotMergeTableRowsAcrossParts()
    {
        var lines = new[]
        {
            new DecisionReportPartLine("صنف 12,5 40,00", EstimatedDocumentPart.Header),
            new DecisionReportPartLine("صنف 13,5 41,00", EstimatedDocumentPart.Content)
        };

        var blocks = FaithfulDocumentView.Build(lines);

        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, block => Assert.Equal(FaithfulBlockKind.Table, block.Kind));
        Assert.Equal(EstimatedDocumentPart.Header, blocks[0].Part);
        Assert.Equal(EstimatedDocumentPart.Content, blocks[1].Part);
    }

    [Fact]
    public void Build_TableRowsKeepAllOriginalText()
    {
        var rows = new[]
        {
            "تفاح أحمر 240.00 كغ 450.00 450.00",
            "قهوة مطحونة 1.000 علبة 950.00 950.00"
        };
        var lines = rows.Select(text => new DecisionReportPartLine(text, EstimatedDocumentPart.Content)).ToList();

        var table = Assert.Single(FaithfulDocumentView.Build(lines));

        Assert.Equal(FaithfulBlockKind.Table, table.Kind);
        Assert.Equal(rows, table.Rows.Select(row => string.Join(' ', row)));
    }
}
