using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية لتسليم نتيجة القراءة إلى شاشة أخرى.
/// لا قاعدة بيانات ولا ملف ولا إرسال في هذه الاختبارات.
/// </summary>
public sealed class OcrDraftHandoffTests
{
    [Fact]
    public void Take_ReturnsPayloadOnlyForItsTarget()
    {
        var handoff = new OcrDraftHandoff();
        handoff.Set("حليب 12.5\nخبز 5", OcrDraftHandoff.SaleTarget);

        Assert.Null(handoff.Take(OcrDraftHandoff.PurchaseTarget));
        Assert.Equal("حليب 12.5\nخبز 5", handoff.Take(OcrDraftHandoff.SaleTarget)?.Text);
    }

    [Fact]
    public void Take_ClearsDraftAfterFirstRead()
    {
        var handoff = new OcrDraftHandoff();
        handoff.Set("خبز 5", OcrDraftHandoff.SaleTarget);

        Assert.NotNull(handoff.Take(OcrDraftHandoff.SaleTarget));
        Assert.Null(handoff.Take(OcrDraftHandoff.SaleTarget));
    }

    [Fact]
    public void Set_EmptyTextOrTarget_IsIgnored()
    {
        var handoff = new OcrDraftHandoff();
        handoff.Set("   ", OcrDraftHandoff.SaleTarget);
        handoff.Set("خبز 5", "   ");

        Assert.Null(handoff.Take(OcrDraftHandoff.SaleTarget));
    }

    [Fact]
    public void Clear_RemovesPendingDraft()
    {
        var handoff = new OcrDraftHandoff();
        handoff.Set("حليب 12.5", OcrDraftHandoff.PurchaseTarget);
        handoff.Clear();

        Assert.Null(handoff.Take(OcrDraftHandoff.PurchaseTarget));
    }

    [Fact]
    public void Set_ReplacesPreviousDraft()
    {
        var handoff = new OcrDraftHandoff();
        handoff.Set("نص قديم", OcrDraftHandoff.SaleTarget);
        handoff.Set("نص جديد", OcrDraftHandoff.PurchaseTarget);

        Assert.Null(handoff.Take(OcrDraftHandoff.SaleTarget));
        Assert.Equal("نص جديد", handoff.Take(OcrDraftHandoff.PurchaseTarget)?.Text);
    }

    [Fact]
    public void Take_CarriesAcceptedLinesWithText()
    {
        var handoff = new OcrDraftHandoff();
        var lines = new[] { new OcrDraftLine("حليب", 12.5m), new OcrDraftLine("خبز", 5m) };
        handoff.Set("حليب 12.5\nخبز 5", OcrDraftHandoff.SaleTarget, lines);

        var payload = handoff.Take(OcrDraftHandoff.SaleTarget);

        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Lines.Count);
        Assert.Equal("حليب", payload.Lines[0].Name);
        Assert.Equal(12.5m, payload.Lines[0].Price);
    }

    [Fact]
    public void Set_WithoutLines_YieldsEmptyLines()
    {
        var handoff = new OcrDraftHandoff();
        handoff.Set("نص بلا أصناف", OcrDraftHandoff.SaleTarget);

        Assert.Empty(handoff.Take(OcrDraftHandoff.SaleTarget)!.Lines);
    }
}
