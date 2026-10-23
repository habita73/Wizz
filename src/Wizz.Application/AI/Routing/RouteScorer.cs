namespace Wizz.Application.AI.Routing;

using Wizz.Application.Abstractions;

/// <summary>حالة المسارات المتاحة لحظة اتخاذ القرار (يدخلها الموجّه من فحوصه الفعلية).</summary>
public sealed record RouteAvailability(
    bool BuiltInCanAnswer,
    bool LocalAvailable,
    DeviceCapabilityTier DeviceTier,
    bool InternetAvailable,
    bool CloudEnabled,
    bool CloudAvailable,
    bool QuotaExceeded,
    bool IsAnalytical,
    bool ProtectBuiltIn = false);

/// <summary>
/// خوارزمية الوزن الذكية للتوجيه التلقائي:
/// الدرجة = نقاط الأولوية (اختيار المستخدم) + درجة الجودة (طبيعة المسار والجهاز والسؤال).
/// الأولوية تفوز عند تقارب الجودة، لكن مرشحاً غير متاح لا يُختار ولو كان الأول.
/// </summary>
public static class RouteScorer
{
    private const int BuiltInQuality = 100;
    private const int LocalQuality = 70;
    private const int CloudQuality = 50;

    /// <summary>سبب اختيار المدمج عند تفعيل حماية أسئلة الأرقام.</summary>
    public const string ProtectedReason = "حماية أسئلة الأرقام — المدمج يضمن الرقم الصحيح";

    /// <summary>يختار المسار الأعلى درجة بين المرشحين المتاحين.</summary>
    public static RouteDecision Choose(RouteAvailability availability, AutoRoutePriority? priority = null)
    {
        var order = priority ?? AutoRoutePriority.Default;
        var candidates = new List<RouteCandidate>(AutoRoutePriority.AllRoutes.Count)
        {
            Evaluate(RouteType.BuiltIn, order, availability),
            Evaluate(RouteType.Local, order, availability),
            Evaluate(RouteType.Cloud, order, availability),
        };

        // المرشحون المتاحون أولاً، ثم الدرجة الأعلى، ثم ترتيب أولوية المستخدم.
        var ordered = candidates
            .OrderByDescending(c => c.Eligible)
            .ThenByDescending(c => c.Score)
            .ThenBy(c => order.IndexOf(c.Route))
            .ToList();

        var chosen = ordered.FirstOrDefault(c => c.Eligible);
        if (chosen is null)
        {
            return new RouteDecision(RouteType.None, "لا يوجد مسار متاح لهذا السؤال", ordered);
        }

        // حماية أسئلة الأرقام: عند إجابة مدمجة دقيقة، يبقى القرار للمدمج مهما كان الترتيب،
        // لأن النماذج اللغوية قد تُخطئ في الأرقام. (الترتيب يظل نافذاً للأسئلة الأخرى.)
        if (availability.ProtectBuiltIn && availability.BuiltInCanAnswer)
        {
            return new RouteDecision(RouteType.BuiltIn, ProtectedReason, ordered);
        }

        return new RouteDecision(chosen.Route, chosen.Reason, ordered);
    }

    /// <summary>
    /// قرار سريع عندما يكون المدمج أولاً وقد أجاب: درجته (180+100) تفوق أقصى درجة ممكنة
    /// للمحلي (120+80) والسحابي (120+60)، فلا داعي لفحص Ollama أو الشبكة (توفير زمن ملموس).
    /// </summary>
    public static RouteDecision BuiltInFirst(string reason)
    {
        var score = (AutoRoutePriority.AllRoutes.Count * AutoRoutePriority.PriorityUnit) + BuiltInQuality;

        return new RouteDecision(RouteType.BuiltIn, reason, new[]
        {
            new RouteCandidate(RouteType.BuiltIn, true, score, reason),
            new RouteCandidate(RouteType.Local, false, 0, UncheckedFirst, Checked: false),
            new RouteCandidate(RouteType.Cloud, false, 0, UncheckedFirst, Checked: false),
        });
    }

