namespace Wizz.Presentation.Services;

using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Wizz.Application.AI.Perception;

/// <summary>
/// قارئ نصوص الصور باستخدام قرة التعرف المدمجة في Windows. يقرأ بالعربية أساسًا، ويستعين
/// بالفرنسية لتحسين الرموز الرقمية إن كانت حزمتها مثبّتة. ينتج نصًا خامًا فقط، ولا يعرف
/// المنتجات ولا الأسعار، ولا يكتب ولا يرسل أي شيء للخارج.
/// </summary>
public sealed class WindowsOcrTextReader : IOcrTextReader
{
    private static readonly Language ArabicLanguage = new("ar");
    private static readonly Language FrenchLanguage = new("fr");

    /// <summary>تُفحص اللغة وقت التشغيل؛ لا يُفترض وجود حزمة عربية مسبقًا.</summary>
    public bool IsSupported => OcrEngine.IsLanguageSupported(ArabicLanguage);

    /// <summary>يفكّ الصورة محليًا ويستخرج النص الخام، أو يُعلن عدم توفر القارئ العربية.</summary>
    public async Task<OcrReadResult> ReadAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            return OcrReadResult.Unavailable("لم تُرفع صورة.", OcrReaderNames.Windows);
        }

        if (!IsSupported)
        {
            return OcrReadResult.Unavailable(
                "قرة التعرف العربية غير مثبّتة على هذا الجهاز؛ استخدم الإدخال اليدوي.",
                OcrReaderNames.Windows);
        }

        var arabicEngine = OcrEngine.TryCreateFromLanguage(ArabicLanguage);
        if (arabicEngine is null)
        {
            return OcrReadResult.Unavailable(
                "تعذّر تشغيل قرة التعرف على هذا الجهاز؛ استخدم الإدخال اليدوي.",
                OcrReaderNames.Windows);
        }

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            var writer = new DataWriter(stream);
            writer.WriteBytes(imageBytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
            stream.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await DecodeWithOptionalUpscaleAsync(decoder);

            var arabic = await arabicEngine.RecognizeAsync(bitmap);
            var primaryText = ReconstructRows(arabic);

            var frenchEngine = OcrEngine.IsLanguageSupported(FrenchLanguage)
                ? OcrEngine.TryCreateFromLanguage(FrenchLanguage)
                : null;

            if (frenchEngine is null)
            {
                return OcrReadResult.Success(primaryText, OcrReadingMerger.PrimaryLanguageTag, OcrReaderNames.Windows);
            }

            // فشل القراءة المساعدة لا يُسقط القراءة الأساسية؛ تُستخدم العربية وحدها عندئذ.
            try
            {
                var french = await frenchEngine.RecognizeAsync(bitmap);
                var merged = OcrReadingMerger.Merge(primaryText, ReconstructRows(french));
                return OcrReadResult.Success(merged, OcrReadingMerger.MergedLanguageTag, OcrReaderNames.Windows);
            }
            catch
            {
                return OcrReadResult.Success(primaryText, OcrReadingMerger.PrimaryLanguageTag, OcrReaderNames.Windows);
            }
        }
        catch (Exception ex)
        {
            return OcrReadResult.Unavailable($"تعذّت قراءة الصورة محليًا: {ex.Message}", OcrReaderNames.Windows);
        }
    }

    /// <summary>
    /// يفكّ الصورة مع احترام اتجاه EXIF، ويكبّر الصور الصغيرة جدًا فقط بجودة عالية.
    /// صور الهاتف تُخزَّن غالبًا باتجاه مستشعر مختلف عن اتجاه عرضها؛ تجاهل وسمة EXIF كان يجعل
    /// النص يصل إلى قرة التعرّف مقلوبًا، فتتدهور العربية خصوصًا. الأبعاد <em>الموجّهة</em> هي أساس
    /// حساب التكبير حتى تبقى الأرقام صحيحة بعد الدوران.
    /// </summary>
    private static async Task<SoftwareBitmap> DecodeWithOptionalUpscaleAsync(BitmapDecoder decoder)
    {
        var target = OcrImagePreprocessor.ComputeTargetSize(
            decoder.OrientedPixelWidth,
            decoder.OrientedPixelHeight);

        if (target.Scale <= 1.0)
        {
            // نستدعي الحمولة الصريحة لأن الحمولة بلا معاملات لا تضمن اتجاه EXIF نفسه في كل إصدار.
            return await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.DoNotColorManage);
        }

        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)target.Width,
            ScaledHeight = (uint)target.Height,
            InterpolationMode = BitmapInterpolationMode.Cubic
        };

        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);
    }

    /// <summary>
    /// يجمع الكلمات ذات المستوى الأفقي القريب في سطر واحد، ويرتّبها من اليمين إلى اليسار،
    /// ثم يفصل الأسطر بفاصل صريح. لا يضيف نصًا ولا يغيّر الكلمات، بل يستعيد بنية الصفحة فقط.
    /// </summary>
    private static string ReconstructRows(OcrResult result)
    {
        var words = result.Lines
            .SelectMany(line => line.Words)
            .Select(word => new OcrWordBox(
                word.Text,
                (float)word.BoundingRect.X,
                (float)(word.BoundingRect.Y + word.BoundingRect.Height / 2),
                (float)word.BoundingRect.Height))
            .ToList();

        return OcrRowReconstructor.Reconstruct(words);
    }
}
