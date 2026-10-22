namespace Wizz.Application.AI.Tools;

using System.Globalization;
using System.Text;

/// <summary>
/// أداة الوقت والتاريخ: تجيب عن «كم الساعة»، «ما تاريخ اليوم»، «أي يوم نحن»،
/// وتُظهر أيضاً التاريخ الهجري التقريبي ومنطقة التوقيت المحلية.
/// </summary>
public sealed class GetTimeTool : IBuiltInTool
{
    private static readonly string[] Keywords =
    {
        "الساعه", "كم الساعه", "الوقت", "كم الوقت", "time",
        "التاريخ", "تاريخ اليوم", "اليوم كم", "ما هو اليوم", "اي يوم"
    };

    private readonly Func<DateTime> _now;
    private readonly TimeZoneInfo _timeZone;

    /// <param name="now">مزوّد الوقت (قابل للحقن للاختبار). الافتراضي <see cref="DateTime.Now"/>.</param>
    /// <param name="timeZone">المنطقة الزمنية للعرض. الافتراضي المنطقة المحلية للنظام.</param>
    public GetTimeTool(Func<DateTime>? now = null, TimeZoneInfo? timeZone = null)
    {
        _now = now ?? (() => DateTime.Now);
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public string Name => "GetTime";

    public string Description => "الوقت والتاريخ المحلي (ميلادي + هجري تقريبي).";

    public int Order => 10;

    public Task<string?> TryAnswerAsync(BuiltInToolContext context, CancellationToken ct = default)
    {
        var q = context.Question;

        var asksTime = HasAny(q, "الساعه", "الوقت", "time");
        var asksDate = HasAny(q, "التاريخ", "تاريخ اليوم", "اي يوم", "اليوم كم", "ما هو اليوم");

        if (!asksTime && !asksDate)
            return Task.FromResult<string?>(null);

        // إن كان الوقت المُزوَّد بلا نوع (Unspecified) نعتبره بتوقيت المنطقة المعروضة مباشرة،
        // كي لا نُزيحه مرتين. وإن كان UTC/محلياً نحوّله للمنطقة المطلوبة.
        var raw = _now();
        var now = raw.Kind == DateTimeKind.Unspecified
            ? raw
            : TimeZoneInfo.ConvertTime(raw, _timeZone);

        var sb = new StringBuilder();
        sb.AppendLine("🕐 الوقت الحالي:");
        sb.AppendLine($"• الساعة: {now:HH:mm}");
        sb.AppendLine($"• التاريخ الميلادي: {now:dddd d MMMM yyyy}");
        sb.AppendLine($"• المنطقة الزمنية: {_timeZone.DisplayName}");

        if (TryHijri(now, out var hijri))
            sb.AppendLine($"• التاريخ الهجري (تقريبي): {hijri}");

        return Task.FromResult<string?>(sb.ToString().TrimEnd());
    }

    /// <summary>تحويل هجري تقريبي (حسابي) عبر تقويم أم القرى إن كان مدعوماً في النظام.</summary>
    private static bool TryHijri(DateTime date, out string result)
    {
        result = string.Empty;
        try
        {
            var calendar = new UmAlQuraCalendar();
            var year = calendar.GetYear(date);
            var month = calendar.GetMonth(date);
            var day = calendar.GetDayOfMonth(date);
            result = $"{day} {HijriMonth(month)} {year} هـ";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string HijriMonth(int month) => month switch
    {
        1 => "محرم",
        2 => "صفر",
        3 => "ربيع الأول",
        4 => "ربيع الثاني",
        5 => "جمادى الأولى",
        6 => "جمادى الثانية",
        7 => "رجب",
        8 => "شعبان",
        9 => "رمضان",
        10 => "شوال",
        11 => "ذو القعدة",
        12 => "ذو الحجة",
        _ => month.ToString(CultureInfo.InvariantCulture)
    };

    private static bool HasAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k, StringComparison.Ordinal));
}
