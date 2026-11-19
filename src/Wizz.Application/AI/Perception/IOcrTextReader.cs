namespace Wizz.Application.AI.Perception;

/// <summary>نتيجة قراءة نص من صورة: إمّا نص خام، أو إعلان واضح أن القارئ غير متاح على هذه المنصة.</summary>
/// <param name="IsAvailable">هل أنتج القارئ نصًا قابلًا للتحكيم؟</param>
/// <param name="Text">النص الخام المستخرج محليًا، أو سلسلة فارغة عند عدم التوفر.</param>
/// <param name="LanguageTag">وسم اللغة التي استخدمها القارئ، إن وُجد.</param>
/// <param name="UnavailableReason">سبب عدم التوفر بلغة المستخدم، عند عدم التوفر فقط.</param>
public sealed record OcrReadResult(
    bool IsAvailable,
    string Text,
    string? LanguageTag,
    string? UnavailableReason)
{
    /// <summary>ينشئ نتيجة ناجحة بالنص الخام.</summary>
    public static OcrReadResult Success(string text, string? languageTag) =>
        new(true, text, languageTag, null);

    /// <summary>ينشئ نتيجة تعلن أن القارئ غير متاح دون أي نص.</summary>
    public static OcrReadResult Unavailable(string reason) =>
        new(false, string.Empty, null, reason);
}

/// <summary>
/// عقد قارئ نصوص الصور. يعرف استخراج النص الخام فقط، ولا يعرف المنتجات ولا الأسعار النهائية،
/// ولا يكتب في قاعدة البيانات ولا يرسل أي شيء للخارج. المنصات التي لا تدعم القراءة تُعلن ذلك صراحةً.
/// </summary>
public interface IOcrTextReader
{
    /// <summary>هل يمكن لهذا القارئ العمل على الجهاز الحالي؟</summary>
    bool IsSupported { get; }

    /// <summary>يقرأ النص الخام من صورة محلية، أو يُعلن عدم التوفر.</summary>
    Task<OcrReadResult> ReadAsync(byte[] imageBytes, CancellationToken ct = default);
}
