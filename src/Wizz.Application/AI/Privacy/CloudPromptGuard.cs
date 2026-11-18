namespace Wizz.Application.AI.Privacy;

/// <summary>
/// فحص إلزامي قبل أي طلب HTTP سحابي — طبقة دفاع إضافية مستقلة عن تنقيح الموجّه.
/// يرفض أي نص يحمل بيانات عملاء أو معرّفات أو عناوين أو نسخة كاملة من البيانات،
/// حتى لو أخطأ مسار أعلى في بناء الطلب.
/// </summary>
public static class CloudPromptGuard
{
    /// <summary>رسالة عربية مفهومة تُعرض عند منع الإرسال.</summary>
    public const string BlockedMessage =
        "تعذّر الإرسال: يحتوي الطلب على بيانات غير مسموح بتشاركها. راجع إعدادات الخصوصية.";

    /// <summary>هل كل النصوص الممرّرة آمنة للإرسال؟</summary>
    public static bool IsSafeToSend(params string?[] prompts)
        => prompts.All(prompt => RedactionValidator.IsSafe(prompt));
}
