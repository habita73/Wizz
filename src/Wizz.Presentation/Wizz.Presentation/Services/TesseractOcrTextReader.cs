namespace Wizz.Presentation.Services;

using Tesseract;
using Wizz.Application.AI.Perception;

/// <summary>
/// قارئ نصوص محلي مضمّن يعمل على أي Windows دون حزمة لغة نظام. يقرأ العربية أساسًا ويستعين
/// بالفرنسية لتحسين الرموز الرقمية، ثم يمرّر النص عبر دمج القراءتين المعتمد. لا يعرف المنتجات،
/// ولا يكتب ولا يرسل أي شيء، ولا يسجّل نص الوصل في أي سجل.
/// دقته على صور الوصول أقل من قرة Windows، ولذلك يُستخدم بديلًا أخيرًا لا قارئًا أساسيًا.
/// </summary>
public sealed class TesseractOcrTextReader : IOcrTextReader
{
    private const string TessdataFolderName = "tessdata";
    private const string PrimaryLanguage = "ara";
    private const string SecondaryLanguage = "eng";

    private readonly string _tessdataPath;

    public TesseractOcrTextReader()
        : this(Path.Combine(AppContext.BaseDirectory, TessdataFolderName))
    {
    }

    public TesseractOcrTextReader(string tessdataPath)
    {
        if (string.IsNullOrWhiteSpace(tessdataPath))
            throw new ArgumentException("مسار بيانات اللغة مطلوب", nameof(tessdataPath));

        _tessdataPath = tessdataPath;
    }

    /// <summary>يتوفر القارئ فقط حين تكون بيانات اللغتين مضمّنة فعلًا في مخرجات التطبيق.</summary>
    public bool IsSupported =>
        File.Exists(Path.Combine(_tessdataPath, "ara.traineddata")) &&
        File.Exists(Path.Combine(_tessdataPath, "eng.traineddata"));

    public Task<OcrReadResult> ReadAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (imageBytes is null || imageBytes.Length == 0)
            return Task.FromResult(OcrReadResult.Unavailable("لم تُرفع صورة."));

        if (!IsSupported)
            return Task.FromResult(OcrReadResult.Unavailable(
                "بيانات اللغة المضمّنة غير موجودة؛ استخدم الإدخال اليدوي."));

        try
        {
            using var image = Pix.LoadFromMemory(imageBytes);
            var scale = OcrImagePreprocessor.ComputeScale(image.Width, image.Height);
            using var scaled = scale > 1.0 ? image.Scale((float)scale, (float)scale) : null;
            var prepared = scaled ?? image;

            var primaryText = ReadLanguage(prepared, PrimaryLanguage);
            var secondaryText = ReadLanguage(prepared, SecondaryLanguage);
            var merged = OcrReadingMerger.Merge(primaryText, secondaryText);
            var languageTag = string.IsNullOrWhiteSpace(secondaryText)
                ? OcrReadingMerger.PrimaryLanguageTag
                : OcrReadingMerger.MergedLanguageTag;

            return Task.FromResult(OcrReadResult.Success(merged, languageTag));
        }
        catch (Exception ex)
        {
            // الرسالة تصف فشل المحرك فقط، ولا تحتوي أي جزء من نص الوصل.
            return Task.FromResult(OcrReadResult.Unavailable($"تعذّت قراءة الصورة محليًا: {ex.Message}"));
        }
    }

    private string ReadLanguage(Pix image, string language)
    {
        using var engine = new TesseractEngine(_tessdataPath, language, EngineMode.Default);
        using var page = engine.Process(image);
        return page.GetText() ?? string.Empty;
    }
}
