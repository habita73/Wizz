namespace Wizz.Application.AI.Privacy;

using System.Text.RegularExpressions;

/// <summary>
/// يفحص أي نص قبل إرساله للخارج: يرفض بيانات العملاء والمعرّفات والنسخ الكاملة.
/// يُستخدم كفحص إلزامي داخل بوابة التشارك وفي مزوّد السحابة ك defenses إضافي.
/// </summary>
public static class RedactionValidator
{
    private static readonly Regex EmailPattern = new(@"@\w+\.\w+", RegexOptions.Compiled);
    private static readonly Regex InvoicePattern = new(@"\b[A-Za-z]{2,}[-\s]?\d{2,}[-\s]?\d+\b", RegexOptions.Compiled);

    // العلامات بصيغة مُطبَّعة (التاء المربوطة هاء، والهمزات موحّدة).
    private static readonly string[] PersonalMarkers =
    {
        "اسم العميل", "اسم الزبون", "اسم الموظف", "اسم العامل", "رقم الهاتف", "رقم الجوال",
        "هاتف", "جوال", "العميل", "الزبون", "الموظف", "العامل"
    };

    private static readonly string[] AddressMarkers =
    {
        "العنوان", "عنوان", "شارع", "نهج", "الحي", "المدينه"
    };

    private static readonly string[] DatabaseMarkers =
    {
        "قاعدة البيانات", "سجل المتجر", "كل البيانات", "نسخه كامله", "دump", "dump"
    };

    private static readonly string[] InvoiceMarkers =
    {
        "رقم الفاتوره", "فاتوره رقم", "رقم المستند", "مستند رقم", "رقم الوثيقه",
        "فاتوره", "مستند", "وثيقه"
    };

    /// <summary>هل النص آمن للإرسال؟</summary>
    public static bool IsSafe(string? text) => IsSafe(text, out _);

    /// <summary>يفحص النص ويعيد قائمة المخالفات المكتشفة (خالية يعني أنه آمن).</summary>
    public static bool IsSafe(string? text, out IReadOnlyList<string> violations)
    {
        var found = new List<string>();
        if (!string.IsNullOrWhiteSpace(text))
        {
            var normalized = TextNormalizer.Normalize(text);

            if (Has(normalized, PersonalMarkers)) found.Add("أسماء أو بيانات عملاء/موظفين");
            if (Has(normalized, AddressMarkers)) found.Add("عناوين");
            if (Has(normalized, InvoiceMarkers) || InvoicePattern.IsMatch(normalized)) found.Add("معرّفات فواتير أو مستندات");
            if (Has(normalized, DatabaseMarkers)) found.Add("نسخة كاملة من البيانات");
            if (EmailPattern.IsMatch(normalized)) found.Add("بريد إلكتروني");

            // سلسلة أرقام طويلة تشبه الهاتف أو رقم المستند لا تُرسل أبداً.
            var digitRun = 0;
            foreach (var c in normalized)
            {
                if (char.IsDigit(c))
                {
                    digitRun++;
                    if (digitRun >= 9)
                    {
                        found.Add("رقم هاتف أو معرّف طويل");
                        break;
                    }
                }
                else
                {
                    digitRun = 0;
                }
            }
        }

        violations = found;
        return found.Count == 0;
    }

    private static bool Has(string normalized, string[] markers)
        => markers.Any(marker => !string.IsNullOrEmpty(marker) && normalized.Contains(marker, StringComparison.Ordinal));
}
