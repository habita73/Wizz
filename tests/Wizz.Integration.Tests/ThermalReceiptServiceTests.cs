using Wizz.Application.DTOs;
using Wizz.Application.Receipts;

namespace Wizz.Integration.Tests;

/// <summary>اختبارات ببيانات اصطناعية لمخرج الإيصال الحراري بلا طابعة ولا قاعدة بيانات.</summary>
public sealed class ThermalReceiptServiceTests
{
    private readonly ThermalReceiptService _sut = new();

    private static ReceiptDto Receipt(
        decimal remaining = 0m,
        string productName = "أرز بسمتي",
        bool isCredit = false) => new(
        Guid.NewGuid(),
        "SALE-0042",
        new DateTime(2026, 11, 21, 10, 30, 0, DateTimeKind.Local),
        new StoreDto(
            Guid.NewGuid(), "متجر النور", "شارع الاستقلال ١٤", "0550 12 34 56",
            "123456789012", "CR-2024-7781", true, "د.ج", "Africa/Algiers"),
        new[]
        {
            new ReceiptLineDto(productName, 2m, "كيس", 250.50m, 501m),
            new ReceiptLineDto("زيت زيتون", 1m, "عبوة", 400m, 400m)
        },
        901m, 1m, 900m, 900m - remaining, remaining,
        isCredit ? PaymentMethodDto.Credit : PaymentMethodDto.Cash,
        "أحمد كاشير", isCredit, isCredit ? new DateTime(2026, 12, 21) : null, null);

    [Fact]
    public void BuildHtml_Uses80mmRtlAndStoreLetterhead()
    {
        var html = _sut.BuildHtml(Receipt());

        Assert.Contains("<html lang=\"ar\" dir=\"rtl\" data-page-width-px=\"302\">", html);
        Assert.Contains("@page{size:80mm auto;margin:0;}", html);
        Assert.Contains("متجر النور", html);
        Assert.Contains("شارع الاستقلال ١٤", html);
        Assert.Contains("هاتف: 0550 12 34 56", html);
        Assert.Contains("الرقم الجبائي: 123456789012", html);
        Assert.Contains("السجل التجاري: CR-2024-7781", html);
        Assert.Contains("وصل بيع — احتفظ به للمراجعة", html);
    }

    [Fact]
    public void BuildHtml_IncludesSaleLinesAndTotals()
    {
        var html = _sut.BuildHtml(Receipt(remaining: 150m));

        Assert.Contains("SALE-0042", html);
        Assert.Contains("أحمد كاشير", html);
        Assert.Contains("نقدي", html);
        Assert.Contains("أرز بسمتي", html);
        Assert.Contains("501.00 د.ج", html);
        Assert.Contains("2 كيس × 250.50 د.ج", html);
        Assert.Contains("901.00 د.ج", html);
        Assert.Contains("1.00 د.ج", html);
        Assert.Contains("900.00 د.ج", html);
        Assert.Contains("750.00 د.ج", html);
        Assert.Contains("المتبقي", html);
        Assert.Contains("150.00 د.ج", html);
        Assert.Contains("class=\"num\"", html);
    }

    [Fact]
    public void BuildHtml_OmitsRemainingWhenZero()
    {
        var html = _sut.BuildHtml(Receipt());

        Assert.DoesNotContain("المتبقي", html);
        Assert.Contains("900.00 د.ج", html);
    }

    [Fact]
    public void BuildHtml_ShowsCreditDueDate()
    {
        var html = _sut.BuildHtml(Receipt(remaining: 300m, isCredit: true));

        Assert.Contains("آجل", html);
        Assert.Contains("تاريخ الاستحقاق", html);
        Assert.Contains("2026-12-21", html);
        Assert.Contains("المتبقي", html);
    }

    [Fact]
    public void BuildHtml_EscapesHtmlInProductName()
    {
        var html = _sut.BuildHtml(Receipt(productName: "<script>alert('x')</script>"));

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }
}
