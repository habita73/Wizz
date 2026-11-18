namespace Wizz.Application.AI.Semantic;

using Wizz.Application.AI.Memory;

/// <summary>
/// نواة مشتركة إضافية من مفردات اللهجة الجزائرية المفهومة على نطاق واسع،
/// تُستخدم في المحادثة فقط. ليست قاموساً مناطقياً ولا تُستخدم في الفواتير؛
/// جودتها تحتاج تحققاً فعلياً قبل الاعتماد التجاري (تبقى `NEEDS-VALIDATION`).
/// </summary>
public static class SharedCoreDialect
{
    // بصيغة مُطبَّعة: التاء المربوطة تُكتب هاءً والهمزات تُوحَّد.
    public static readonly string[] Want = { "بغيت", "نبي", "نريد" };
    public static readonly string[] Question = { "وش", "واش", "كيفاش", "علاش" };
    public static readonly string[] Time = { "دابا", "البارح", "غدا", "اليوم", "الشهر", "السبوع" };
    public static readonly string[] Wholesale = { "الجمله", "التجزئه" };
    public static readonly string[] Goods = DialectSynonyms.Goods;
    public static readonly string[] Show = DialectSynonyms.Show;
    public static readonly string[] Price = DialectSynonyms.Price;
}

/// <summary>
/// يحسّن فهم النية محلياً: فصحى + النواة المشتركة من اللهجة + دعم استيعابي أساسي للفرنسية،
/// ويحلّ مرجعية أسئلة المتابعة («كم سعره؟») من ذاكرة المحادثة. لا يلمس بيانات المتجر المخزّنة،
/// ولا يستخدم نموذجاً لغوياً ولا اتصالاً خارجياً.
/// </summary>
public static class SemanticIntentResolver
{
    // علامات الفصحى واللهجة بصيغة مُطبَّعة.
    private static readonly string[] SalesMarkers =
        { "مبيعات", "بيع", "مبيع", "ايراد", "ايرادات", "اجمالي", "تحصيل", "ventes", "chiffre" };

    private static readonly string[] DebtMarkers =
        { "ديون", "دين", "متاخر", "مستحق", "مديونيه", "dettes", "impayes" };

    private static readonly string[] PriceMarkers =
        { "سعر", "اسعار", "ثمن", "بكم", "شحال", "بشحال", "التمن", "prix", "combien", "tarif" };

    private static readonly string[] ProductMarkers =
        { "منتج", "منتجات", "بضاعه", "بضايع", "مخزون", "اصناف", "produit", "produits", "stock" };

    private static readonly string[] AnalysisMarkers =
        { "حلل", "تحليل", "قارن", "مقارنه", "متوسط", "فرق", "اتجاه", "احصاء", "analyser", "comparer", "moyenne" };

    private static readonly string[] ToolMarkers =
        { "الوقت", "الساعه", "التاريخ", "الطابعه", "الملفات", "القرص", "النظام", "heure", "date", "imprimante" };

    private static readonly string[] FollowUpMarkers =
        { "سعره", "سعرها", "بكم", "شحال", "نفسه", "نفسها", "وحده", "وحدتها", "الجمله", "التجزئه", "ce prix", "meme" };

    private static readonly string[] SubjectStopWords =
    {
        "سعر", "اسعار", "ثمن", "بكم", "شحال", "بشحال", "التمن", "كم", "كام", "بغيت", "نبي", "منتج", "المنتج",
        "منتجات", "المنتجات", "بضاعه", "بضايع", "هو", "هي", "هم", "هذا", "هذه", "ذلك", "تلك",
        "في", "من", "الى", "على", "عن", "مع", "الجمله", "التجزئه", "وحده", "نفسه", "ما", "هل",
        "او", "ثم", "كل", "لي", "له", "لها", "يا", "بغيت", "نبي", "وش", "واش", "كيفاش", "علاش",
        "prix", "combien", "le", "la", "les", "un", "une", "du", "de", "est", "ce", "cette"
    };

