namespace Wizz.Application.AI.Perception;

/// <summary>موضع كلمة كما أخرجها قارئ الصور؛ يُستخدم محليًا لإعادة بناء الأسطر.</summary>
/// <param name="Text">نص الكلمة كما قرئ، بلا تعديل.</param>
/// <param name="Left">إحداثي الحافة اليسرى للكلمة.</param>
/// <param name="CenterY">إحداثي مركز الكلمة رأسيًا.</param>
/// <param name="Height">ارتفاع مستطيل الكلمة.</param>
public sealed record OcrWordBox(string Text, double Left, double CenterY, double Height);

/// <summary>
/// يعيد بناء أسطر الصفحة من مواضع الكلمات: حدّ التجميع نسبي لارتفاع الكلمات لا رقم ثابت،
/// ومستوى السطر يُحدَّث أثناء الجمع ليصمد أمام الميل البسيط. الترتيب داخل السطر من اليمين لليسار.
/// لا يضيف نصًا ولا يحذف كلمة؛ يستعيد بنية الصفحة فقط، وينتج نصًا خامًا تقديريًا.
/// </summary>
public static class OcrRowReconstructor
{
    /// <summary>نسبة ارتفاع الكلمة التي تُستخدم حدًا لتجميع الكلمات في سطر واحد.</summary>
    public const double RelativeThresholdFactor = 0.6;

    /// <summary>حد أدنى بالبكسل حين تكون الكلمات صغيرة جدًا.</summary>
    public const double MinimumThresholdPixels = 6.0;

    /// <summary>يعيد الأسطر مفصولة بفاصل صريح، أو نصًا فارغًا إن لم توجد كلمات.</summary>
    public static string Reconstruct(IReadOnlyList<OcrWordBox> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        var usable = words.Where(word => !string.IsNullOrWhiteSpace(word.Text)).ToList();
        if (usable.Count == 0) return string.Empty;

        var threshold = ThresholdFor(usable);
        var ordered = usable
            .OrderBy(word => word.CenterY)
            .ThenByDescending(word => word.Left)
            .ToList();

        var rows = new List<string>();
        var current = new List<OcrWordBox> { ordered[0] };
        var baseline = ordered[0].CenterY;

        void Flush()
        {
            if (current.Count == 0) return;
            rows.Add(string.Join(' ', current
                .OrderByDescending(word => word.Left)
                .Select(word => word.Text)));
            current = new List<OcrWordBox>();
        }

        foreach (var word in ordered.Skip(1))
        {
            if (Math.Abs(word.CenterY - baseline) > threshold)
            {
                Flush();
                current.Add(word);
                baseline = word.CenterY;
                continue;
            }

            current.Add(word);
            baseline = current.Average(item => item.CenterY);
        }

        Flush();
        return string.Join('\n', rows);
    }

    private static double ThresholdFor(IReadOnlyList<OcrWordBox> words)
    {
        var heights = words.Select(word => word.Height).Where(height => height > 0).Order().ToList();
        if (heights.Count == 0) return 16.0;

        var middle = heights.Count / 2;
        var median = heights.Count % 2 == 0
            ? (heights[middle - 1] + heights[middle]) / 2.0
            : heights[middle];

        return Math.Max(MinimumThresholdPixels, median * RelativeThresholdFactor);
    }
}
