namespace Wizz.Application.Abstractions;

using Wizz.Application.AI.Perception;

/// <summary>
/// تخصيصات التقاط الصورة في شاشة تحكيم النص. تُحفظ محليًا، بلا قاعدة بيانات ولا إرسال،
/// ولا تؤثر في القارئ الافتراضي ولا في مسار الفواتير أو المخزون.
/// </summary>
/// <param name="EnhanceEnabled">تفعيل تحسين الصورة محليًا قبل القراءة.</param>
/// <param name="PreviewBeforeAnalysis">إظهار معاينة الصورة وانتظار أمر التحليل.</param>
/// <param name="ShowEnhancedPreview">إظهار النسخة المحسّنة بجانب الأصلية للمقارنة.</param>
/// <param name="EnhanceLevel">شدة التحسين حين يُفعّل؛ القوي هو الإعداد السابق.</param>
public sealed record ImageCaptureSettings(
    bool EnhanceEnabled,
    bool PreviewBeforeAnalysis,
    bool ShowEnhancedPreview,
    ImageEnhanceLevel EnhanceLevel)
{
    /// <summary>
    /// الافتراض: بلا تحسين، مع معاينة قبل التحليل، ومعاينة محسّنة عند تفعيل التحسين،
    /// وشدة «قوي» حتى لا يتغيّر سلوك من فعّل التحسين قبل إضافة المستويات.
    /// </summary>
    public static ImageCaptureSettings Default { get; } =
        new(false, true, true, ImageEnhanceLevel.Strong);
}

/// <summary>حفظ/قراءة تخصيصات التقاط الصورة في ملف محلي داخل بيانات المستخدم.</summary>
public interface IImageCaptureSettingsStore
{
    ImageCaptureSettings Get();

    void Save(ImageCaptureSettings settings);
}
