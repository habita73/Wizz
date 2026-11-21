namespace Wizz.Application.AI.Perception;

/// <summary>مسار النموذج المستخدم لاستخراج النص من صورة الورقة.</summary>
public enum DocumentModelRoute
{
    /// <summary>نموذج محلي عبر Ollama؛ الصورة لا تخرج من الجهاز.</summary>
    Local = 0,

    /// <summary>نموذج سحابي مجاني فقط؛ الصورة تُرسل بعد موافقة صريحة.</summary>
    Cloud = 1
}

/// <summary>نتيجة استخراج النص من الصورة عبر نموذج.</summary>
/// <param name="Text">النص المستخرج كما أعاده النموذج، للمراجعة البشرية فقط.</param>
/// <param name="Route">المسار الذي استُخدم.</param>
/// <param name="Model">اسم النموذج الذي استُخدم.</param>
/// <param name="Notes">ملاحظات غير حاسمة للعرض، مثل تحذير خروج الصورة من الجهاز.</param>
public sealed record DocumentExtractionResult(
    string Text,
    DocumentModelRoute Route,
    string Model,
    IReadOnlyList<string> Notes);

/// <summary>
/// استخراج نص عربي مطبوع من صورة ورقة عبر نموذج، عند طلب المستخدم صراحةً وبكل مرة.
/// ليس جزءًا من مسار القراءة الروتيني، ولا يكتب في قاعدة البيانات، ولا يطبّق تلقائيًا.
/// </summary>
public interface IModelDocumentExtractor
{
    Task<DocumentExtractionResult> ExtractAsync(
        byte[] imageBytes,
        DocumentModelRoute route,
        CancellationToken ct = default);
}
