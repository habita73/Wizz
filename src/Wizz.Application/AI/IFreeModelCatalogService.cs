namespace Wizz.Application.AI;

/// <summary>نموذج مجاني كما ظهر في كتالوج عام — بيانات وصفية فقط، بلا سؤال أو صورة أو مفتاح.</summary>
/// <param name="ModelId">اسم النموذج في الكتالوج.</param>
/// <param name="DisplayName">الاسم المعروض.</param>
/// <param name="ContextLength">طول السياق المعلن.</param>
/// <param name="SupportsImage">هل يقبل صورة.</param>
/// <param name="ObservedAt">وقت رصد النموذج في الرد.</param>
public sealed record FreeModelSuggestion(
    string ModelId,
    string DisplayName,
    long ContextLength,
    bool SupportsImage,
    DateTimeOffset ObservedAt);

/// <summary>
/// لقطة من الكتالوج العام في وقت محدد. تُحفظ محليًا، ولا تحتوي أي بيانات متجر أو عميل.
/// الترتيب داخل القوائم تخمين بحسب بيانات الكتالوج (مجاني، غير منتهٍ، يدعم المطلوب)،
/// وليس قياسًا لدقة العربية — تلك بوابة `NEEDS-VALIDATION` منفصلة.
/// </summary>
public sealed record FreeModelCatalogSnapshot(
    DateTimeOffset FetchedAt,
    string Source,
    IReadOnlyList<FreeModelSuggestion> ChatCandidates,
    IReadOnlyList<FreeModelSuggestion> VisionCandidates,
    string? LastError)
{
    public string? SuggestedChatModel => ChatCandidates.FirstOrDefault()?.ModelId;
    public string? SuggestedVisionModel => VisionCandidates.FirstOrDefault()?.ModelId;

    public bool HasError => !string.IsNullOrWhiteSpace(LastError);
}

/// <summary>
/// خدمة قراءة كتالوج النماذج العامة. لا تُرسل أسئلة ولا صورًا ولا مفتاح API،
/// ولا تُغيّر المسار المختار ولا تُطبّق أي نموذج تلقائيًا. تعمل فقط عند طلب المستخدم
/// أو في الخلفية بعد تفعيله صراحةً من الإعدادات.
/// </summary>
public interface IFreeModelCatalogService
{
    /// <summary>آخر لقطة محفوظة محليًا، أو <c>null</c> إن لم تُجد.</summary>
    FreeModelCatalogSnapshot? Cached { get; }

    /// <summary>يحدّث الكتالوج من مصدره العام، أو يعيد اللقطة السابقة مع سبب الفشل.</summary>
    Task<FreeModelCatalogSnapshot?> RefreshAsync(CancellationToken ct = default);
}
