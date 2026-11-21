namespace Wizz.Application.AI.Perception;

/// <summary>
/// يحصر المسار السحابي بالنماذج المجانية فقط، التزامًا بالقاعدة المعتمدة «خدمات مجانية فقط / صفر تكلفة».
/// يطبّق قاعدة قابلة للفحص في الكود بدل الاعتماد على وصف النموذج في وثيقة الإعدادات.
/// </summary>
public static class FreeCloudModelGuard
{
    /// <summary>اللاحقة التي تميّز النماذج المجانية في مزوّد OpenRouter.</summary>
    public const string FreeSuffix = ":free";

    public const string BlockedMessage =
        "المسار السحابي مسموح به للنماذج المجانية فقط. اضبط اسم نموذج رؤية مجاني ينتهي بـ :free قبل الاستخدام.";

    /// <summary>هل اسم النموذج مجاني بحسب قاعدة قابلة للتحقق؟</summary>
    public static bool IsFree(string? model)
        => !string.IsNullOrWhiteSpace(model)
           && model.Trim().EndsWith(FreeSuffix, StringComparison.OrdinalIgnoreCase);
}
