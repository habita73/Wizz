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

    /// <summary>
    /// أبعاد الصورة المستهدفة قبل التعرّف، محسوبة من الأبعاد <em>الموجّهة</em> (بعد تطبيق دوران EXIF).
    /// فصل هذا الحساب عن فكّ الصورة يجعل سلوك التكبير قابلًا للاختبار ببيانات اصطناعية، ويمنع حساب
    /// عامل التكبير من أبعاد خاطئة حين تكون الصورة مخزّنة باتجاه مختلف عن اتجاه عرضها.
    /// </summary>
    /// <param name="orientedWidth">عرض الصورة بعد تطبيق اتجاه EXIF.</param>
    /// <param name="orientedHeight">ارتفاع الصورة بعد تطبيق اتجاه EXIF.</param>
    public static OcrTargetSize ComputeTargetSize(long orientedWidth, long orientedHeight)
    {
        var scale = ComputeScale(orientedWidth, orientedHeight);
        if (orientedWidth <= 0 || orientedHeight <= 0 || scale <= 1.0)
        {
            return new OcrTargetSize(
                (int)Math.Max(0, orientedWidth),
                (int)Math.Max(0, orientedHeight),
                1.0);
        }

        var width = (int)Math.Min(MaximumLongSidePixels, Math.Round(orientedWidth * scale));
        var height = (int)Math.Min(MaximumLongSidePixels, Math.Round(orientedHeight * scale));
        return new OcrTargetSize(width, height, scale);
    }
}

/// <summary>أبعاد الصورة بعد التكبير الاختياري، والعامل المستخدم.</summary>
/// <param name="Width">العرض المستهدف بالبكسل.</param>
/// <param name="Height">الارتفاع المستهدف بالبكسل.</param>
/// <param name="Scale">عامل التكبير؛ <c>1</c> يعني بلا تغيير.</param>
public readonly record struct OcrTargetSize(int Width, int Height, double Scale);
