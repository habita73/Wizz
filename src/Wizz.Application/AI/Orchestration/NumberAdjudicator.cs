namespace Wizz.Application.AI.Orchestration;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// حارس الأرقام: يستخرج الأرقام بصيغة قياسية ويمنع عرض أي رقم لم يأتِ من حزمة حقائق المدمج.
/// إذا احتوت صياغة النموذج على رقم غريب، تُرفض الصياغة كاملة ويُعاد نص المدمج.
/// </summary>
public static class NumberAdjudicator
{
    private static readonly Regex NumericPattern = new(@"\d+(?:[.,]\d+)*", RegexOptions.Compiled);

    /// <summary>نتيجة تحكيم الأرقام.</summary>
    /// <param name="Accepted">هل تُقبل صياغة النموذج؟</param>
    /// <param name="FinalText">النص الذي يُعرض فعلاً للمستخدم.</param>
    /// <param name="Reason">سبب مفهوم يظهر في الواجهة.</param>
    /// <param name="ForeignNumbers">الأرقام غير الموثقة التي أدّت إلى الرفض.</param>
    public sealed record AdjudicationResult(
        bool Accepted,
        string FinalText,
        string Reason,
        IReadOnlyList<string> ForeignNumbers);

    /// <summary>أسباب الرفض الثابتة (تُستخدم في الواجهة والاختبارات).</summary>
    public const string AcceptedReason = "تم تحكيم الأرقام — كل رقم في الرد مصدره المدمج";
    public const string RejectedReason = "رُفضت صياغة تحتوي أرقاماً غير موثقة — أُعيد رد المدمج";

    /// <summary>
    /// يستخرج الأرقام من النص بصيغة قياسية موحّدة (فاصلة آلاف تُحذف، نقطة عشرية تبقى،
    /// والأرقام العربية/الفارسية تُحوَّل إلى اللاتينية عبر <see cref="TextNormalizer"/>).
    /// </summary>
    public static IReadOnlyList<string> ExtractCanonical(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var normalized = TextNormalizer.Normalize(text);
        var values = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in NumericPattern.Matches(normalized))
        {
            var canonical = Canonical(match.Value);
            if (canonical is not null)
            {
                values.Add(canonical);
            }
        }

        return values.OrderBy(v => v, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// يحكّم صياغة النموذج مقابل حقائق المدمج. أي رقم غير موجود في الحزمة يُسقط الصياغة.
    /// </summary>
    public static AdjudicationResult Adjudicate(string? draftedText, FactPackage facts)
    {
        var draft = (draftedText ?? string.Empty).Trim();
        var allowed = new HashSet<string>(facts.NumericTokens, StringComparer.Ordinal);
        var foreign = ExtractCanonical(draft).Where(n => !allowed.Contains(n)).ToList();

        if (foreign.Count > 0)
        {
            return new AdjudicationResult(
                false,
                facts.AuthoritativeText,
                RejectedReason,
                foreign);
        }

        return new AdjudicationResult(true, draft, AcceptedReason, Array.Empty<string>());
    }

    private static string? Canonical(string raw)
    {
        var cleaned = raw.Replace(",", string.Empty);
        if (!decimal.TryParse(cleaned, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }
}
