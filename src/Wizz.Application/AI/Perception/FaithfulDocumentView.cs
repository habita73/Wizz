namespace Wizz.Application.AI.Perception;

/// <summary>نوع كتلة العرض الأمين للنص المقروء.</summary>
public enum FaithfulBlockKind
{
    /// <summary>سطر واحد كما قُرئ، منقولًا حرفيًا.</summary>
    Line = 0,

    /// <summary>صفوف متتالية تُعرض كجدول نصي؛ خلاياها كلمات السطور كما قُرئت.</summary>
    Table
}

/// <summary>
/// كتلة عرض أمين للنص المقروء. <c>Lines</c> يحمل النص الأصلي حرفيًا: سطرًا واحدًا حين تكون الكتلة
/// <c>Line</c>، وكل أسطر الصفوف حين تكون <c>Table</c>. <c>Rows</c> يحمل الخلايا المقسّمة عند
/// <c>Table</c> فقط. لا تُصحَّح الكلمات ولا تُعاد ترتيبها ولا تُحذف.
/// </summary>
/// <param name="Part">الجزء المقدَّر للسطر أو للصفوف.</param>
/// <param name="Kind">نوع الكتلة.</param>
/// <param name="Lines">النص الأصلي كما قُرئ، بلا تعديل.</param>
/// <param name="Rows">خلايا الجدول النصي حين تكون الكتلة جدولًا.</param>
public sealed record FaithfulBlock(
    EstimatedDocumentPart Part,
    FaithfulBlockKind Kind,
    IReadOnlyList<string> Lines,
    IReadOnlyList<IReadOnlyList<string>> Rows);

/// <summary>
/// يحوّل أسطر التقرير إلى كتل عرض أمين: يحتفظ بكل سطر كما قُرئ وبترتيبه، ويجمّع الصفوف ذات
/// المظهر الجدولي في جدول نصي. لا يقرأ قاعدة بيانات، لا يرسل، لا يحذف، ولا يصحّح أي حرف.
/// </summary>
public static class FaithfulDocumentView
{
    private static readonly char[] WordSeparators = { ' ', '\t' };

    /// <summary>يبني كتل العرض الأمين من أسطر التقرير المقدَّرة.</summary>
    /// <param name="partLines">أسطر التقرير كما قُرئت مع أجزائها.</param>
    /// <returns>كتل تحفظ كل سطر كما قُرئ وبترتيبه.</returns>
    public static IReadOnlyList<FaithfulBlock> Build(IReadOnlyList<DecisionReportPartLine> partLines)
    {
        ArgumentNullException.ThrowIfNull(partLines);

        var result = new List<FaithfulBlock>();
        var i = 0;
        while (i < partLines.Count)
        {
            var current = partLines[i];
            if (LooksLikeTableRow(current.Text))
            {
                var rows = new List<IReadOnlyList<string>>();
                var sourceLines = new List<string>();
                var j = i;
                while (j < partLines.Count
                       && partLines[j].Part == current.Part
                       && LooksLikeTableRow(partLines[j].Text))
                {
                    sourceLines.Add(partLines[j].Text);
                    rows.Add(SplitCells(partLines[j].Text));
                    j++;
                }

                result.Add(new FaithfulBlock(current.Part, FaithfulBlockKind.Table, sourceLines, rows));
                i = j;
            }
            else
            {
                result.Add(new FaithfulBlock(
                    current.Part,
                    FaithfulBlockKind.Line,
                    new[] { current.Text },
                    Array.Empty<IReadOnlyList<string>>()));
                i++;
            }
        }

        return result;
    }

    /// <summary>
    /// صف جدولي إن حوى ثلاث كلمات على الأقل ورقمين عشريّين على الأقل. معيار عرضي تقريبي فقط؛
    /// لا يُغيّر النص ولا يُصنّفه، والسطر الذي لا يُطابقه يظهر كسطر عادي بلا حذف.
    /// </summary>
    /// <param name="text">السطر المقروء كما هو.</param>
    /// <returns><c>true</c> إن بدا صفًا جدوليًا؛ للعرض فقط.</returns>
    public static bool LooksLikeTableRow(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var tokens = text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length >= 3 && tokens.Count(IsDecimalNumber) >= 2;
    }

    /// <summary>يقسّم السطر إلى خلايا نصية كما قُرئت، بلا تعديل.</summary>
    /// <param name="text">السطر المقروء كما هو.</param>
    /// <returns>الكلمات كما قُرئت، بترتيبها.</returns>
    public static IReadOnlyList<string> SplitCells(string text) =>
        text.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries);

    private static bool IsDecimalNumber(string token)
    {
        var hasDigit = false;
        var hasSeparator = false;
        foreach (var c in token)
        {
            if (char.IsDigit(c))
            {
                hasDigit = true;
                continue;
            }

            if (c is '.' or ',')
            {
                hasSeparator = true;
                continue;
            }

            return false;
        }

        return hasDigit && hasSeparator;
    }
}
