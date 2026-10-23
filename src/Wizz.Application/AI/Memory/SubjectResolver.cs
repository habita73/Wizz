namespace Wizz.Application.AI.Memory;

using Wizz.Application.AI;

/// <summary>
/// يستخرج موضوع السؤال (اسم المنتج) من قائمة المنتجات، ويكشف أسئلة المتابعة
/// التي تعتمد على موضوع سابق مثل «كم سعره؟» أو «وفي الجملة؟».
/// </summary>
public static class SubjectResolver
{
    /// <summary>كلمات لا تُعتبر موضوعاً (أدوات لغوية وعبارات الأسئلة).</summary>
    private static readonly string[] StopWords =
    {
        "سعر", "اسعار", "ثمن", "بكم", "كم", "كام", "منتج", "المنتج", "منتجات", "المنتجات",
        "هو", "هي", "هم", "هذا", "هذه", "ذلك", "تلك", "في", "من", "الى", "على", "عن", "مع",
        "الجمله", "التجزئه", "وحده", "وحدتها", "نفسه", "نفسها", "ما", "هل", "او", "ثم", "كل",
        "الجديد", "الجديده", "القديم", "القديمه", "لي", "له", "لها", "يا"
    };

    /// <summary>كلمات تدلّ على سؤال متابعة يعتمد على موضوع سابق.</summary>
    private static readonly string[] FollowUpMarkers =
    {
        "سعر", "اسعار", "ثمن", "بكم", "الجمله", "التجزئه", "وحده"
    };

    /// <summary>الحد الأدنى لطول الكلمة الدالّة على موضوع.</summary>
    private const int MinTokenLength = 3;

    /// <summary>أدوات ترقيم تُحذف من طرفي الكلمة.</summary>
    private static readonly char[] TrimmablePunctuation =
    {
        '؟', '?', '!', '.', ',', '،', '؛', ':', ';', '"', '«', '»', '(', ')',
        '[', ']', '-', '—', '_', '/', '\\', '*', '%', '#', '=', '~', '@', '$'
    };

    /// <summary>يستخرج الكلمات الدالّة من نص السؤال بعد إزالة الكلمات الشائعة وعلامات الترقيم.</summary>
    public static IReadOnlyList<string> ExtractTokens(string? question)
    {
        var normalized = TextNormalizer.Normalize(question);
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(TrimPunctuation)
            .Where(t => t.Length >= MinTokenLength && !IsStopWord(t))
            .ToList();
    }

    /// <summary>
    /// هل الكلمة من الكلمات الشائعة؟ المقارنة **كاملة** لا جزئية، حتى لا تُحذف أسماء
    /// منتجات تحتوي الكلمة الشائعة داخل حروفها (مثل «حليب» التي تحتوي «لي»).
    /// </summary>
    private static bool IsStopWord(string token)
    {
        var bare = StripDefiniteArticle(token);
        foreach (var word in StopWords)
        {
            if (string.Equals(word, token, StringComparison.Ordinal) ||
                string.Equals(word, bare, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>يحذف أداة التعريف «ال» من بداية الكلمة إن وُجدت (الخبز ← خبز).</summary>
    public static string StripDefiniteArticle(string token)
    {
        if (token.Length > 2 && token.StartsWith("ال", StringComparison.Ordinal))
        {
            return token[2..];
        }

        return token;
    }

    /// <summary>
    /// يبحث عن المنتجات المطابقة لكلمات السؤال (يتجاهل أداة التعريف «ال» وعلامات الترقيم).
    /// </summary>
    public static IReadOnlyList<ProductInfo> MatchProducts(
        IReadOnlyList<ProductInfo>? products,
        IReadOnlyList<string>? tokens,
        int maxResults = 5)
    {
        var matches = new List<ProductInfo>();
        if (products is null || products.Count == 0 || tokens is null || tokens.Count == 0)
        {
            return matches;
        }

        foreach (var product in products)
        {
            var name = TextNormalizer.Normalize(product.Name);
            if (tokens.Any(t => MatchesToken(name, product.Barcode, t)))
            {
                matches.Add(product);
            }

            if (matches.Count >= maxResults)
            {
                break;
            }
        }

        return matches;
    }

    /// <summary>
    /// يبحث عن أفضل منتج مطابق للسؤال ويعيد اسمه، أو null إن لم يوجد تطابق واضح.
    /// </summary>
    public static string? FindSubject(string? question, IReadOnlyList<ProductInfo>? products)
    {
        if (products is null || products.Count == 0)
        {
            return null;
        }

        var tokens = ExtractTokens(question);
        if (tokens.Count == 0)
        {
            return null;
        }

        ProductInfo? best = null;
        var bestScore = 0;

        foreach (var product in products)
        {
            var name = TextNormalizer.Normalize(product.Name);
            var score = 0;
            foreach (var token in tokens)
            {
                if (MatchesToken(name, product.Barcode, token))
                {
                    score += Math.Max(token.Length, StripDefiniteArticle(token).Length);
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = product;
            }
        }

        return best?.Name;
    }

    /// <summary>
    /// هل السؤال متابعة تعتمد على موضوع سابق (لا موضوع صريح فيه، لكنه يطرح سؤال سعر)؟
    /// </summary>
    public static bool IsFollowUp(string? question, IReadOnlyList<ProductInfo>? products)
    {
        if (FindSubject(question, products) is not null)
        {
            return false;
        }

        var normalized = TextNormalizer.Normalize(question);
        return TextNormalizer.HasAny(normalized, FollowUpMarkers);
    }

    /// <summary>هل تطابقت الكلمة مع اسم المنتج (مع مراعاة أداة التعريف)؟</summary>
    private static bool MatchesToken(string normalizedName, string? barcode, string token)
    {
        if (token.Length == 0)
        {
            return false;
        }

        var names = new[] { normalizedName, StripDefiniteArticle(normalizedName) };
        var forms = new[] { token, StripDefiniteArticle(token) };

        foreach (var name in names)
        {
            foreach (var form in forms)
            {
                if (form.Length >= MinTokenLength && name.Contains(form, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return !string.IsNullOrWhiteSpace(barcode)
               && barcode.Contains(token, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>يحذف علامات الترقيم من طرفي الكلمة.</summary>
    private static string TrimPunctuation(string token)
        => token.Trim(TrimmablePunctuation);
}
