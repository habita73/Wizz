namespace Wizz.Application.AI.Tools;

/// <summary>مقتطف واحد من نتيجة بحث (عنوان + ملخص + رابط إن وُجد).</summary>
public sealed record WebSearchSnippet(string Title, string Snippet, string? Url);

/// <summary>نتيجة بحث إنترنت جاهزة للعرض. لا تحتوي أي بيانات متجر أو بيانات شخصية.</summary>
public sealed record WebSearchResult(
    string Provider,
    string Summary,
    IReadOnlyList<WebSearchSnippet> Snippets);

/// <summary>
/// بحث ويب اختياري (بلا مفاتيح API) يستخدمه المساعد المدمج عند طلب المستخدم الصريح فقط.
/// التنفيذ في طبقة البنية التحتية، والقرار (هل نبحث؟) في طبقة التطبيق.
/// </summary>
public interface IWebSearch
{
    /// <summary>يبحث عن نص عام ويعيد نتيجة، أو <c>null</c> إن لم يجد شيئاً.</summary>
    Task<WebSearchResult?> SearchAsync(string query, CancellationToken ct = default);
}
