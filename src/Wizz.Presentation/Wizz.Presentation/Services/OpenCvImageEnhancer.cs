namespace Wizz.Presentation.Services;

using OpenCvSharp;
using Wizz.Application.AI.Perception;

/// <summary>
/// تحسين اختياري للصورة داخل الجهاز بـOpenCV، بلا نماذج ولا اتصال. يطبّق CLAHE على قناة
/// الإضاءة في فضاء LAB فقط، فيرفع التباين المحلي دون تلوين الصورة أو تغيير أبعادها؛ التكبير
/// إن لزم يتم لاحقًا في قارئ Windows على الصورة المُحسَّنة.
/// يفكّOpenCV الصورة بالوضع الافتراضي الذي يحترم وسمة اتجاه EXIF (وثائق IMREAD_IGNORE_ORIENTATION)،
/// فيصبح الاتجاه مدفونًا في البكسلات عند إعادة الترميز ولا يحتاج القارئ إلى وسمة EXIF.
/// </summary>
public sealed class OpenCvImageEnhancer : IImageEnhancer
{
    private const double ContrastClipLimit = 2.0;
    private const int ThumbnailJpegQuality = 75;

    /// <summary>يعيد صورة PNG محسّنة، أو <c>null</c> عند تعذّر فكّ الصورة أو تحسينها.</summary>
    public byte[]? Enhance(byte[] imageBytes)
    {
        if (imageBytes is null || imageBytes.Length == 0) return null;

        try
        {
            using var source = Mat.FromImageData(imageBytes, ImreadModes.Color);
            if (source.Empty()) return null;

            using var lab = new Mat();
            Cv2.CvtColor(source, lab, ColorConversionCodes.BGR2Lab);

            var channels = Cv2.Split(lab);
            try
            {
                using var clahe = CLAHE.Create(ContrastClipLimit, new Size(8, 8));
                var enhancedLight = new Mat();
                clahe.Apply(channels[0], enhancedLight);
                channels[0].Dispose();
                channels[0] = enhancedLight;

                using var merged = new Mat();
                Cv2.Merge(channels, merged);
                using var bgr = new Mat();
                Cv2.CvtColor(merged, bgr, ColorConversionCodes.Lab2BGR);

                var encoded = bgr.ImEncode(".png");
                return encoded.Length > 0 ? encoded : null;
            }
            finally
            {
                foreach (var channel in channels) channel.Dispose();
            }
        }
        catch
        {
            // فشل التحسين لا يُسقط القراءة؛ يعود الواجهة بالصورة الأصلية.
            return null;
        }
    }

    /// <summary>يعيد نسخة JPEG مصغّرة للمعاينة فقط، أو <c>null</c> عند تعذّر فكّ الصورة.</summary>
    public byte[]? CreateThumbnail(byte[] imageBytes, int maxDimension)
    {
        if (imageBytes is null || imageBytes.Length == 0 || maxDimension <= 0) return null;

        try
        {
            using var source = Mat.FromImageData(imageBytes, ImreadModes.Color);
            if (source.Empty()) return null;

            var scale = Math.Min(1.0, (double)maxDimension / Math.Max(source.Width, source.Height));
            using var thumbnail = new Mat();
            if (scale < 1.0)
            {
                Cv2.Resize(source, thumbnail, new Size(), scale, scale, InterpolationFlags.Area);
            }
            else
            {
                source.CopyTo(thumbnail);
            }

            var encoded = thumbnail.ImEncode(
                ".jpg",
                new ImageEncodingParam(ImwriteFlags.JpegQuality, ThumbnailJpegQuality));
            return encoded.Length > 0 ? encoded : null;
        }
        catch
        {
            return null;
        }
    }
}
