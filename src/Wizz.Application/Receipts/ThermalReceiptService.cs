namespace Wizz.Application.Receipts;

using System.Globalization;
using System.Net;
using System.Text;
using Wizz.Application.DTOs;

/// <summary>
/// يحوّل إيصال بيع محسوبًا إلى صفحة طباعة حرارية بعرض 80 ملم واتجاه عربي RTL.
/// لا يعيد حساب أي مبلغ؛ كل القيم مأخوذة من <see cref="ReceiptDto"/> كما وصلت.
/// لا يقرأ قاعدة بيانات ولا يتصل بأي جهاز؛ الطباعة تتم في المتصفح عبر wizzPrint.
/// </summary>
public sealed class ThermalReceiptService
{
    /// <summary>عرض الورقة الحرارية بالمليمتر.</summary>
    public const int PaperWidthMm = 80;

    /// <summary>عرض الإطار المطلوب لقياس الطباعة في المتصفح (96 بكسل/بوصة).</summary>
    public int PreviewWidthPx => (int)Math.Round(PaperWidthMm * 96.0 / 25.4);

    /// <summary>يبني صفحة إيصال جاهزة للطباعة في iframe المتصفح.</summary>
    public string BuildHtml(ReceiptDto receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var currency = string.IsNullOrWhiteSpace(receipt.Store.Currency) ? "د.ج" : receipt.Store.Currency.Trim();
        var sb = new StringBuilder();

        sb.Append("<!doctype html><html lang=\"ar\" dir=\"rtl\" data-page-width-px=\"");
        sb.Append(PreviewWidthPx.ToString(CultureInfo.InvariantCulture));
        sb.Append("\"><head><meta charset=\"utf-8\">");
        sb.Append("<title>إيصال بيع ").Append(Encode(receipt.SaleNumber)).Append("</title>");
        sb.Append("<style>");
        sb.Append("@page{size:80mm auto;margin:0;}");
        sb.Append("*{box-sizing:border-box;}html,body{margin:0;padding:0;background:#fff;}");
        sb.Append("body{font-family:Tahoma,'Segoe UI',Arial,sans-serif;font-size:11px;line-height:1.5;color:#000;direction:rtl;}");
        sb.Append(".receipt{width:80mm;padding:4mm 3mm;margin:0 auto;}");
        sb.Append(".center{text-align:center;}");
        sb.Append(".store{font-size:15px;font-weight:700;line-height:1.2;}");
        sb.Append(".line{font-size:10px;}");
        sb.Append(".rule{border-top:1px dashed #000;margin:6px 0;}");
        sb.Append(".row{display:flex;justify-content:space-between;gap:8px;}");
        sb.Append(".row>span:first-child{overflow-wrap:anywhere;}");
        sb.Append(".item{margin:3px 0;}");
        sb.Append(".item-detail{font-size:9.5px;color:#333;}");
        sb.Append(".num{direction:ltr;unicode-bidi:isolate;white-space:nowrap;font-variant-numeric:tabular-nums;}");
        sb.Append(".grand{font-weight:700;font-size:12px;}");
        sb.Append(".footer{text-align:center;margin-top:8px;font-size:10px;}");
        sb.Append(".note{font-size:9.5px;color:#333;}");
        sb.Append("</style></head><body><div class=\"receipt\">");

        // ترويسة المتجر.
        sb.Append("<div class=\"center\"><div class=\"store\">").Append(Encode(receipt.Store.Name)).Append("</div>");
        if (!string.IsNullOrWhiteSpace(receipt.Store.Address))
            sb.Append("<div class=\"line\">").Append(Encode(receipt.Store.Address)).Append("</div>");
        var contacts = new List<string>();
        if (!string.IsNullOrWhiteSpace(receipt.Store.Phone)) contacts.Add("هاتف: " + receipt.Store.Phone);
        if (!string.IsNullOrWhiteSpace(receipt.Store.TaxNumber)) contacts.Add("الرقم الجبائي: " + receipt.Store.TaxNumber);
        if (!string.IsNullOrWhiteSpace(receipt.Store.CommercialRegister)) contacts.Add("السجل التجاري: " + receipt.Store.CommercialRegister);
        if (contacts.Count > 0)
            sb.Append("<div class=\"line\">").Append(Encode(string.Join(" · ", contacts))).Append("</div>");
        sb.Append("</div><div class=\"rule\"></div>");

        // بيانات العملية.
        sb.Append("<div class=\"row\"><span>رقم البيع</span><span class=\"num\">")
          .Append(Encode(receipt.SaleNumber)).Append("</span></div>");
        sb.Append("<div class=\"row\"><span>التاريخ</span><span class=\"num\">")
          .Append(Encode(Time(receipt.SaleDate))).Append("</span></div>");
        if (!string.IsNullOrWhiteSpace(receipt.CashierName))
            sb.Append("<div class=\"row\"><span>الكاشير</span><span>")
              .Append(Encode(receipt.CashierName)).Append("</span></div>");
        sb.Append("<div class=\"row\"><span>طريقة الدفع</span><span>")
          .Append(Encode(PaymentLabel(receipt.PaymentMethod))).Append("</span></div>");
        if (receipt.IsCreditSale && receipt.DueDate is { } due)
            sb.Append("<div class=\"row\"><span>تاريخ الاستحقاق</span><span class=\"num\">")
              .Append(Encode(due.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))).Append("</span></div>");
        sb.Append("<div class=\"rule\"></div>");

