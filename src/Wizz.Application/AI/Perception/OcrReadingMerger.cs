namespace Wizz.Application.AI.Perception;

/// <summary>
/// يدمج قراءتين محليتين للصورة نفسها: قراءة عربية أساسية وقراءة فرنسية مساعدة. لا يغيّر أسماء
/// المنتجات، بل يستبدل فقط الرموز الرقمية المشوّشة برقم نظيف قرأته القراءة المساعدة في السطر نفسه.
/// القراءة المدمجة تظل نصًا خامًا تقديريًا، ويبقى المحكّم صاحب الحكم النهائي.
/// </summary>
public static class OcrReadingMerger
{
    // حروف تُخلط غالبًا مع أرقام. تُقبل فقط داخل رمز رقمي خالص.
    private const string DigitConfusions = "OoIiLl|'";

    /// <summary>وسم اللغة للقراءة المدمجة حين تتوفر القراءة المساعدة.</summary>
    public const string MergedLanguageTag = "ar+fr";

    /// <summary>وسم اللغة للقراءة العربية وحدها حين لا تتوفر المساعدة.</summary>
    public const string PrimaryLanguageTag = "ar";

    /// <summary>
    /// يدمج النصين سطرًا بسطر: يُبقي بنية السطور من القراءة الأساسية، ويستبدل كل رمز رقمي
    /// مشوّش بأول رقم نظيف قرأته القراءة المساعدة في السطر نفسه. إن لم يوجد رقم نظيف بقي الأصل.
    /// </summary>
    public static string Merge(string? primaryText, string? secondaryText)
    {
        if (string.IsNullOrWhiteSpace(primaryText)) return primaryText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(secondaryText)) return primaryText;

        var primaryLines = primaryText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var secondaryLines = secondaryText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        var merged = new List<string>(primaryLines.Length);
        for (var i = 0; i < primaryLines.Length; i++)
        {
            var secondary = i < secondaryLines.Length ? secondaryLines[i] : string.Empty;
            merged.Add(MergeLine(primaryLines[i], secondary));
        }

        return string.Join('\n', merged);
    }

    /// <summary>هل الرمز رقمي خالص لكنه يحوي حرفًا مخلوطًا بالأرقام؟</summary>
    private static bool IsGarbledNumericToken(string token)
    {
        if (token.Length < 2) return false;

        var hasConfusion = false;
        foreach (var ch in token)
        {
            if (char.IsDigit(ch) || ch is '.' or ',') continue;
            if (DigitConfusions.Contains(ch))
            {
                hasConfusion = true;
                continue;
            }

            return false;
        }

        return hasConfusion;
    }

    /// <summary>هل الرمز رقم نظيف خالٍ من الحروف المخلوطة؟</summary>
    private static bool IsCleanNumericToken(string token)
    {
        if (token.Length == 0) return false;

        var hasDigit = false;
        foreach (var ch in token)
        {
            if (char.IsDigit(ch))
            {
                hasDigit = true;
                continue;
            }

            if (ch is '.' or ',') continue;
            return false;
        }

        return hasDigit;
    }

    private static string MergeLine(string primaryLine, string secondaryLine)
    {
        var cleanNumbers = secondaryLine
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(IsCleanNumericToken)
            .ToList();

        if (cleanNumbers.Count == 0) return primaryLine;

        var next = 0;
        var tokens = primaryLine.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length && next < cleanNumbers.Count; i++)
        {
            if (!IsGarbledNumericToken(tokens[i])) continue;
            tokens[i] = cleanNumbers[next++];
        }

        return string.Join(' ', tokens);
    }
}
