using System.Text;
using Wizz.Application.DTOs;
using Wizz.Application.Reports;

namespace Wizz.Integration.Tests;

/// <summary>اختبارات ببيانات اصطناعية لتصدير التقارير بلا قاعدة بيانات ولا اتصال.</summary>
public sealed class ReportExportServiceTests
{
    private readonly ReportExportService _sut = new();
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 11, 21);

    [Fact]
    public void BuildCsv_StartsWithUtf8Bom_AndKeepsArabic()
    {
        var table = new ReportTable(
            "المنتجات",
            new[] { "المنتج", "القيمة" },
            new IReadOnlyList<string>[] { new[] { "أرز", "3.50" } });

        var bytes = _sut.BuildCsv("متجر التجربة", "تقرير", From, To, new[] { table });

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        Assert.Contains("متجر التجربة", text);
        Assert.Contains("المنتجات", text);
        Assert.Contains("أرز", text);
    }

    [Fact]
    public void BuildCsv_EscapesQuotesAndCommas_AndNeutralizesTextFormulas()
    {
        var table = new ReportTable(
            "ت",
            new[] { "أ", "ب" },
            new IReadOnlyList<string>[]
            {
                new[] { "سعر,كمية", "قال \"مرحبا\"" },
                new[] { "=SUM(1,2)", "@cmd" },
                new[] { "+cmd", "-cmd" }
            });

        var bytes = _sut.BuildCsv("س", "ت", From, To, new[] { table });
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        Assert.Contains("\"سعر,كمية\"", text);
        Assert.Contains("\"قال \"\"مرحبا\"\"\"", text);
        Assert.Contains("\"'=SUM(1,2)\"", text);
        Assert.Contains("\"'@cmd\"", text);
        Assert.Contains("\"'+cmd\"", text);
        Assert.Contains("\"'-cmd\"", text);
    }

    [Fact]
    public void BuildSummary_UsesInvariantNumbers_AndArabicPaymentLabels()
    {
        var table = _sut.BuildSummary(
            1000.5m, 10m, 900m, 100.5m, 2,
            new[] { new PaymentMethodBreakdownDto(PaymentMethodDto.Cash, 2, 1000.5m) });

        var label = Assert.Single(table.Rows.Where(r => r[0].StartsWith("طريقة الدفع", StringComparison.Ordinal)));
        Assert.Equal("طريقة الدفع: نقدي", label[0]);
        Assert.Equal("1000.50 د.ج — 2 عملية", label[1]);

        var average = Assert.Single(table.Rows.Where(r => r[0] == "متوسط البيع"));
        Assert.Equal("500.25 د.ج", average[1]);

        var beforeDiscount = Assert.Single(table.Rows.Where(r => r[0] == "الإجمالي قبل الخصم"));
        Assert.Equal("1010.50 د.ج", beforeDiscount[1]);
    }

    [Fact]
    public void BuildCsv_KeepsNegativeGeneratedNumbersAsNumbers()
    {
        var table = new ReportTable("الفرق", new[] { "القيمة" }, new IReadOnlyList<string>[] { new[] { "-5.25" } });
        var bytes = _sut.BuildCsv("س", "ت", From, To, new[] { table });
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        Assert.Contains("\"-5.25\"", text);
        Assert.DoesNotContain("'-5.25", text);
    }

    [Fact]
    public void BuildPurchases_WithoutCostPermission_ExportsCountsOnly()
    {
        var tables = _sut.BuildPurchases(PurchaseReportWithTotals(), includeCosts: false);

        var table = Assert.Single(tables);
        Assert.Equal("المستندات", table.Title);
        Assert.DoesNotContain(tables, t => t.Title.Contains("حسب", StringComparison.Ordinal));
        Assert.All(table.Rows, r => Assert.DoesNotContain("2500.00", string.Join("|", r)));
        Assert.Equal("3", table.Rows[0][1]);
        Assert.Equal("1", table.Rows[1][1]);
    }

    [Fact]
    public void BuildPurchases_WithCostPermission_IncludesAmountsAndBreakdowns()
    {
        var tables = _sut.BuildPurchases(PurchaseReportWithTotals(), includeCosts: true);

        Assert.Equal(3, tables.Count);
        Assert.Contains(tables, t => t.Title == "حسب المورد");
        Assert.Contains(tables, t => t.Title == "حسب المنتج");
        var totals = Assert.Single(tables.Where(t => t.Title == "الإجمالي"));
        Assert.Equal("2500.00 د.ج", totals.Rows[2][1]);
    }

    [Fact]
    public void BuildPrintHtml_IsRtlAndEscapesUserText()
    {
        var table = new ReportTable(
            "تقرير",
            new[] { "الاسم" },
            new IReadOnlyList<string>[] { new[] { "<b>خطر</b>" } });

        var html = _sut.BuildPrintHtml("متجر <س>", "تقرير", From, To, new[] { table });

        Assert.Contains("lang=\"ar\"", html);
        Assert.Contains("dir=\"rtl\"", html);
        Assert.DoesNotContain("<b>خطر</b>", html);
        Assert.Contains("&lt;b&gt;خطر&lt;/b&gt;", html);
        Assert.Contains("متجر &lt;س&gt;", html);
    }

    [Fact]
    public void BuildCash_CloseNoteSpansAllColumnsInPrint()
    {
        var table = _sut.BuildCash(CashReportWithNote());
        var html = _sut.BuildPrintHtml("س", "ت", From, To, new[] { table });

        Assert.Equal(11, table.Headers.Count);
        Assert.Contains("colspan=\"11\"", html);
        Assert.Contains("ملاحظة إغلاق مهمة", html);
    }

    [Fact]
    public void BuildPrintHtml_IncludesStoreLetterheadAndPrintMetadata()
    {
        var info = new ReportPrintInfo(
            Address: "شارع التجربة 12",
            Phone: "0550 12 34 56",
            TaxNumber: "123456789",
            CommercialRegister: "CR-77",
            PrintedBy: "كاشير الاختبار",
            PrintedAt: new DateTime(2026, 11, 21, 10, 30, 0, DateTimeKind.Utc));

        var html = _sut.BuildPrintHtml("متجر التجربة", "ملخص المبيعات", From, To, Array.Empty<ReportTable>(), info);

        Assert.Contains("متجر التجربة", html);
        Assert.Contains("شارع التجربة 12", html);
        Assert.Contains("هاتف: 0550 12 34 56", html);
        Assert.Contains("الرقم الجبائي: 123456789", html);
        Assert.Contains("السجل التجاري: CR-77", html);
        Assert.Contains("بواسطة: كاشير الاختبار", html);
        Assert.Contains("ليس فاتورة ولا إيصالًا", html);
    }

    [Fact]
    public void BuildPrintHtml_IncludesMetricsAndPageNumbers()
    {
        var info = new ReportPrintInfo(
            Currency: "د.ج",
            Metrics: new[]
            {
                new ReportMetric("إجمالي المبيعات", "1,250.00 د.ج"),
                new ReportMetric("عدد العمليات", "7")
            },
            PrintedAt: new DateTime(2026, 11, 21, 10, 30, 0, DateTimeKind.Utc));
        var table = new ReportTable("ت", new[] { "أ" }, new IReadOnlyList<string>[] { new[] { "ب" } });

        var html = _sut.BuildPrintHtml("س", "ت", From, To, new[] { table }, info);

        Assert.Contains("class=\"metrics\"", html);
        Assert.Contains("إجمالي المبيعات", html);
        Assert.Contains("1,250.00 د.ج", html);
        Assert.Contains("counter(page)", html);
        Assert.Contains("counter(pages)", html);
    }

    [Fact]
    public void BuildPrintHtml_UsesLandscapeForWideTables()
    {
        var narrow = new ReportTable("ت", new[] { "أ" }, new IReadOnlyList<string>[] { new[] { "ب" } });
        var wide = _sut.BuildCash(CashReportWithNote());

        Assert.Contains("size:A4 portrait", _sut.BuildPrintHtml("س", "ت", From, To, new[] { narrow }));
        Assert.Contains("size:A4 landscape", _sut.BuildPrintHtml("س", "ت", From, To, new[] { wide }));
    }

    [Fact]
    public void BuildPrintHtml_AlignsNumericCellsAndEmphasizesTotals()
    {
        var table = new ReportTable(
            "ت",
            new[] { "المنتج", "الكمية", "التاريخ" },
            new IReadOnlyList<string>[] { new[] { "أرز", "12.50", "2026-11-05 09:00" } });
        var html = _sut.BuildPrintHtml("س", "ت", From, To, new[] { table });

        Assert.Contains("<td>أرز</td>", html);
        Assert.Contains("<td class=\"num\">12.50</td>", html);
        Assert.Contains("<td class=\"num\">2026-11-05 09:00</td>", html);

        var purchases = _sut.BuildPurchases(PurchaseReportWithTotals(), includeCosts: true);
        var purchasesHtml = _sut.BuildPrintHtml("س", "ت", From, To, purchases);
        Assert.Contains("<tr class=\"total\">", purchasesHtml);
    }

    [Fact]
    public void BuildPrintHtml_MarksPageGeometryAndSeparatesPageNumberStyle()
    {
        var narrow = new ReportTable("ت", new[] { "أ" }, new IReadOnlyList<string>[] { new[] { "ب" } });
        var html = _sut.BuildPrintHtml("س", "ت", From, To, new[] { narrow });

        Assert.Contains("data-page-width-px=\"703\"", html);
        Assert.Contains("data-page-height-px=\"1009\"", html);
        Assert.Contains("<style id=\"wizz-page-number\">", html);
        Assert.Contains("<div id=\"wizz-content\">", html);
        var mainStyle = html[..html.IndexOf("</style>", StringComparison.Ordinal)];
        Assert.DoesNotContain("@bottom-center", mainStyle);

        var wide = _sut.BuildCash(CashReportWithNote());
        var wideHtml = _sut.BuildPrintHtml("س", "ت", From, To, new[] { wide });
        Assert.Contains("data-page-width-px=\"1032\"", wideHtml);
        Assert.Contains("data-page-height-px=\"680\"", wideHtml);
    }

    [Fact]
    public void BuildFileName_UsesLatinSlugAndPeriod()
    {
        Assert.Equal("wizz-daily-20261101-20261121.csv", _sut.BuildFileName("daily", From, To));
        Assert.Equal("wizz-report-20261101-20261121.csv", _sut.BuildFileName("تقرير!", From, To));
    }

    private static PurchaseReportDto PurchaseReportWithTotals() => new(
        From, To, 3, 1, 2500.00m,
        new[] { new PurchaseSupplierRowDto("مورد التجربة", 3, 2500.00m) },
        new[] { new PurchaseProductRowDto("أرز", 10m, 2500.00m) });

    private static CashReportDto CashReportWithNote() => new(
        From, To,
        new[]
        {
            new CashReportRowDto(
                Guid.NewGuid(),
                new DateTime(2026, 11, 5, 9, 0, 0, DateTimeKind.Utc),
                null,
                Guid.NewGuid(),
                "كاشير التجربة",
                IsOpen: true,
                HasCashCount: false,
                IsLive: true,
                OpeningFloat: 100m,
                CashSalesTotal: 500m,
                DepositsTotal: 0m,
                WithdrawalsTotal: 0m,
                ExpectedCash: 600m,
                CountedCash: null,
                CashDifference: null,
                CloseNote: "ملاحظة إغلاق مهمة",
                StatusLabel: "مباشر")
        },
        OpenShiftCount: 1, ClosedShiftCount: 0, LegacyShiftCount: 0);
}
