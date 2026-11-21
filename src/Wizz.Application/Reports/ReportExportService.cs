namespace Wizz.Application.Reports;

using System.Globalization;
using System.Net;
using System.Text;
using Wizz.Application.DTOs;

/// <summary>جدول جاهز للتصدير: عنوان، عناوين أعمدة، وصفوف نصية.</summary>
/// <param name="Title">عنوان الجدول داخل الملف أو ورقة الطباعة.</param>
/// <param name="Headers">عناوين الأعمدة بترتيب العرض.</param>
/// <param name="Rows">الصفوف؛ كل صف بنفس عدد الأعمدة.</param>
/// <param name="EmphasizeLastRow">يُبرز الصف الأخير كإجمالي في ورقة الطباعة.</param>
public sealed record ReportTable(
    string Title,
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    bool EmphasizeLastRow = false);

/// <summary>مؤشر يظهر في شريط أعلى التقرير المطبوع.</summary>
public sealed record ReportMetric(string Label, string Value);

/// <summary>بيانات الترويسة والتذييل التي لا تُحسب من الجداول.</summary>
/// <param name="Address">عنوان المتجر.</param>
/// <param name="Phone">هاتف المتجر.</param>
/// <param name="TaxNumber">الرقم الجبائي.</param>
/// <param name="CommercialRegister">السجل التجاري.</param>
/// <param name="PrintedBy">اسم من طلب الطباعة.</param>
/// <param name="Currency">رمز العملة المعروض.</param>
/// <param name="Metrics">مؤشرات الشريط العلوي.</param>
/// <param name="PrintedAt">وقت الطباعة؛ يُستخدم الوقت الحالي إن لم يُمرَّر.</param>
public sealed record ReportPrintInfo(
    string? Address = null,
    string? Phone = null,
    string? TaxNumber = null,
    string? CommercialRegister = null,
    string? PrintedBy = null,
    string? Currency = null,
    IReadOnlyList<ReportMetric>? Metrics = null,
    DateTime? PrintedAt = null);

