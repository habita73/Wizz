namespace Wizz.Application.AI.Perception;

/// <summary>نتيجة التقاط صورة: حالة القارئ، النص الخام، وحكم المدمج إن وُجد.</summary>
/// <param name="ReaderAvailable">هل كان القارئ متاحًا وأنتاج نصًا؟</param>
/// <param name="RecognizedText">النص الخام الذي استخرجه القارئ للعرض.</param>
/// <param name="UnavailableReason">سبب عدم توفر القارئ، عند عدم التوفر فقط.</param>
/// <param name="Adjudication">حكم المدمج على مستخرجات النص، أو <c>null</c> عند عدم التوفر.</param>
/// <param name="ReaderName">اسم القارئ الذي أنتج النص، ليعرف المستخدم أي محرك اشتغل.</param>
public sealed record OcrDocumentCaptureResult(
    bool ReaderAvailable,
    string RecognizedText,
    string? UnavailableReason,
    DocumentCaptureResult? Adjudication,
    string ReaderName = "")
{
    /// <summary>يبقى <c>false</c> دائمًا في هذه المرحلة.</summary>
    public bool WrittenToDatabase => Adjudication?.WrittenToDatabase ?? false;
}

/// <summary>
/// يركّب قارئ النص مع طبقة الإدخال الحالية: يستخرج نصًا خامًا محليًا، ثم يمرّره إلى
/// <see cref="DocumentCaptureService"/> ليحكّمه المدمج قبل أي عرض. لا يكتب، لا يرسل، ولا يغيّر التوجيه.
/// </summary>
public sealed class OcrDocumentCaptureService
{
    /// <summary>أقصى حجم صورة مقبول؛ حماية للذاكرة والواجهة.</summary>
    public const long MaxImageBytes = 10L * 1024 * 1024;

    private readonly IOcrTextReader _reader;
    private readonly DocumentCaptureService _capture;

    public OcrDocumentCaptureService(IOcrTextReader reader, DocumentCaptureService capture)
    {
        _reader = reader;
        _capture = capture;
    }

    /// <summary>يقرأ الصورة ثم يحكّم نصها عبر المدمج، أو يُعلن عدم توفر القارئ.</summary>
    public async Task<OcrDocumentCaptureResult> CaptureAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (imageBytes is null || imageBytes.Length == 0)
        {
            return new OcrDocumentCaptureResult(false, string.Empty, "لم تُرفع صورة.", null);
        }

        if (imageBytes.Length > MaxImageBytes)
        {
            return new OcrDocumentCaptureResult(
                false, string.Empty, "حجم الصورة أكبر من 10 ميغابايت.", null);
        }

        var read = await _reader.ReadAsync(imageBytes, ct);
        if (read is null || !read.IsAvailable)
        {
            return new OcrDocumentCaptureResult(
                false,
                string.Empty,
                read?.UnavailableReason ?? "قارئ النص غير متاح على هذا الجهاز؛ استخدم الإدخال اليدوي.",
                null);
        }

        var adjudication = await _capture.CaptureAsync(
            new DocumentCaptureRequest(read.Text, PerceptionSource.Local),
            ct);

        return new OcrDocumentCaptureResult(true, read.Text, null, adjudication, read.ReaderName);
    }
}
