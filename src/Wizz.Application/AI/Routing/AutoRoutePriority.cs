namespace Wizz.Application.AI.Routing;

using Wizz.Application.Abstractions;

/// <summary>
/// ترتيب أولوية المسارات في التوجيه التلقائي (المدمج / المحلي / السحابي).
/// منطق نقي وقابل للاختبار؛ حفظ الترتيب في localStorage مسؤولية طبقة العرض.
/// </summary>
public sealed class AutoRoutePriority
{
    /// <summary>نقاط كل درجة أولوية (الفارق بين المرتبة الأولى والثانية = 60 نقطة).</summary>
    public const int PriorityUnit = 60;

    /// <summary>المسارات الثلاثة القابلة للترتيب (بدون None).</summary>
    public static IReadOnlyList<RouteType> AllRoutes { get; } =
        new[] { RouteType.BuiltIn, RouteType.Local, RouteType.Cloud };

    private readonly List<RouteType> _order;

    /// <summary>ينشئ ترتيباً افتراضياً: المدمج ← المحلي ← السحابي (local-first).</summary>
    public AutoRoutePriority()
        : this(null)
    {
    }

    /// <summary>ينشئ ترتيباً من قائمة مسارات (يُكمل الناقص بالترتيب الافتراضي).</summary>
    public AutoRoutePriority(IEnumerable<RouteType>? order)
    {
        _order = Normalize(order);
    }

    /// <summary>الترتيب الافتراضي المعتمد: المدمج ثم المحلي ثم السحابي.</summary>
    public static AutoRoutePriority Default => new(null);

    /// <summary>الترتيب الحالي (الأول = الأعلى أولوية).</summary>
    public IReadOnlyList<RouteType> Order => _order;

    /// <summary>يعيد ضبط الترتيب الحالي في المكان (يستفيد منه الموجّه لأن الكائن واحد لكل جلسة).</summary>
    public void SetOrder(IEnumerable<RouteType>? order)
    {
        var normalized = Normalize(order);
        _order.Clear();
        _order.AddRange(normalized);
    }

    /// <summary>موضع المسار (0 = الأول)، أو -1 إن لم يكن موجوداً.</summary>
    public int IndexOf(RouteType route) => _order.IndexOf(route);

    /// <summary>نقاط الأولوية: المرتبة 1 = 180، 2 = 120، 3 = 60 (لثلاثة مسارات).</summary>
    public int PriorityPoints(RouteType route)
    {
        var index = IndexOf(route);
        return index < 0 ? 0 : (_order.Count - index) * PriorityUnit;
    }

    /// <summary>يعيد نسخة جديدة بمسار منقول إلى موضع جديد (دون تعديل الأصل).</summary>
    public AutoRoutePriority Moved(RouteType route, int newIndex)
    {
        if (!_order.Contains(route))
        {
            return this;
        }

        var moved = new List<RouteType>(_order);
        moved.Remove(route);
        moved.Insert(Math.Clamp(newIndex, 0, moved.Count), route);
        return new AutoRoutePriority(moved);
    }

    /// <summary>يعيد نسخة جديدة بعد رفع المسار مرتبة واحدة.</summary>
    public AutoRoutePriority MoveUp(RouteType route)
    {
        var index = IndexOf(route);
        return index <= 0 ? this : Moved(route, index - 1);
    }

    /// <summary>يعيد نسخة جديدة بعد إنزال المسار مرتبة واحدة.</summary>
    public AutoRoutePriority MoveDown(RouteType route)
    {
        var index = IndexOf(route);
        return index < 0 || index >= _order.Count - 1 ? this : Moved(route, index + 1);
    }

    /// <summary>هل الترتيب مطابق للترتيب الافتراضي؟</summary>
    public bool IsDefault
        => _order.Count == AllRoutes.Count && _order.SequenceEqual(AllRoutes);

    /// <summary>يحذف التكرار ويستبعد None ويُكمل المسارات الناقصة بالترتيب الافتراضي.</summary>
    private static List<RouteType> Normalize(IEnumerable<RouteType>? order)
    {
        var result = new List<RouteType>(AllRoutes.Count);
        if (order is not null)
        {
            foreach (var route in order)
            {
                if (AllRoutes.Contains(route) && !result.Contains(route))
                {
                    result.Add(route);
                }
            }
        }

        foreach (var route in AllRoutes)
        {
            if (!result.Contains(route))
            {
                result.Add(route);
            }
        }

        return result;
    }
}
