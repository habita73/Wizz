namespace Wizz.Application.Abstractions;

/// <summary>
/// تخصيصات التقاط الصورة في شاشة تحكيم النص. تُحفظ محليًا، بلا قاعدة بيانات ولا إرسال،
/// ولا تؤثر في القارئ الافتراضي ولا في مسار الفواتير أو المخزون.
/// </summary>
/// <param name="EnhanceEnabled">تفعيل تحسين الصورة محليًا قبل القراءة.</param>
/// <param name="PreviewBeforeAnalysis">إظهار معاينة الصورة وانتظار أمر التحليل.</param>
/// <param name="ShowEnhancedPreview">إظهار النسخة المحسّنة بجانب الأصلية للمقارنة.</param>
public sealed record ImageCaptureSettings(
    bool EnhanceEnabled,
    bool PreviewBeforeAnalysis,
    bool ShowEnhancedPreview)
{
    /// <summary>الافتراض: بلا تحسين، مع معاينة قبل التحليل، ومعاينة محسّنة عند تفعيل التحسين.</summary>
    public static ImageCaptureSettings Default { get; } = new(false, true, true);
}

/// <summary>حفظ/قراءة تخصيصات التقاط الصورة في ملف محلي داخل بيانات المستخدم.</summary>
public interface IImageCaptureSettingsStore
{
    ImageCaptureSettings Get();

    void Save(ImageCaptureSettings settings);
}
