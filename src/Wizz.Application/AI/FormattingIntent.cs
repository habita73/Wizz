namespace Wizz.Application.AI;

/// <summary>
/// يفرّق بين سؤال يطلب رقماً دقيقاً من المتجر وطلب يطلب صياغة أو تنسيقاً أو تقريراً.
/// طلبات التنسيق يجوز تفويضها إلى نموذج لغوي، بشرط بقاء تحكيم الأرقام محلياً.
/// </summary>
public static class FormattingIntent
{
    // العلامات بصيغة مُطبَّعة (انظر TextNormalizer): الهمزات تُوحَّد والتاء المربوطة هاء والتشكيل يُحذف.
    private static readonly string[] Markers =
    {
        "تقرير", "جدول", "مخطط", "رسم بياني", "تنسيق", "صياغه", "صيغ", "رتب", "نظم",
        "ترتيب", "بصيغه", "على شكل", "بشكل", "مستند", "قالب", "ملخص منسق",
        "لخص", "تلخيص", "انشئ", "اعمل تقرير"
    };

    /// <summary>هل طلب المستخدم يهدف إلى إخراج مُنسَّق لا إلى رقم دقيق؟</summary>
    public static bool IsFormattingRequest(string? question)
        => TextNormalizer.HasAny(TextNormalizer.Normalize(question), Markers);
}