    /// <summary>يحلّل السؤال ويقرّر النية والموضوع والمرجعية.</summary>
    public static SemanticUnderstanding Resolve(
        string? question,
        string? rememberedSubject = null,
        IConversationMemory? memory = null,
        IReadOnlyList<string>? knownProductNames = null)
    {
        var normalized = TextNormalizer.Normalize(question);
        var matched = new List<string>();

        TryCollect(normalized, DebtMarkers, matched);
        TryCollect(normalized, PriceMarkers, matched);
        TryCollect(normalized, SharedCoreDialect.Price, matched);
        TryCollect(normalized, SharedCoreDialect.Goods, matched);
        TryCollect(normalized, ProductMarkers, matched);
        TryCollect(normalized, SharedCoreDialect.Show, matched);
        TryCollect(normalized, AnalysisMarkers, matched);
        TryCollect(normalized, SalesMarkers, matched);
        TryCollect(normalized, ToolMarkers, matched);
        TryCollect(normalized, SharedCoreDialect.Want, matched);
        TryCollect(normalized, SharedCoreDialect.Question, matched);

        var intent = DetectIntent(normalized);

        // أولوية البحث عن الموضوع: منتج معروف مذكور صراحةً ← موضوع من الذاكرة عند سؤال متابعة.
        var subject = ExtractSubject(normalized, knownProductNames);
        var isFollowUp = matched.Any(m => FollowUpMarkers.Contains(m, StringComparer.Ordinal))
                         || (subject is null && !string.IsNullOrWhiteSpace(rememberedSubject));
        var effectiveRemembered = rememberedSubject
            ?? (memory is not null ? memory.LastSubject : null);
        var resolved = subject ?? (isFollowUp ? effectiveRemembered : null);

        return new SemanticUnderstanding(
            intent,
            isFollowUp,
            subject,
            resolved,
            matched.Distinct(StringComparer.Ordinal).ToList());
    }

    private static SemanticIntent DetectIntent(string normalized)
    {
        if (TextNormalizer.HasAny(normalized, ToolMarkers)) return SemanticIntent.Tools;
        if (TextNormalizer.HasAny(normalized, DebtMarkers)) return SemanticIntent.Debt;
        if (TextNormalizer.HasAny(normalized, PriceMarkers) || TextNormalizer.HasAny(normalized, SharedCoreDialect.Price))
            return SemanticIntent.Price;
        if (TextNormalizer.HasAny(normalized, SalesMarkers)) return SemanticIntent.Sales;
        if (TextNormalizer.HasAny(normalized, AnalysisMarkers)) return SemanticIntent.Analysis;
        if (TextNormalizer.HasAny(normalized, ProductMarkers) || TextNormalizer.HasAny(normalized, SharedCoreDialect.Goods))
            return SemanticIntent.Product;
        return SemanticIntent.General;
    }

    private static string? ExtractSubject(string normalized, IReadOnlyList<string>? knownProductNames)
    {
        if (knownProductNames is not null)
        {
            foreach (var product in knownProductNames)
            {
                if (string.IsNullOrWhiteSpace(product)) continue;
                var normalizedProduct = TextNormalizer.Normalize(product);
                if (normalizedProduct.Length > 0 && normalized.Contains(normalizedProduct, StringComparison.Ordinal))
                    return product.Trim();
            }
        }

        var tokens = normalized
            .Split(new[] { ' ', '\t', '،', ',', '؟', '?', '!', '-', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length >= 3)
            .Where(t => !SubjectStopWords.Contains(t, StringComparer.Ordinal))
            .Where(t => !t.All(char.IsDigit))
            .ToList();

        return tokens.Count == 0 ? null : tokens[0];
    }

    private static void TryCollect(string normalized, string[] markers, List<string> matched)
    {
        foreach (var marker in markers)
        {
            if (!string.IsNullOrEmpty(marker)
                && normalized.Contains(marker, StringComparison.Ordinal)
                && !matched.Contains(marker, StringComparer.Ordinal))
            {
                matched.Add(marker);
            }
        }
    }
}
