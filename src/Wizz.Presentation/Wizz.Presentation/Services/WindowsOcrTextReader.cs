namespace Wizz.Presentation.Services;

using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using Wizz.Application.AI.Perception;

/// <summary>
/// قارئ نصوص الصور باستخدام قرة التعرف المدمجة في Windows. ينتج نصًا خامًا فقط،
/// ولا يعرف المنتجات ولا الأسعار، ولا يكتب ولا يرسل أي شيء للخارج.
/// </summary>
public sealed class WindowsOcrTextReader : IOcrTextReader
{
    private static readonly Language ArabicLanguage = new("ar");

    /// <summary>تُفحص اللغة وقت التشغيل؛ لا يُفترض وجود حزمة عربية مسبقًا.</summary>
    public bool IsSupported => OcrEngine.IsLanguageSupported(ArabicLanguage);

    /// <summary>يفكّ الصورة محليًا ويستخرج النص الخام، أو يُعلن عدم توفر القارئ العربية.</summary>
    public async Task<OcrReadResult> ReadAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            return OcrReadResult.Unavailable("لم تُرفع صورة.");
        }

        if (!IsSupported)
        {
            return OcrReadResult.Unavailable(
                "قرة التعرف العربية غير مثبّتة على هذا الجهاز؛ استخدم الإدخال اليدوي.");
        }

        var engine = OcrEngine.TryCreateFromLanguage(ArabicLanguage);
        if (engine is null)
        {
            return OcrReadResult.Unavailable("تعذّر تشغيل قرة التعرف على هذا الجهاز؛ استخدم الإدخال اليدوي.");
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
            using var bitmap = await decoder.GetSoftwareBitmapAsync();
            var recognized = await engine.RecognizeAsync(bitmap);

            return OcrReadResult.Success(recognized.Text, "ar");
        }
        catch (Exception ex)
        {
            return OcrReadResult.Unavailable($"تعذّت قراءة الصورة محليًا: {ex.Message}");
        }
    }
}