/// <summary>
/// يحوّل بيانات التقارير المحسوبة إلى CSV متوافق مع Excel وصفحة طباعة عربية.
/// لا يقرأ قاعدة بيانات ولا يعيد حساب أي رقم؛ كل ما يخرج منه مبني على البيانات الممرّرة إليه.
/// </summary>
public sealed class ReportExportService
{
    /// <summary>يُظهر الأرقام بفاصلة عشرية ثابتة وبثقافة محايدة كي يقرأها Excel كأرقام.</summary>
    private static string Num(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Date(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Time(DateTime value) => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>تسمية طريقة الدفع — مصدر واحد للعرض والتصدير.</summary>
    public static string PaymentMethodLabel(PaymentMethodDto method) => method switch
    {
        PaymentMethodDto.Cash => "نقدي",
        PaymentMethodDto.Card => "بطاقة",
        PaymentMethodDto.Credit => "آجل",
        PaymentMethodDto.Mobile => "محمول",
        PaymentMethodDto.Mixed => "مختلط",
        _ => "—"
    };

    public ReportTable BuildSummary(
        decimal totalSales,
        decimal totalDiscount,
        decimal totalPaid,
        decimal totalRemaining,
        int salesCount,
        IReadOnlyList<PaymentMethodBreakdownDto> payments)
    {
        var rows = new List<IReadOnlyList<string>>
        {
            new[] { "إجمالي المبيعات", Num(totalSales) + " د.ج" },
            new[] { "إجمالي الخصم", Num(totalDiscount) + " د.ج" },
            new[] { "الإجمالي قبل الخصم", Num(totalSales + totalDiscount) + " د.ج" },
            new[] { "المدفوع", Num(totalPaid) + " د.ج" },
            new[] { "المتبقي", Num(totalRemaining) + " د.ج" },
            new[] { "عدد العمليات", Int(salesCount) },
            new[] { "متوسط البيع", Num(salesCount > 0 ? totalSales / salesCount : 0m) + " د.ج" }
        };

        rows.AddRange(payments.Select(p => new[] { "طريقة الدفع: " + PaymentMethodLabel(p.Method), Num(p.Amount) + " د.ج — " + Int(p.Count) + " عملية" }));
        return new ReportTable("إحصائيات الفترة", new[] { "البند", "القيمة" }, rows);
    }

    public ReportTable BuildDaily(IReadOnlyList<DailySalesDto> rows) => new(
        "المبيعات اليومية",
        new[] { "التاريخ", "المبيعات", "المدفوع", "المتبقي", "العمليات" },
        rows.Select(d => (IReadOnlyList<string>)new[]
        {
            Date(DateOnly.FromDateTime(d.Date)),
            Num(d.Total), Num(d.Paid), Num(d.Remaining), Int(d.Count)
        }).ToList());

    public ReportTable BuildProducts(IReadOnlyList<(string Name, decimal QuantitySold, decimal TotalSales)> rows) => new(
        "أفضل المنتجات",
        new[] { "المنتج", "الكمية المباعة", "إجمالي المبيعات", "متوسط السعر" },
        rows.Select(p => (IReadOnlyList<string>)new[]
        {
            p.Name,
            Num(p.QuantitySold),
            Num(p.TotalSales),
            Num(p.QuantitySold > 0 ? p.TotalSales / p.QuantitySold : 0m)
        }).ToList());

    public ReportTable BuildPayment(IReadOnlyList<PaymentMethodBreakdownDto> payments, decimal totalSales) => new(
        "تحليل طرق الدفع",
        new[] { "طريقة الدفع", "العدد", "المبلغ", "النسبة" },
        payments.Select(p => (IReadOnlyList<string>)new[]
        {
            PaymentMethodLabel(p.Method),
            Int(p.Count),
            Num(p.Amount),
            (totalSales > 0 ? (p.Amount / totalSales * 100m).ToString("0.0", CultureInfo.InvariantCulture) : "0") + "%"
        }).ToList());

    public ReportTable BuildCash(CashReportDto report)
    {
        var rows = new List<IReadOnlyList<string>>();
        foreach (var row in report.Rows)
        {
            rows.Add(new[]
            {
                Time(row.OpenedAt),
                row.ClosedAt is { } closed ? Time(closed) : "—",
                row.CashierName,
                Num(row.OpeningFloat),
                Num(row.CashSalesTotal),
                Num(row.DepositsTotal),
                Num(row.WithdrawalsTotal),
                Num(row.ExpectedCash),
                row.CountedCash is { } counted ? Num(counted) : "—",
                row.CashDifference is { } diff ? Num(diff) : "—",
                row.StatusLabel
            });
        }

        foreach (var row in report.Rows.Where(r => !string.IsNullOrWhiteSpace(r.CloseNote)))
        {
            rows.Add(new[] { "ملاحظة إغلاق — " + Time(row.OpenedAt) + ": " + row.CloseNote! });
        }

        return new ReportTable(
            "الورديات",
            new[] { "الفتح", "الإغلاق", "الكاشير", "الفكة", "مبيعات نقدية", "إيداعات", "سحوبات", "المتوقع", "المعدود", "الفرق", "الحالة" },
            rows);
    }

    /// <summary>
    /// تقرير المشتريات. حين لا تكون صلاحية عرض التكلفة متاحة تُصدَّر أعداد المستندات فقط،
    /// فلا يظهر أي مبلغ تكلفة في الملف أو ورقة الطباعة.
    /// </summary>
    public IReadOnlyList<ReportTable> BuildPurchases(PurchaseReportDto report, bool includeCosts)
    {
        if (!includeCosts)
        {
            return new[]
            {
                new ReportTable(
                    "المستندات",
                    new[] { "البند", "القيمة" },
                    new IReadOnlyList<string>[]
                    {
                        new[] { "مستندات نشطة", Int(report.DocumentCount) },
                        new[] { "مستندات ملغاة", Int(report.VoidedDocumentCount) }
                    })
            };
        }

        var bySupplier = new ReportTable(
            "حسب المورد",
            new[] { "المورد", "المستندات", "إجمالي الشراء" },
            report.BySupplier.Select(s => (IReadOnlyList<string>)new[]
            {
                s.SupplierName, Int(s.DocumentCount), Num(s.TotalPurchase)
            }).ToList());

        var byProduct = new ReportTable(
            "حسب المنتج",
            new[] { "المنتج", "الكمية", "إجمالي الشراء" },
            report.ByProduct.Select(p => (IReadOnlyList<string>)new[]
            {
                p.ProductName, Num(p.Quantity), Num(p.TotalPurchase)
            }).ToList());

        var totals = new ReportTable(
            "الإجمالي",
            new[] { "البند", "القيمة" },
            new IReadOnlyList<string>[]
            {
                new[] { "مستندات نشطة", Int(report.DocumentCount) },
                new[] { "مستندات ملغاة", Int(report.VoidedDocumentCount) },
                new[] { "إجمالي الشراء (ليس ربحًا ولا تكلفة مبيعات)", Num(report.TotalPurchase) + " د.ج" }
            },
            EmphasizeLastRow: true);

        return new[] { totals, bySupplier, byProduct };
    }

    /// <summary>اسم ملف آمن بالأحرف اللاتينية والأرقام، مع الفترة.</summary>
    public string BuildFileName(string slug, DateOnly from, DateOnly to)
    {
        var safe = new string((slug ?? "report")
            .Where(ch => char.IsAsciiLetterOrDigit(ch))
            .Select(char.ToLowerInvariant)
            .ToArray());
        if (safe.Length == 0) safe = "report";
        return $"wizz-{safe}-{Date(from).Replace("-", "")}-{Date(to).Replace("-", "")}.csv";
    }

    /// <summary>CSV بترميز UTF-8 مع علامة BOM كي تُقرأ العربية سليمة في Excel.</summary>
    public byte[] BuildCsv(string storeName, string reportTitle, DateOnly from, DateOnly to, IReadOnlyList<ReportTable> tables)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CsvRow(new[] { "اسم المتجر", storeName ?? string.Empty }));
        sb.AppendLine(CsvRow(new[] { "التقرير", reportTitle ?? string.Empty }));
        sb.AppendLine(CsvRow(new[] { "الفترة", Date(from) + " إلى " + Date(to) }));
        sb.AppendLine();

        foreach (var table in tables)
        {
            sb.AppendLine(CsvRow(new[] { table.Title }));
            sb.AppendLine(CsvRow(table.Headers));
            foreach (var row in table.Rows)
                sb.AppendLine(CsvRow(row));
            sb.AppendLine();
        }

        var text = Encoding.UTF8.GetBytes(sb.ToString());
        var withBom = new byte[text.Length + 3];
        withBom[0] = 0xEF;
        withBom[1] = 0xBB;
        withBom[2] = 0xBF;
        Buffer.BlockCopy(text, 0, withBom, 3, text.Length);
        return withBom;
    }

