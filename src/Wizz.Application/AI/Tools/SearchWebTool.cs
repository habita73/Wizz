namespace Wizz.Application.AI.Tools;

using System.Text;

/// <summary>
/// أداة «بحث في الإنترنت» — اختيارية ومغلقة افتراضياً (Local-first).
/// تعمل فقط إذا: (1) فعّل المستخدم المفتاح، (2) كان السؤال طلب بحث صريحاً،
/// (3) لم يكن السؤال عن بيانات المتجر. كل إجابة تُعلن مصدرها وتذكّر أنها ليست من بيانات المتجر.
/// </summary>
public sealed class SearchWebTool : IBuiltInTool
{
    /// <summary>علامة بادئة لكل إجابة من الإنترنت (تُستخدم للتمييز في الواجهة والسجل).</summary>
    public const string WebMarker = "🌐";

    /// <summary>النية التي تُسجَّل لإجابات البحث.</summary>
    public const string Intent = "بحث";

    /// <summary>كلمات تدل على طلب بحث صريح.</summary>
    private static readonly string[] SearchMarkers =
    {
        "ابحث", "بحث", "على الانترنت", "في الانترنت", "من الانترنت", "ويب", "google", "غوغل", "قوقل",
        "من هو", "من هي", "من هم", "اخبار", "تعريف", "معلومات عن", "ما معنى", "ترجمه", "ترجم"
    };

    /// <summary>كلمات تدل على أن السؤال عن بيانات المتجر — لا تُرسل إلى الإنترنت أبداً.</summary>
    private static readonly string[] StoreMarkers =
    {
        "مبيعات", "مبيعاتك", "بيع", "ديون", "دين", "سعر", "أسعار", "منتج", "منتجات", "مخزون", "متجر",
        "فاتوره", "ايصال", "ضريبه", "كاشير", "زبون", "عميل", "ربح", "ايراد"
    };

    /// <summary>بادئات تُحذف من السؤال قبل البحث (لتقليل الضجيج).</summary>
    private static readonly string[] QueryPrefixes =
    {
        "ابحث لي عن ", "ابحث لي ", "ابحث عن ", "ابحثي عن ", "ابحث ", "بحث عن ", "بحث ",
        "معلومات عن ", "تعريف ", "ما معنى ", "ما هو ", "ما هي ", "من هو ", "من هي ", "من هم ", "اخبار "
    };

    private readonly IWebSearch? _search;
    private readonly Func<bool>? _isAllowed;

    public SearchWebTool(IWebSearch? search, Func<bool>? isAllowed = null)
    {
        _search = search;
        _isAllowed = isAllowed;
    }

    /// <inheritdoc />
    public string Name => "بحث الويب";

    /// <inheritdoc />
    public string Description => "بحث في الإنترنت عن معلومة عامة (اختياري — عند تفعيله من الإعدادات)";

    /// <inheritdoc />
    public int Order => 90;

    /// <summary>هل هذه الإجابة صادرة من الإنترنت؟ (لتمييزها عن إجابات البيانات المحلية).</summary>
    public static bool IsWebAnswer(string? answer)
        => !string.IsNullOrWhiteSpace(answer) && answer.TrimStart().StartsWith(WebMarker, StringComparison.Ordinal);

    /// <summary>
    /// استخراج نص البحث من السؤال: يحذف بادئة الطلب («ابحث عن»، «من هو»…) مع الحفاظ على
    /// الكتابة الأصلية للأسماء (بدون تطبيع) لتحسين نتائج البحث.
    /// </summary>
    public static string BuildQuery(string rawQuestion)
    {
        var raw = rawQuestion?.Trim() ?? string.Empty;
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var prefix in QueryPrefixes)
        {
            var prefixWords = prefix.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (prefixWords.Length == 0 || prefixWords.Length >= words.Length)
            {
                continue;
            }

            var head = string.Join(' ', words.Take(prefixWords.Length));
            if (string.Equals(
                    TextNormalizer.Normalize(head),
                    TextNormalizer.Normalize(prefix.Trim()),
                    StringComparison.Ordinal))
            {
                return TrimPunctuation(string.Join(' ', words.Skip(prefixWords.Length)));
            }
        }

        return TrimPunctuation(raw);
    }

    private static string TrimPunctuation(string text)
        => text.Trim(' ', '؟', '?', '.', '،', '!', ':', '؛');

    /// <summary>هل السؤال طلب بحث صريح عن معلومة عامة (وليست عن بيانات المتجر)؟</summary>
    public static bool IsSearchQuestion(string normalizedQuestion)
        => TextNormalizer.HasAny(normalizedQuestion, SearchMarkers)
           && !TextNormalizer.HasAny(normalizedQuestion, StoreMarkers);

    /// <inheritdoc />
    public async Task<string?> TryAnswerAsync(BuiltInToolContext context, CancellationToken ct = default)
    {
        if (_search is null || _isAllowed is not { } allow || !allow())
        {
            return null;
        }

        if (!IsSearchQuestion(context.Question))
        {
            return null;
        }

        var query = BuildQuery(context.RawQuestion);
        if (query.Length < 3)
        {
            return $"{WebMarker} اكتب ما تريد البحث عنه بعد كلمة «ابحث»، مثال: «ابحث عن تاريخ الجزائر».";
        }

        try
        {
            var result = await _search.SearchAsync(query, ct);
            if (result is null)
            {
                return $"{WebMarker} بحثت في الإنترنت عن «{query}» ولم أجد نتيجة واضحة.\n" +
                       "جرّب صياغة أوضح، أو اسأل عن بيانات متجرك — فهي متاحة محلياً دون إنترنت.";
            }

            return Format(query, result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // فشل الشبكة لا يجب أن يُفشل المساعد: المدمج يعمل محلياً.
            return $"{WebMarker} تعذّر الوصول إلى الإنترنت الآن.\n" +
                   "المساعد المدمج يعمل محلياً — بيانات متجرك متاحة دون إنترنت.";
        }
    }

    private static string Format(string query, WebSearchResult result)
    {
        var sb = new StringBuilder($"{WebMarker} نتيجة من الإنترنت ({result.Provider}) عن «{query}»:\n");

        if (!string.IsNullOrWhiteSpace(result.Summary))
        {
            sb.AppendLine(result.Summary.Trim());
        }

        foreach (var snippet in result.Snippets.Take(3))
        {
            var url = string.IsNullOrWhiteSpace(snippet.Url) ? string.Empty : $"\n  🔗 {snippet.Url}";
            sb.AppendLine($"• {snippet.Title}: {snippet.Snippet}{url}");
        }

        sb.AppendLine();
        sb.Append("ℹ️ هذه معلومة من الإنترنت وليست من بيانات متجرك.");
        return sb.ToString().TrimEnd();
    }
}
