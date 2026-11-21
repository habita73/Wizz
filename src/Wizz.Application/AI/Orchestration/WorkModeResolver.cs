namespace Wizz.Application.AI.Orchestration;

using Wizz.Application.Abstractions;

/// <summary>
/// يختار نمط العمل حسب نوع المهمة والمسارات المتاحة، دون تغيير قرار التوجيه نفسه.
/// الفرق عن الموجّه: الموجّه يختار «من يجيب»، وهذا الصنف يختار «كيف يتعاونون»،
/// مع بقاء حارس الأرقام إلزامياً في كل الأحوال.
/// </summary>
public static class WorkModeResolver
{
    // العلامات بصيغة مُطبَّعة (انظر TextNormalizer): التاء المربوطة تُكتب هاء، والهمزات تُوحَّد.
    private static readonly string[] AnalysisMarkers =
    {
        "حلل", "تحليل", "قارن", "مقارن", "متوسط", "فرق", "اتجاه", "احصاء", "إحصاء"
    };

    private static readonly string[] PhrasingMarkers =
    {
        "صيغ", "صياغه", "لخص", "تلخيص", "اكتب", "تنسيق", "نظم", "رتّب", "تقرير", "اعرض", "وريني", "دلني"
    };

    private static readonly string[] ResearchMarkers =
    {
        "ابحث", "بحث", "من هو", "متى", "اين", "اخبار", "الانترنت"
    };

    private static readonly string[] RecommendationMarkers =
    {
        "اقترح", "انصح", "نصيحه", "الافضل", "افضل", "ما العمل"
    };

    /// <summary>سؤال رقمي محمي يُجاب حصرياً بالمدمج.</summary>
    public const string PrecisionReason = "سؤال رقمي محمي — المدمج يجيب مباشرة ويحكّم رقمه";
    public const string DelegatedReason = "صياغة/تحليل بمساعدة نموذج لغوي — المدمج يجهّز الحقائق ويحكّم الأرقام";
    public const string SharedReason = "مهمة مركّبة — تقسيم المهام بين المسارات وتجميع المدمج وتحكيمه";
    public const string SoloReason = "المساعد المدمج وحده — بلا تفويض ولا إنترنت";
    public const string ManualReason = "اختيار يدوي مع تحكيم المدمج الإلزامي للأرقام";

    /// <summary>يحلّل النية ويقرّر نمط العمل والمسارات المشاركة.</summary>
    public static WorkModeDecision Resolve(
        string? question,
        BuiltInAnswer? builtInAnswer,
        bool localAvailable,
        bool cloudAvailable,
        bool isManual = false,
        RouteType manualRoute = RouteType.None)
    {
        var normalized = TextNormalizer.Normalize(question);
        var precision = builtInAnswer is not null
            && BuiltInAssistant.IsPrecisionAnswer(builtInAnswer)
            && !BuiltInAssistant.IsAnalytical(question ?? string.Empty)
            && !FormattingIntent.IsFormattingRequest(question);

        if (isManual)
        {
            var mode = manualRoute == RouteType.BuiltIn || precision
                ? WorkMode.Solo
                : WorkMode.Delegated;
            var contributors = mode == WorkMode.Solo
                ? new[] { RouteType.BuiltIn }
                : AvailableContributors(builtInAnswer, localAvailable, cloudAvailable, manualRoute);
            return new WorkModeDecision(mode, ManualReason, contributors, IsManual: true);
        }

        // أسئلة الأرقام تبقى حصراً للمدمج، ولو كان نموذج لغوي متاحاً.
        if (precision)
        {
            return new WorkModeDecision(WorkMode.Solo, PrecisionReason, new[] { RouteType.BuiltIn });
        }

        var categories = new[]
        {
            Has(normalized, AnalysisMarkers),
            Has(normalized, PhrasingMarkers),
            Has(normalized, ResearchMarkers),
            Has(normalized, RecommendationMarkers)
        }.Count(flag => flag);

        var anyModel = localAvailable || cloudAvailable;

        if (categories >= 2 && anyModel)
        {
            return new WorkModeDecision(
                WorkMode.Shared,
                SharedReason,
                AvailableContributors(builtInAnswer, localAvailable, cloudAvailable));
        }

        if (categories >= 1 && anyModel)
        {
            return new WorkModeDecision(
                WorkMode.Delegated,
                DelegatedReason,
                AvailableContributors(builtInAnswer, localAvailable, cloudAvailable));
        }

        if (builtInAnswer is not null)
        {
            return new WorkModeDecision(WorkMode.Solo, SoloReason, new[] { RouteType.BuiltIn });
        }

        // سؤال حر بلا حقائق رقمية: نموذج لغوي مع بقاء الحارس (لا رقم يُعرض إلا إن كان موثقاً).
        return anyModel
            ? new WorkModeDecision(
                WorkMode.Delegated,
                DelegatedReason,
                AvailableContributors(null, localAvailable, cloudAvailable, preferred: null))
            : new WorkModeDecision(WorkMode.Solo, SoloReason, new[] { RouteType.BuiltIn });
    }

    private static IReadOnlyList<RouteType> AvailableContributors(
        BuiltInAnswer? builtInAnswer,
        bool localAvailable,
        bool cloudAvailable,
        RouteType? preferred = null)
    {
        var contributors = new List<RouteType> { RouteType.BuiltIn };

        if (preferred is RouteType.Local or RouteType.Cloud)
        {
            if (preferred == RouteType.Local && localAvailable) contributors.Add(RouteType.Local);
            if (preferred == RouteType.Cloud && cloudAvailable) contributors.Add(RouteType.Cloud);
            return contributors;
        }

        if (localAvailable) contributors.Add(RouteType.Local);
        if (cloudAvailable) contributors.Add(RouteType.Cloud);
        return contributors;
    }

    private static bool Has(string normalized, string[] markers)
        => markers.Any(marker => normalized.Contains(marker, StringComparison.Ordinal));
}
