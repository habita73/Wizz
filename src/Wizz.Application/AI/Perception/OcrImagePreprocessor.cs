namespace Wizz.Application.AI.Perception;

/// <summary>
/// يقرّر تحسينًا بسيطًا لصورة الإدخال قبل التعرّف: تكبير الصور الصغيرة جدًا فقط، دون لمس الصور
/// الكافية. قرار حسابي صرف بلا أي مكتبة أو نموذج، والنتيجة عامل تكبير <c>1</c> يعني «لا تغيير».
/// </summary>
public static class OcrImagePreprocessor
{
    /// <summary>أقل طول للضلع القصير قبل اعتبار الصورة صغيرة تحتاج تكبيرًا.</summary>
    public const int MinimumShortSidePixels = 960;

    /// <summary>أقصى عامل تكبير؛ يمنع تضخيم صورة صغيرة إلى حجم غير عملي.</summary>
    public const double MaximumUpscaleFactor = 2.5;

    /// <summary>أقصى طول للضلع الطويل بعد التكبير؛ حماية للذاكرة والزمن.</summary>
    public const int MaximumLongSidePixels = 4096;

    /// <summary>
    /// يحسب عامل التكبير من أبعاد الصورة كما فكّها القارئ. الصورة الكافية تعود بعامل <c>1</c>،
    /// والصورة الصغيرة تُكبَّر بمقدار محسوب ومقيّد بالحدود أعلاه.
    /// </summary>
    public static double ComputeScale(long pixelWidth, long pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0) return 1.0;

        var shortSide = Math.Min(pixelWidth, pixelHeight);
        var longSide = Math.Max(pixelWidth, pixelHeight);
        if (shortSide >= MinimumShortSidePixels) return 1.0;

        var scale = (double)MinimumShortSidePixels / shortSide;
        scale = Math.Min(scale, MaximumUpscaleFactor);
        scale = Math.Min(scale, (double)MaximumLongSidePixels / longSide);

        return Math.Max(1.0, scale);
    }
}
