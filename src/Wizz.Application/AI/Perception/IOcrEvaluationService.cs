namespace Wizz.Application.AI.Perception;

/// <summary>عينة اصطناعية لتقييم استخراج النص، بلا بيانات فاتورة أو عميل حقيقية.</summary>
/// <param name="Id">معرّف العينة في التقرير.</param>
/// <param name="Text">النص المقروء المتوقّع.</param>
/// <param name="ExpectedProduct">اسم المنتج المتوقّع.</param>
/// <param name="ExpectedPrice">السعر المتوقّع، أو <c>null</c> إن لم يُكن مطلوبًا.</param>
public sealed record OcrEvaluationSample(
    string Id,
    string Text,
    string ExpectedProduct,
    decimal? ExpectedPrice);

/// <summary>نتيجة قياس الاستخراج على العينات المحلية.</summary>
/// <param name="Total">عدد العينات.</param>
/// <param name="NameHits">عينات تطابق فيها اسم المنتج.</param>
/// <param name="PriceHits">عينات تطابق فيها السعر.</param>
/// <param name="Failures">وصف العينات التي لم تتطابق.</param>
public sealed record OcrEvaluationResult(
    int Total,
    int NameHits,
    int PriceHits,
    IReadOnlyList<string> Failures);

/// <summary>مخزن عينات التقييم المحلية؛ يُهيّأ بعينات اصطناعية وقابلة للتعديل يدويًا.</summary>
public interface IOcrEvaluationSampleStore
{
    IReadOnlyList<OcrEvaluationSample> GetAll();
}

/// <summary>
/// قياس محلي لدقة استخراج الأسماء والأسعار على عينات اصطناعية. لا يدرّب نموذجًا،
/// ولا يكتب في قاعدة البيانات، ولا يرسل أي شيء للخارج.
/// </summary>
public interface IOcrEvaluationService
{
    OcrEvaluationResult Run();
}
