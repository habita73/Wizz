namespace Wizz.Application.AI.Tools;

using System.Globalization;
using System.Text;

/// <summary>
/// أداة تحليلات حتمية للمساعد المدمج: متوسط قيمة البيع، مقارنة الفترات،
/// الديون المتأخرة، وإجمالي فترة محددة. كل رقم يأتي من لقطة المتجر المحلية
/// (استعلامات C#/SQLite حتمية) — لا SQL حر ولا رقم من توليد نص.
/// الأداة للقراءة فقط ولا تكتب في قاعدة البيانات.
/// </summary>
public sealed class EmbeddedAnalyticsTool : IBuiltInTool
{
    private static readonly string[] AverageMarkers =
    {
        "متوسط البيع", "متوسط قيمه", "متوسط المبيعات", "متوسط العمليه",
        "متوسط الفاتوره", "متوسط الحساب"
    };

    private static readonly string[] ComparisonMarkers =
    {
        "فرق المبيعات", "فرق مبيعات", "فرق اليوم", "فرق الاسبوع", "زادت المبيعات", "نقصت المبيعات",
        "مبيعات زادت", "مبيعات نقصت"
    };

    private static readonly string[] OverdueMarkers =
    {
        "المتاخرات", "ديون متاخره", "الديون المتاخره", "ديون فائته", "شحال المتاخر"
    };

    private static readonly string[] TotalMarkers = { "اجمالي مبيعات", "مجموع مبيعات", "اجمالي البيع" };

    private static readonly string[] WeekMarkers = { "اسبوع", "7 ايام" };
    private static readonly string[] MonthMarkers = { "شهر" };
    private static readonly string[] YesterdayMarkers = { "امس" };

    public string Name => "EmbeddedAnalytics";

    public string Description => "تحليلات محسوبة من بيانات المتجر المحلية (متوسط، مقارنة، متأخرات، إجمالي فترة).";

    public int Order => 20;

    public async Task<string?> TryAnswerAsync(BuiltInToolContext context, CancellationToken ct = default)
    {
        var q = context.Question;

        if (HasAny(q, AverageMarkers))
        {
            var snapshot = await context.GetSnapshotAsync(ct);
            return FormatAverage(snapshot, q);
        }

        if (HasAny(q, ComparisonMarkers))
        {
            var snapshot = await context.GetSnapshotAsync(ct);
            return FormatComparison(snapshot, q);
        }

        if (HasAny(q, OverdueMarkers))
        {
            var snapshot = await context.GetSnapshotAsync(ct);
            return FormatOverdue(snapshot);
        }

        if (HasAny(q, TotalMarkers))
        {
            var snapshot = await context.GetSnapshotAsync(ct);
            return FormatTotal(snapshot, q);
        }

        return null;
    }

    private static string FormatAverage(StoreSnapshot s, string q)
    {
        var (label, total, count) = Period(s, q);
        if (count == 0)
        {
            return $"لا توجد مبيعات مسجلة في {label} لحساب المتوسط.";
        }

        return $"متوسط قيمة البيع ({label}): {Fmt(total / count)} د.ج\n" +
               $"• الإجمالي: {Fmt(total)} د.ج\n" +
               $"• عدد العمليات: {count}";
    }

    private static string FormatComparison(StoreSnapshot s, string q)
    {
        if (HasAny(q, WeekMarkers))
        {
            var diff = s.WeekSales - s.PreviousWeekSales;
            var direction = Direction(diff);
            var percent = s.PreviousWeekSales > 0 ? (diff / s.PreviousWeekSales) * 100m : 0m;
            return $"مقارنة آخر 7 أيام بالأسبوع الذي قبلها:\n" +
                   $"• الآن: {Fmt(s.WeekSales)} د.ج في {s.WeekCount} عملية\n" +
                   $"• الأسبوع السابق: {Fmt(s.PreviousWeekSales)} د.ج في {s.PreviousWeekCount} عملية\n" +
                   $"• الفرق: {direction} {Fmt(Math.Abs(diff))} د.ج ({percent.ToString("0.0", CultureInfo.InvariantCulture)}%)";
        }

        var dayDiff = s.TodaySales - s.YesterdaySales;
        var dayDirection = Direction(dayDiff);
        var dayPercent = s.YesterdaySales > 0 ? (dayDiff / s.YesterdaySales) * 100m : 0m;
        return $"مقارنة مبيعات اليوم بأمس:\n" +
               $"• اليوم: {Fmt(s.TodaySales)} د.ج في {s.TodayCount} عملية\n" +
               $"• أمس: {Fmt(s.YesterdaySales)} د.ج في {s.YesterdayCount} عملية\n" +
               $"• الفرق: {dayDirection} {Fmt(Math.Abs(dayDiff))} د.ج ({dayPercent.ToString("0.0", CultureInfo.InvariantCulture)}%)";
    }

    private static string FormatOverdue(StoreSnapshot s)
    {
        var overdue = s.Debts.Where(d => d.IsOverdue).ToList();
        if (overdue.Count == 0)
        {
            return "لا توجد ديون متأخرة حالياً ✅";
        }

        var total = overdue.Sum(d => d.Remaining);
        var sb = new StringBuilder($"الديون المتأخرة: {overdue.Count} عملية، بإجمالي {Fmt(total)} د.ج:\n");
        foreach (var d in overdue.OrderByDescending(d => d.DaysOverdue).Take(10))
        {
            sb.AppendLine($"• فاتورة #{d.SaleNumber} — متبقٍ {Fmt(d.Remaining)} د.ج — متأخرة {d.DaysOverdue} يوم");
        }

        if (overdue.Count > 10)
        {
            sb.AppendLine($"… و{overdue.Count - 10} عملية أخرى.");
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatTotal(StoreSnapshot s, string q)
    {
        var (label, total, count) = Period(s, q);
        return $"إجمالي مبيعات {label}: {Fmt(total)} د.ج في {count} عملية";
    }

    private static (string Label, decimal Total, int Count) Period(StoreSnapshot s, string q)
    {
        if (HasAny(q, WeekMarkers))
        {
            return ("آخر 7 أيام", s.WeekSales, s.WeekCount);
        }

        if (HasAny(q, MonthMarkers))
        {
            return ("هذا الشهر", s.MonthSales, s.MonthCount);
        }

        if (HasAny(q, YesterdayMarkers))
        {
            return ("أمس", s.YesterdaySales, s.YesterdayCount);
        }

        return ("اليوم", s.TodaySales, s.TodayCount);
    }

    private static string Direction(decimal diff)
        => diff > 0 ? "زيادة" : diff < 0 ? "نقص" : "لا فرق";

    private static string Fmt(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    private static bool HasAny(string text, params string[] keywords)
        => TextNormalizer.HasAny(text, keywords);
}