    /// <summary>
    /// صفحة طباعة رسمية بالعربية وRTL؛ تُمرَّر إلى نافذة الطباعة في المتصفح.
    /// الترويسة والمؤشرات والتذييل تُبنى من البيانات الممرّرة، ولا يُعاد حساب أي رقم.
    /// </summary>
    public string BuildPrintHtml(
        string storeName,
        string reportTitle,
        DateOnly from,
        DateOnly to,
        IReadOnlyList<ReportTable> tables,
        ReportPrintInfo? info = null)
    {
        var meta = info ?? new ReportPrintInfo();
        var landscape = tables.Any(t => t.Headers.Count > 7);
        var printedAt = Time(meta.PrintedAt ?? DateTime.Now);
        var currency = string.IsNullOrWhiteSpace(meta.Currency) ? "د.ج" : meta.Currency!;

        // أبعاد مساحة المحتوى بعد خصم الهوامش؛ يستخدمها المتصفح لتحديد صفحة واحدة من تعددها.
        const double pxPerMm = 96.0 / 25.4;
        var contentWidthPx = (int)Math.Round((landscape ? 273 : 186) * pxPerMm);
        var contentHeightPx = (int)Math.Round((landscape ? 180 : 267) * pxPerMm);

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"ar\" dir=\"rtl\" data-page-width-px=\"");
        sb.Append(contentWidthPx.ToString(CultureInfo.InvariantCulture)).Append("\" data-page-height-px=\"");
        sb.Append(contentHeightPx.ToString(CultureInfo.InvariantCulture)).Append("\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>").Append(Encode(reportTitle)).Append("</title>");
        sb.Append("<style>");
        sb.Append("@page{size:A4 ").Append(landscape ? "landscape" : "portrait").Append(";margin:14mm 12mm 16mm;}");
        sb.Append("*{box-sizing:border-box;}html,body{margin:0;padding:0;}");
        sb.Append("body{font-family:Tahoma,'Segoe UI',Arial,sans-serif;font-size:9pt;line-height:1.5;color:#24313d;-webkit-print-color-adjust:exact;print-color-adjust:exact;}");
        sb.Append(".letterhead{display:flex;justify-content:space-between;gap:16px;border-bottom:2px solid #124a6b;padding:0 0 10px;margin-bottom:12px;}");
        sb.Append(".brand h1{font-size:18pt;line-height:1.1;margin:0;color:#124a6b;}");
        sb.Append(".brand p{margin:3px 0 0;font-size:8pt;color:#5b6b78;}");
        sb.Append(".doc{text-align:left;font-size:8pt;color:#5b6b78;white-space:nowrap;}");
        sb.Append(".doc strong{display:block;font-size:10pt;color:#24313d;margin-bottom:3px;}");
        sb.Append(".titlebar{background:#eef4f7;border-right:4px solid #1c74a1;padding:8px 12px;margin-bottom:10px;}");
        sb.Append(".titlebar h2{margin:0;font-size:13pt;color:#124a6b;}");
        sb.Append(".titlebar p{margin:3px 0 0;font-size:8.5pt;color:#5b6b78;}");
        sb.Append(".metrics{display:flex;gap:8px;margin:10px 0 14px;}");
        sb.Append(".metric{flex:1;border:1px solid #d7e3ea;border-top:3px solid #1c74a1;border-radius:3px;background:#fbfdfe;padding:7px 6px;text-align:center;}");
        sb.Append(".metric span{display:block;font-size:7.5pt;color:#5b6b78;}");
        sb.Append(".metric b{display:block;font-size:11pt;color:#124a6b;margin-top:3px;}");
        sb.Append("h3{font-size:10.5pt;color:#124a6b;margin:14px 0 6px;padding-bottom:4px;border-bottom:1px solid #d7e3ea;}");
        sb.Append("table{width:100%;border-collapse:collapse;margin:0 0 12px;font-size:8.5pt;}");
        sb.Append("thead{display:table-header-group;}");
        sb.Append("th{background:#124a6b;color:#fff;border:1px solid #124a6b;padding:5px 6px;text-align:right;font-weight:600;}");
        sb.Append("td{border:1px solid #d7e3ea;padding:4px 6px;vertical-align:top;}");
        sb.Append("tbody tr:nth-child(even){background:#f5f9fb;}");
        sb.Append("tr{page-break-inside:avoid;}");
        sb.Append("td.num{text-align:left;direction:ltr;unicode-bidi:isolate;font-variant-numeric:tabular-nums;white-space:nowrap;}");
        sb.Append("tr.total td{font-weight:700;background:#e8f1f6;}");
        sb.Append(".signoff{margin-top:16px;padding-top:8px;border-top:1px dashed #b8c6d0;display:flex;justify-content:space-between;font-size:8pt;color:#5b6b78;}");
        sb.Append("</style>");
        // ترقيم الصفحات في ذيل الورقة؛ يزيله سكربت الطباعة إن كان المحتوى صفحة واحدة.
        sb.Append("<style id=\"wizz-page-number\">@page{@bottom-center{content:\"صفحة \" counter(page) \" من \" counter(pages);font-family:Tahoma,Arial,sans-serif;font-size:8pt;color:#66788a;}");
        sb.Append("@bottom-right{content:\"تقرير للمراجعة\";font-family:Tahoma,Arial,sans-serif;font-size:8pt;color:#8899a6;}}</style>");
        sb.Append("</head><body><div id=\"wizz-content\">");

