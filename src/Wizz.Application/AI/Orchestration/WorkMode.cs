namespace Wizz.Application.AI.Orchestration;

using Wizz.Application.Abstractions;
using Wizz.Application.AI.Routing;

/// <summary>
/// نمط العمل الصريح كما اعتمد في مصفوفة أنماط العمل (المرجع المعتمد، القسم 3):
/// فردي للدقة والسرعة، مُفوَّض للصياغة وجودة العرض، مُشترَك للمهام المركّبة.
/// التحكيم الرقمي للمدمج إلزامي في كل الأنماط، والاختيار اليدوي باقٍ.
/// </summary>
public enum WorkMode
{
    /// <summary>المدمج وحده: أسئلة الأرقام والمهام الحرجة.</summary>
    Solo = 0,

    /// <summary>المدمج يجهّز الحقائق ويفوّض الصياغة لمسار لغوي، ثم يُحكّم الأرقام.</summary>
    Delegated = 1,

    /// <summary>تقسيم المهام بين أكثر من مسار مع تجميع المدمج وتحكيم كل رقم.</summary>
    Shared = 2
}

/// <summary>قرار نمط العمل مع المسارات المشاركة وسبب الاختيار.</summary>
/// <param name="Mode">النمط المختار.</param>
/// <param name="Reason">سبب مفهوم يظهر للمستخدم.</param>
/// <param name="Contributors">المسارات التي أدّت عملاً فعلياً (بترتيب المشاركة).</param>
/// <param name="IsManual">هل فُرض النمط باختيار يدوي؟</param>
public sealed record WorkModeDecision(
    WorkMode Mode,
    string Reason,
    IReadOnlyList<RouteType> Contributors,
    bool IsManual = false);

/// <summary>نصوص أنماط العمل المستخدمة في الواجهة والاختبارات.</summary>
public static class WorkModeText
{
    public static string Label(WorkMode mode) => mode switch
    {
        WorkMode.Solo => "فردي",
        WorkMode.Delegated => "مُفوَّض",
        WorkMode.Shared => "مُشترَك",
        _ => "فردي"
    };

    /// <summary>سطر شفافية يوضّح للمستخدم من أدّى ماذا، وأن الأرقام محكَّمة.</summary>
    public static string Transparency(WorkModeDecision decision)
    {
        var routes = decision.Contributors.Count == 0
            ? RouteText.Label(RouteType.BuiltIn)
            : string.Join(" + ", decision.Contributors.Select(RouteText.Label));
        return $"نمط العمل: {Label(decision.Mode)} — شارك: {routes} — الأرقام محكَّمة بواسطة المدمج";
    }
}
