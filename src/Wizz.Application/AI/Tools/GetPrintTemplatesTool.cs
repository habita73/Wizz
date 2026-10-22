namespace Wizz.Application.AI.Tools;

using System.Text;

/// <summary>
/// أداة قوالب الطباعة: تجيب عن «ما القوالب المتاحة للطباعة»، «كيف أطبع تقريراً»،
/// وتُظهر الطابعة الحالية وعرض الورق وأنواع المستندات القابلة للطباعة/التصدير.
/// </summary>
public sealed class GetPrintTemplatesTool : IBuiltInTool
{
    private static readonly string[] Keywords =
    {
        "قالب", "قوالب", "نماذج", "الطباعه المتاحه", "ما يمكن طباعته", "ما يمكن طبعه",
        "انواع الطباعه", "خيارات الطباعه", "طابعه", "الطابعه", "template", "templates"
    };

    private readonly Func<PrinterInfo> _printerInfo;

    /// <param name="printerInfo">
    /// مزوّد معلومات الطابعة الحالية (قابل للحقن للاختبار).
    /// الافتراضي: طابعة تجريبية (Mock).
    /// </param>
    public GetPrintTemplatesTool(Func<PrinterInfo>? printerInfo = null)
    {
        _printerInfo = printerInfo ?? (() => new PrinterInfo("طابعة تجريبية (Mock)", false, 80));
    }

    public string Name => "GetPrintTemplates";

    public string Description => "قوالب/أنواع المستندات القابلة للطباعة ومعلومات الطابعة الحالية.";

    public int Order => 30;

    public Task<string?> TryAnswerAsync(BuiltInToolContext context, CancellationToken ct = default)
    {
        if (!HasAny(context.Question, Keywords))
            return Task.FromResult<string?>(null);

        var printer = _printerInfo();

        var sb = new StringBuilder();
        sb.AppendLine("🖨️ الطباعة والقوالب:");
        sb.AppendLine($"• الطابعة الحالية: {printer.DeviceName}");
        sb.AppendLine($"• الحالة: {(printer.IsConnected ? "متصلة وجاهزة" : "غير متصلة (محاكاة)")}");
        sb.AppendLine($"• عرض الورق: {printer.PaperWidthMm} مم");
        sb.AppendLine();
        sb.AppendLine("المستندات القابلة للطباعة/التصدير:");
        sb.AppendLine("• إيصال بيع (Receipt) — من بعد البيع أو من سجل المبيعات.");
        sb.AppendLine("• تقرير مبيعات لفترة (SalesReport) — صفحة التقارير.");
        sb.AppendLine("• قائمة الديون (DebtList) — يمكن توليدها من محادثة المساعد «قائمة الديون».");
        sb.AppendLine("• أي مخرج من المساعد (جدول/تقرير) يظهر ككائن بأزرار: عرض، تنزيل، طباعة، حذف.");
        sb.AppendLine();
        sb.AppendLine("صيغ التصدير: HTML، Markdown، JSON، CSV، نص عادي — و PDF عبر «طباعة / حفظ PDF» (A4، اتجاه RTL).");

        return Task.FromResult<string?>(sb.ToString().TrimEnd());
    }

    private static bool HasAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k, StringComparison.Ordinal));
}

/// <summary>معلومات الطابعة المعروضة من الأداة (نسخة مستقلة عن تنفيذ البنية التحتية).</summary>
public sealed record PrinterInfo(string DeviceName, bool IsConnected, int PaperWidthMm);