    /// <summary>
    /// قرار سريع لحماية أسئلة الأرقام: المدمج أجاب إجابة دقيقة، فلا نفحص النماذج ولا الشبكة
    /// ونُبقي القرار للمدمج مع توضيح السبب في الواجهة.
    /// </summary>
    public static RouteDecision BuiltInProtected()
    {
        var score = (AutoRoutePriority.AllRoutes.Count * AutoRoutePriority.PriorityUnit) + BuiltInQuality;

        return new RouteDecision(RouteType.BuiltIn, ProtectedReason, new[]
        {
            new RouteCandidate(RouteType.BuiltIn, true, score, ProtectedReason),
            new RouteCandidate(RouteType.Local, false, 0, UncheckedProtected, Checked: false),
            new RouteCandidate(RouteType.Cloud, false, 0, UncheckedProtected, Checked: false),
        });
    }

    private const string UncheckedFirst = "لم يُفحص — المدمج فاز بالأولوية";
    private const string UncheckedProtected = "لم يُفحص — حماية أسئلة الأرقام (المدمج أدق)";

    private static RouteCandidate Evaluate(RouteType route, AutoRoutePriority order, RouteAvailability a)
    {
        var (eligible, reason) = route switch
        {
            RouteType.BuiltIn => a.BuiltInCanAnswer
                ? (true, "بيانات متجرك جاهزة محلياً دون إنترنت")
                : (false, "لا توجد إجابة مدمجة لهذا السؤال"),
            RouteType.Local => a.LocalAvailable
                ? (true, $"نموذج محلي متاح ({TierLabel(a.DeviceTier)})")
                : (false, "Ollama أو النموذج المحلي غير متاح"),
            RouteType.Cloud => CloudStatus(a),
            _ => (false, "مسار غير معروف")
        };

        var score = eligible ? order.PriorityPoints(route) + Quality(route, a) : 0;
        return new RouteCandidate(route, eligible, score, reason);
    }

    private static (bool Eligible, string Reason) CloudStatus(RouteAvailability a)
    {
        if (!a.CloudEnabled)
        {
            return (false, "السحابة غير مفعّلة");
        }

        if (!a.InternetAvailable)
        {
            return (false, "لا يوجد اتصال بالإنترنت");
        }

        if (a.QuotaExceeded)
        {
            return (false, "تم تجاوز الحصة المجانية");
        }

        return a.CloudAvailable
            ? (true, "سحابي مفعّل ومتاح ضمن الحصة")
            : (false, "لا يوجد مزوّد سحابي مُهيّأ");
    }

    private static int Quality(RouteType route, RouteAvailability a)
    {
        return route switch
        {
            RouteType.BuiltIn => BuiltInQuality,
            RouteType.Local => LocalQuality + a.DeviceTier switch
            {
                DeviceCapabilityTier.Heavy => 10,
                DeviceCapabilityTier.Medium => 5,
                _ => 0
            },
            RouteType.Cloud => CloudQuality + (a.IsAnalytical ? 10 : 0),
            _ => 0
        };
    }

    private static string TierLabel(DeviceCapabilityTier tier) => tier switch
    {
        DeviceCapabilityTier.Heavy => "جهاز قوي",
        DeviceCapabilityTier.Medium => "جهاز متوسط",
        DeviceCapabilityTier.Light => "جهاز خفيف",
        _ => "قدرة غير معروفة"
    };
}

/// <summary>نصوص المسارات العربية المشتركة بين المنطق والواجهة.</summary>
public static class RouteText
{
    /// <summary>اسم المسار المختصر.</summary>
    public static string Label(RouteType route) => route switch
    {
        RouteType.BuiltIn => "المدمج",
        RouteType.Local => "المحلي",
        RouteType.Cloud => "السحابي",
        _ => "لا مسار"
    };

    /// <summary>أيقونة المسار.</summary>
    public static string Icon(RouteType route) => route switch
    {
        RouteType.BuiltIn => "⚡",
        RouteType.Local => "🟢",
        RouteType.Cloud => "☁️",
        _ => "⛔"
    };

    /// <summary>عنوان مركّب للأيقونة والاسم: «⚡ المدمج».</summary>
    public static string Display(RouteType route) => $"{Icon(route)} {Label(route)}";
}
