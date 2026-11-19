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

            // على بعض إصدارات Windows لا يضع OcrResult.Text فواصل بين الأسطر،
            // فيُدمج الوصل كله في سطر واحد. نعيد بناء الأسطر من مواضع الكلمات محليًا.
            return OcrReadResult.Success(ReconstructRows(recognized), "ar");
        }
        catch (Exception ex)
        {
            return OcrReadResult.Unavailable($"تعذّت قراءة الصورة محليًا: {ex.Message}");
        }
    }

    /// <summary>
    /// يجمع الكلمات ذات المستوى الأفقي القريب في سطر واحد، ويرتّبها من اليمين إلى اليسار،
    /// ثم يفصل الأسطر بفاصل صريح. لا يضيف نصًا ولا يغيّر الكلمات، بل يستعيد بنية الصفحة فقط.
    /// </summary>
    private static string ReconstructRows(OcrResult result)
    {
        var words = result.Lines
            .SelectMany(line => line.Words)
            .Select(word => new WordBox(
                word.Text,
                (float)word.BoundingRect.X,
                (float)(word.BoundingRect.Y + word.BoundingRect.Height / 2)))
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .OrderBy(word => word.CenterY)
            .ThenByDescending(word => word.Left)
            .ToList();

        if (words.Count == 0) return string.Empty;

        var rows = new List<string>();
        var current = new List<WordBox> { words[0] };
        var rowY = words[0].CenterY;

        void Flush()
        {
            if (current.Count == 0) return;
            rows.Add(string.Join(' ', current.Select(word => word.Text)));
            current = new List<WordBox>();
        }

        foreach (var word in words.Skip(1))
        {
            if (Math.Abs(word.CenterY - rowY) > 16f)
            {
                Flush();
                rowY = word.CenterY;
            }
            current.Add(word);
        }
        Flush();

        return string.Join('\n', rows);
    }

    private sealed record WordBox(string Text, float Left, float CenterY);
}