        // الأصناف.
        foreach (var line in receipt.Lines)
        {
            sb.Append("<div class=\"item row\"><span>").Append(Encode(line.ProductName)).Append("</span>");
            sb.Append("<span class=\"num\">").Append(Money(line.LineTotal, currency)).Append("</span></div>");
            sb.Append("<div class=\"item-detail row\"><span>")
              .Append(Encode(Quantity(line))).Append(" × ").Append(Money(line.UnitPrice, currency))
              .Append("</span></div>");
        }
        sb.Append("<div class=\"rule\"></div>");

        // الإجماليات — القيم كما وصلت في الإيصال.
        sb.Append(TotalRow("المجموع", receipt.Subtotal, currency));
        sb.Append(TotalRow("الخصم", receipt.Discount, currency));
        sb.Append("<div class=\"row grand\"><span>الإجمالي</span><span class=\"num\">")
          .Append(Money(receipt.Total, currency)).Append("</span></div>");
        sb.Append(TotalRow("المدفوع", receipt.Paid, currency));
        if (receipt.Remaining > 0)
            sb.Append(TotalRow("المتبقي", receipt.Remaining, currency));
        sb.Append("<div class=\"rule\"></div>");

        if (!string.IsNullOrWhiteSpace(receipt.Notes))
            sb.Append("<div class=\"note\">").Append(Encode(receipt.Notes)).Append("</div>");
        sb.Append("<div class=\"footer\">شكرًا لتعاملكم معنا</div>");
        sb.Append("<div class=\"footer note\">وصل بيع — احتفظ به للمراجعة</div>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static string TotalRow(string label, decimal value, string currency)
        => "<div class=\"row\"><span>" + Encode(label) + "</span><span class=\"num\">"
           + Money(value, currency) + "</span></div>";

    /// <summary>أرقام ثابتة بفاصلة عشرية ولاتينية كي تُقرأ بوضوح على الورقة الحرارية.</summary>
    private static string Money(decimal value, string currency)
        => value.ToString("0.00", CultureInfo.InvariantCulture) + " " + currency;

    private static string Quantity(ReceiptLineDto line)
        => line.Quantity.ToString("0.###", CultureInfo.InvariantCulture) + " " + line.UnitSymbol;

    private static string Time(DateTime value)
        => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    private static string PaymentLabel(PaymentMethodDto method) => method switch
    {
        PaymentMethodDto.Cash => "نقدي",
        PaymentMethodDto.Card => "بطاقة",
        PaymentMethodDto.Credit => "آجل",
        PaymentMethodDto.Mobile => "دفع عبر الهاتف",
        PaymentMethodDto.Mixed => "مختلط",
        _ => "غير محدد"
    };

    private static string Encode(string? value)
        => value is null ? string.Empty : WebUtility.HtmlEncode(value);
}