        // ترويسة المتجر: الاسم والعنوان والهاتف والرقم الجبائي والسجل التجاري.
        sb.Append("<div class=\"letterhead\"><div class=\"brand\"><h1>").Append(Encode(storeName)).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(meta.Address))
            sb.Append("<p>").Append(Encode(meta.Address)).Append("</p>");
        var contacts = new List<string>();
        if (!string.IsNullOrWhiteSpace(meta.Phone)) contacts.Add("هاتف: " + meta.Phone);
        if (!string.IsNullOrWhiteSpace(meta.TaxNumber)) contacts.Add("الرقم الجبائي: " + meta.TaxNumber);
        if (!string.IsNullOrWhiteSpace(meta.CommercialRegister)) contacts.Add("السجل التجاري: " + meta.CommercialRegister);
        if (contacts.Count > 0)
            sb.Append("<p>").Append(Encode(string.Join(" · ", contacts))).Append("</p>");
        sb.Append("</div><div class=\"doc\"><strong>وثيقة تقرير</strong>");
        sb.Append("وقت الطباعة: ").Append(Encode(printedAt));
        if (!string.IsNullOrWhiteSpace(meta.PrintedBy))
            sb.Append("<br>بواسطة: ").Append(Encode(meta.PrintedBy));
        sb.Append("</div></div>");

        sb.Append("<div class=\"titlebar\"><h2>").Append(Encode(reportTitle)).Append("</h2>");
        sb.Append("<p>من ").Append(Date(from)).Append(" إلى ").Append(Date(to));
        sb.Append(" · العملة: ").Append(Encode(currency)).Append("</p></div>");

        if (meta.Metrics is { Count: > 0 })
        {
            sb.Append("<div class=\"metrics\">");
            foreach (var metric in meta.Metrics)
            {
                sb.Append("<div class=\"metric\"><span>").Append(Encode(metric.Label))
                  .Append("</span><b>").Append(Encode(metric.Value)).Append("</b></div>");
            }
            sb.Append("</div>");
        }

        foreach (var table in tables)
        {
            sb.Append("<h3>").Append(Encode(table.Title)).Append("</h3><table><thead><tr>");
            foreach (var header in table.Headers)
                sb.Append("<th>").Append(Encode(header)).Append("</th>");
            sb.Append("</tr></thead><tbody>");

            for (var r = 0; r < table.Rows.Count; r++)
            {
                var row = table.Rows[r];
                sb.Append(table.EmphasizeLastRow && r == table.Rows.Count - 1
                    ? "<tr class=\"total\">"
                    : "<tr>");
                var span = table.Headers.Count - row.Count + 1;
                for (var i = 0; i < row.Count; i++)
                {
                    var colspan = i == row.Count - 1 && span > 1
                        ? " colspan=\"" + span.ToString(CultureInfo.InvariantCulture) + "\""
                        : string.Empty;
                    var cls = IsNumericCell(row[i]) ? " class=\"num\"" : string.Empty;
                    sb.Append("<td").Append(colspan).Append(cls).Append(">").Append(Encode(row[i])).Append("</td>");
                }
                sb.Append("</tr>");
            }
            sb.Append("</tbody></table>");
        }

        sb.Append("<div class=\"signoff\"><span>تقرير للمراجعة — ليس فاتورة ولا إيصالًا</span>");
        sb.Append("<span>Wizz · ").Append(Encode(printedAt)).Append("</span></div>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    /// <summary>يضبط اتجاه الخلايا الرقمية لليسار كي تتساوى الأعمدة داخل جدول RTL.</summary>
    private static bool IsNumericCell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value!.Trim();
        if (text.Length == 0 || !(char.IsAsciiDigit(text[0]) || text[0] == '-')) return false;

        foreach (var ch in text)
        {
            if (char.IsAsciiDigit(ch) || ch is '.' or ',' or '-' or ':' or '%' or ' ' or 'د' or 'ج')
                continue;
            return false;
        }
        return true;
    }

    /// <summary>
    /// يمنع Excel من تفسير نصوص المستخدم كمعادلة، مع ترك الأرقام السالبة التي نولّدها أرقامًا.
    /// </summary>
    private static string CsvCell(string value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0)
        {
            var first = text[0];
            var dangerous = first is '=' or '+' or '@'
                || (first == '-' && (text.Length == 1 || !char.IsAsciiDigit(text[1])));
            if (dangerous)
                text = "'" + text;
        }
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }

    private static string CsvRow(IReadOnlyList<string> cells) =>
        string.Join(",", cells.Select(CsvCell));

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
