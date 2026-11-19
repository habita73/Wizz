namespace Wizz.Application.AI.Perception;

using System.Globalization;

/// <summary>
/// قرينة للمراجعة فقط: يقترح تصحيحات مقيدة لأكثر أخطاء التعرف شيوعًا في الرموز الرقمية
/// (مثل <c>I.OOO</c> بدل <c>1.000</c>، و<c>OO</c> بدل <c>00</c>، و<c>ll</c> بدل <c>11</c>).
/// لا يغيّر النص الذي يدخل التحكيم، ولا يلمس أسماء المنتجات، ولا يُصدر حكمًا؛
/// كل ما يفعله أن يعرض احتمالًا بجانب النص الأصلي ليقرّر العامل وحده.
/// </summary>
public static class OcrNumericCleanup
{
    /// <summary>توضيح حدود هذه الاحتمالات، يُعرض كما هو في الواجهة.</summary>
    public const string Note =
        "احتمالات تصحيح مقيدة للأرقام فقط، للمراجعة البشرية. النص الأصلي كما قرئه التعرف لم يُغيَّر، " +
        "ولم يدخل أي احتمال في حكم المدمج؛ لا تُعتمد الصيغة المقترحة إلا بقرارة منك.";

    /// <summary>احتمال تصحيح رقمي واحد، مع إبقاء النص الأصلي والسطر ظاهرين.</summary>
    /// <param name="LineNumber">رقم السطر داخل النص المقروء، بدءًا من ١.</param>
    /// <param name="Original">الرمز كما قرأه التعرف.</param>
    /// <param name="Suggested">الصيغة الرقمية المقترحة (ليست حكمًا).</param>
    /// <param name="Line">السطر الخام كما قرئ، للمراجعة.</param>
    public sealed record Hint(int LineNumber, string Original, string Suggested, string Line);

    /// <summary>نتيجة التحليل: قائمة بالاحتمالات فقط، بلا أي تعديل على النص.</summary>
    /// <param name="Hints">الاحتمالات المرتبة حسب ورودها في النص.</param>
    public sealed record Analysis(IReadOnlyList<Hint> Hints)
    {
        /// <summary>هل وُجد ما يستحق عرضه للمستخدم؟</summary>
        public bool HasHints => Hints.Count > 0;
    }

    // حروف تُخلط غالبًا مع أرقام في مخرجات التعرف. لا تُطبَّق إلا داخل رمز رقمي خالص.
    private const string DigitConfusions = "OoIl|i";

    /// <summary>يحلّل النص المقروء ويعيد احتمالات التصحيح، دون تعديل أي حرف فيه.</summary>
    public static Analysis Analyze(string? recognizedText)
    {
        if (string.IsNullOrWhiteSpace(recognizedText)) return new Analysis(Array.Empty<Hint>());

        var hints = new List<Hint>();
        var lines = recognizedText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            foreach (var token in line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!TrySuggest(token, out var suggested)) continue;
                hints.Add(new Hint(i + 1, token, suggested, line.Trim()));
            }
        }

        return new Analysis(hints);
    }

    /// <summary>
    /// يقترح صيغة رقمية لرمز واحد إن كان رمزًا رقميًا خالصًا يحوي خطأً معروفًا.
    /// الرموز التي تحوي أي حرف عربي أو حرفًا خارج مجموعة الالتباس تُستبعد تمامًا،
    /// فلا تُلمس أسماء المنتجات ولا كلمات الوثيقة.
    /// </summary>
    private static bool TrySuggest(string token, out string suggested)
    {
        suggested = string.Empty;
        if (token.Length < 2) return false;

        var hasConfusable = false;
        foreach (var ch in token)
        {
            var isDigit = ch is >= '0' and <= '9';
            var isSeparator = ch is '.' or ',';
            var isConfusion = DigitConfusions.Contains(ch);
            if (!isDigit && !isSeparator && !isConfusion) return false;
            if (isConfusion) hasConfusable = true;
        }

        if (!hasConfusable) return false;

        var mapped = new char[token.Length];
        for (var i = 0; i < token.Length; i++)
        {
            var ch = token[i];
            mapped[i] = ch switch
            {
                'O' or 'o' => '0',
                'I' or 'l' or 'i' or '|' => '1',
                _ => ch
            };
        }

        var candidate = new string(mapped);
        if (string.Equals(candidate, token, StringComparison.Ordinal)) return false;

        // لا يُعرض إلا إن كانت الصيغة المقترحة رقمًا صالحًا فعلًا.
        if (!decimal.TryParse(
                candidate.Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out _))
        {
            return false;
        }

        suggested = candidate;
        return true;
    }
}
