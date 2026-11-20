namespace Wizz.Presentation.Services;

using RapidOcrNet;
using SkiaSharp;
using Wizz.Application.AI.Perception;

/// <summary>
/// قارئ نصوص محلي مضمّن يعمل على أي Windows دون حزمة لغة نظام. يعتمد على نماذج PP-OCR رسمية
/// مجانية: كشف النصوص وتصحيح الاتجاه من PP-OCRv5، والتعرّف العربي من نموذج
/// <c>arabic_PP-OCRv4_rec_mobile</c>؛ اختير إصدار التعرّف الرابع لأن اختبارًا اصطناعيًا أظهر أن
/// إصدار الخامس يعكس ترتيب الحروف العربية على صورة غير متأثرة بضجيج. يُستخدم بديلًا أخيرًا بعد
/// قرة Windows؛ لا يعرف المنتجات، ولا يكتب ولا يرسل أي شيء، ولا يسجّل نص الوصل في أي سجل.
/// </summary>
public sealed class RapidOcrArabicTextReader : IOcrTextReader
{
    private const string ModelsFolderName = "models";
    private const string DetModel = "ch_PP-OCRv5_det_mobile.onnx";
    private const string ClsModel = "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx";
    private const string RecModel = "arabic_PP-OCRv4_rec_mobile.onnx";
    private const string KeysFile = "arabic_dict.txt";

    private readonly string _modelsPath;

    public RapidOcrArabicTextReader()
        : this(Path.Combine(AppContext.BaseDirectory, ModelsFolderName, "ocr", "arabic"))
    {
    }

    public RapidOcrArabicTextReader(string modelsPath)
    {
        if (string.IsNullOrWhiteSpace(modelsPath))
            throw new ArgumentException("مسار نماذج القارئ مطلوب", nameof(modelsPath));

        _modelsPath = modelsPath;
    }

    /// <summary>يتوفر القارئ فقط حين تكون النماذج الأربعة مضمّنة فعلًا في مخرجات التطبيق.</summary>
    public bool IsSupported =>
        File.Exists(Path.Combine(_modelsPath, DetModel)) &&
        File.Exists(Path.Combine(_modelsPath, ClsModel)) &&
        File.Exists(Path.Combine(_modelsPath, RecModel)) &&
        File.Exists(Path.Combine(_modelsPath, KeysFile));

    public async Task<OcrReadResult> ReadAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (imageBytes is null || imageBytes.Length == 0)
            return OcrReadResult.Unavailable("لم تُرفع صورة.");

        if (!IsSupported)
            return OcrReadResult.Unavailable(
                "نماذج القارئ العربي المضمّنة غير موجودة؛ استخدم الإدخال اليدوي.");

        try
        {
            using var ocr = new RapidOcr();
            ocr.InitModels(
                Path.Combine(_modelsPath, DetModel),
                Path.Combine(_modelsPath, ClsModel),
                Path.Combine(_modelsPath, RecModel),
                Path.Combine(_modelsPath, KeysFile),
                RapidOcr.GetDefaultSessionOptions());

            using var stream = new MemoryStream(imageBytes, writable: false);
            using var bitmap = SKBitmap.Decode(stream);
            if (bitmap is null)
                return OcrReadResult.Unavailable("لم يمكن فك ترميز الصورة محليًا.");

            var result = await ocr.DetectAsync(bitmap, RapidOcrOptions.Default, null, ct);
            var lines = (result?.TextBlocks ?? Array.Empty<TextBlock>())
                .Select(block => block.Text?.Trim() ?? string.Empty)
                .Where(line => line.Length > 0);

            return OcrReadResult.Success(
                string.Join(Environment.NewLine, lines),
                OcrReadingMerger.PrimaryLanguageTag);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return OcrReadResult.Unavailable("أُلغيت قراءة الصورة.");
        }
        catch (Exception ex)
        {
            // الرسالة تصف فشل المحرك فقط، ولا تحتوي أي جزء من نص الوصل.
            return OcrReadResult.Unavailable($"تعذّت قراءة الصورة محليًا: {ex.Message}");
        }
    }
}
