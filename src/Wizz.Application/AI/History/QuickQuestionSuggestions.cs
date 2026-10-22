namespace Wizz.Application.AI.History;

/// <summary>
/// يقترح أسئلة جاهزة حسب سياق الصفحة التي جاء منها المستخدم.
/// يجمع بين الاقتراحات السياقية وأكثر أسئلته تكراراً، بلا تكرار.
/// </summary>
public static class QuickQuestionSuggestions
{
    /// <summary>سياق الصفحة — يُمرَّر من الواجهة (اسم الصفحة الحالي).</summary>
    public enum Context
    {
        General,
        Sales,
        Products,
        Debt,
        Reports,
        Settings
    }

    private static readonly IReadOnlyDictionary<Context, string[]> ByContext =
        new Dictionary<Context, string[]>
        {
            [Context.General] = new[]
            {
                "مبيعات اليوم",
                "قائمة الديون",
                "أفضل 5 منتجات مبيعاً",
                "كم عدد المنتجات؟"
            },
            [Context.Sales] = new[]
            {
                "مبيعات اليوم",
                "مبيعات أمس",
                "مبيعات آخر 7 أيام",
                "مبيعات هذا الشهر"
            },
            [Context.Products] = new[]
            {
                "كم عدد المنتجات؟",
                "قائمة المنتجات",
                "المنتجات المعطلة",
                "أفضل 5 منتجات مبيعاً"
            },
            [Context.Debt] = new[]
            {
                "قائمة الديون",
                "مجموع الديون",
                "أكبر دين",
                "أقدم دين"
            },
            [Context.Reports] = new[]
            {
                "مبيعات هذا الشهر",
                "مبيعات آخر 7 أيام",
                "أفضل 5 منتجات مبيعاً",
                "مجموع الديون"
            },
            [Context.Settings] = new[]
            {
                "كيف أعمل نسخة احتياطية؟",
                "ما أنواع الطباعة المتاحة؟",
                "معلومات النظام",
                "ما مساحة القرص المتبقية؟"
            }
        };

    /// <summary>الاقتراحات السياقية الخام لصفحة معيّنة.</summary>
    public static IReadOnlyList<string> ForContext(Context context)
        => ByContext.TryGetValue(context, out var list) ? list : ByContext[Context.General];

    /// <summary>
    /// يبني قائمة نهائية: أكثر الأسئلة تكراراً أولاً، ثم اقتراحات السياق،
    /// دون تكرار (بمطابقة النص المطبَّع)، وبحد أقصى.
    /// </summary>
    public static IReadOnlyList<string> Build(
        Context context,
        AiHistoryService? history = null,
        int max = 6)
    {
        if (max <= 0) return Array.Empty<string>();

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void TryAdd(string q)
        {
            var key = AiHistoryService.KeyOf(AiHistoryService.Clean(q));
            if (key.Length == 0 || !seen.Add(key)) return;
            result.Add(AiHistoryService.Clean(q));
        }

        // الأكثر تكراراً أولاً
        if (history is not null)
        {
            foreach (var entry in history.Top(max))
                TryAdd(entry.Question);
        }

        // ثم اقتراحات السياق
        foreach (var q in ForContext(context))
        {
            if (result.Count >= max) break;
            TryAdd(q);
        }

        // التعويض من العام إن لم يكتمل العدد
        if (result.Count < max)
        {
            foreach (var q in ForContext(Context.General))
            {
                if (result.Count >= max) break;
                TryAdd(q);
            }
        }

        return result.Count > max ? result.Take(max).ToList() : result;
    }

    /// <summary>تخمين السياق من مسار/اسم صفحة.</summary>
    public static Context ContextFromRoute(string? route)
    {
        var r = (route ?? string.Empty).ToLowerInvariant();
        if (r.Contains("sale")) return Context.Sales;
        if (r.Contains("product")) return Context.Products;
        if (r.Contains("debt") || r.Contains("credit")) return Context.Debt;
        if (r.Contains("report") || r.Contains("dashboard")) return Context.Reports;
        if (r.Contains("setting")) return Context.Settings;
        return Context.General;
    }
}
