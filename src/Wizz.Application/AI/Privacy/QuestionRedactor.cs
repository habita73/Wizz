namespace Wizz.Application.AI.Privacy;

using System.Text.RegularExpressions;

/// <summary>
/// نتيجة تنقيح سؤال المستخدم: ملخص مجهول فقط، مع تسجيل ما حُذف منه.
/// لا يُعاد أبداً النص الخام، ولا أي اسم عميل أو معرّف فاتورة أو رقم دقيق.
/// </summary>
/// <param name="Summary">الملخص المجهول المسموح إرساله.</param>
/// <param name="RemovedCategories">أنواع البيانات التي حُذفت من السؤال.</param>
/// <param name="IsSafe">هل الملخص يجتاز فحص البوابة؟</param>
/// <param name="Reason">سبب التنقيح.</param>
public sealed record RedactedQuestion(
    string Summary,
    IReadOnlyList<string> RemovedCategories,
    bool IsSafe,
    string Reason);

/// <summary>
/// ينقّح سؤال المستخدم إلزامياً قبل أي تشارك سحابي. المسموح: إجماليات/متوسطات،
/// أسماء منتجات عامة، ونطاقات زمنية واسعة. الممنوع: أسماء العملاء وهواتفهم
/// وعناوينهم ومعرّفات الفواتير وبيانات الموظفين والنسخة الكاملة من قاعدة البيانات
/// ونص السؤال غير المنقَّح.
/// </summary>
public static class QuestionRedactor
{
    private static readonly Regex PhonePattern = new(@"(?:\+?\d[\d\s\-]{8,}\d)", RegexOptions.Compiled);
    private static readonly Regex EmailPattern = new(@"@\w+\.\w+", RegexOptions.Compiled);
    private static readonly Regex InvoicePattern = new(@"\b[A-Za-z]{2,}[-\s]?\d{2,}[-\s]?\d+\b", RegexOptions.Compiled);
    private static readonly Regex DigitPattern = new(@"\d+", RegexOptions.Compiled);

    private static readonly string[] SalesMarkers = { "مبيعات", "بيع", "مبيع", "ايراد", "ايرادات" };
    private static readonly string[] DebtMarkers = { "ديون", "دين", "متاخر", "مستحق" };
    private static readonly string[] PriceMarkers = { "سعر", "اسعار", "ثمن", "شحال", "التمن" };
    private static readonly string[] ProductMarkers = { "منتج", "منتجات", "بضاعه", "بضايع", "مخزون" };
    private static readonly string[] AnalysisMarkers = { "حلل", "تحليل", "قارن", "متوسط", "فرق" };

    private static readonly string[] PersonalMarkers =
    {
        "اسم العميل", "اسم الزبون", "اسم الموظف", "رقم الهاتف", "رقم الجوال",
        "هاتف", "جوال", "العميل", "الزبون", "الموظف", "العامل"
    };

    private static readonly string[] AddressMarkers = { "العنوان", "عنوان", "شارع", "نهج", "الحي" };
    private static readonly string[] InvoiceMarkers = { "رقم الفاتوره", "فاتوره رقم", "رقم المستند", "فاتوره", "مستند", "وثيقه" };

    /// <summary>ينقّح السؤال إلى ملخص عام؛ النص الخام لا يُستخدم في الناتج إطلاقاً.</summary>
    public static RedactedQuestion Redact(string? question)
    {
        var normalized = TextNormalizer.Normalize(question);
        var removed = new List<string>();

        if (PhonePattern.IsMatch(normalized) || HasLongDigitRun(normalized)) removed.Add("أرقام هواتف ومعرّفات طويلة");
        if (EmailPattern.IsMatch(normalized)) removed.Add("بريد إلكتروني");
        if (InvoicePattern.IsMatch(normalized) || Has(normalized, InvoiceMarkers)) removed.Add("معرّفات فواتير");
        if (Has(normalized, PersonalMarkers)) removed.Add("أسماء عملاء وموظفين");
        if (Has(normalized, AddressMarkers)) removed.Add("عناوين");
        if (DigitPattern.IsMatch(normalized)) removed.Add("أرقام وتواريخ دقيقة");

        var category = Classify(normalized);
        var range = DetectRange(normalized);
        var summary = $"استفسار {category} — نطاق زمني واسع: {range}.";
        var safe = RedactionValidator.IsSafe(summary);

        return new RedactedQuestion(
            summary,
            removed.AsReadOnly(),
            safe,
            "تم التنقيح: ملخص مجهول بلا أسماء أو معرّفات أو أرقام دقيقة");
    }

    private static string Classify(string normalized)
    {
        if (Has(normalized, DebtMarkers)) return "ديون";
        if (Has(normalized, PriceMarkers)) return "أسعار";
        if (Has(normalized, ProductMarkers)) return "منتجات عامة";
        if (Has(normalized, AnalysisMarkers)) return "تحليل مجمّع";
        if (Has(normalized, SalesMarkers)) return "مبيعات مجمّعة";
        return "عام";
    }

    private static string DetectRange(string normalized)
    {
        if (normalized.Contains("اليوم", StringComparison.Ordinal)) return "اليوم";
        if (normalized.Contains("الاسبوع", StringComparison.Ordinal)) return "الأسبوع";
        if (normalized.Contains("الشهر", StringComparison.Ordinal)) return "الشهر";
        return "غير محدد";
    }

    private static bool HasLongDigitRun(string normalized)
    {
        var run = 0;
        foreach (var c in normalized)
        {
            if (char.IsDigit(c))
            {
                run++;
                if (run >= 9) return true;
            }
            else
            {
                run = 0;
            }
        }

        return false;
    }

    private static bool Has(string normalized, string[] markers)
        => markers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
}
