namespace Wizz.Application.AI.Perception;

/// <summary>
/// شدة تحسين الصورة داخل الجهاز. كل المستويات تستخدم العملية نفسها (رفع التباين المحلي على
/// قناة الإضاءة) وتختلف في شدتها فقط؛ لا عمليات إضافية ولا نماذج ولا اتصال.
/// </summary>
public enum ImageEnhanceLevel
{
    Light = 1,
    Medium = 2,
    Strong = 3
}

/// <summary>قيم الشدة والتسمية العربية؛ تُستخدم في التحسين وفي شاشة التخصيص.</summary>
public static class ImageEnhanceLevels
{
    /// <summary>شدة خفيفة: رفع تباين أقل من الإعداد السابق.</summary>
    public const double LightClipLimit = 1.0;

    /// <summary>شدة متوسطة: بين الخفيف والإعداد السابق.</summary>
    public const double MediumClipLimit = 1.5;

    /// <summary>شدة قوية: القيمة المستخدمة قبل إضافة المستويات.</summary>
    public const double StrongClipLimit = 2.0;

    /// <summary>قيمة رفع التباين المقابلة للمستوى؛ أي قيمة غير معروفة تعود للإعداد السابق.</summary>
    public static double ClipLimit(ImageEnhanceLevel level) => level switch
    {
        ImageEnhanceLevel.Light => LightClipLimit,
        ImageEnhanceLevel.Medium => MediumClipLimit,
        ImageEnhanceLevel.Strong => StrongClipLimit,
        _ => StrongClipLimit
    };

    /// <summary>التسمية المعروضة للمستخدم لهذا المستوى.</summary>
    public static string Label(ImageEnhanceLevel level) => level switch
    {
        ImageEnhanceLevel.Light => "خفيف",
        ImageEnhanceLevel.Medium => "متوسط",
        ImageEnhanceLevel.Strong => "قوي",
        _ => Label(ImageEnhanceLevel.Strong)
    };
}
