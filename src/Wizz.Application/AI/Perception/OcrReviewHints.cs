namespace Wizz.Application.AI.Perception;

/// <summary>نوع ملاحظة مراجعة تقديرية مستخلصة من النص المقروء كما هو.</summary>
public enum ReviewHintKind
{
    /// <summary>كلمة رقمية قد تكون رقمًا قرأه التعرف كلمةً.</summary>
    NumberWord,

    /// <summary>سطر قصير بلا أرقام خارج صفوف المنتجات، قد يكون شعارًا أو نصًا زخرفيًا.</summary>
    LogoLike
}

/// <summary>
/// ملاحظة مراجعة واحدة. لا تُغيّر النص، ولا تُمرَّر إلى المحكّم، ولا تُكتب أو تُرسل.
/// </summary>
/// <param name="LineNumber">رقم السطر داخل النص المقروء، بدءًا من ١.</param>
/// <param name="Token">الرمز أو الكلمة التي أثارت الملاحظة.</param>
/// <param name="Kind">نوع الملاحظة.</param>
/// <param name="Message">توضيح للعامل.</param>
/// <param name="Line">السطر الخام كما قُرئ.</param>
public sealed record OcrReviewHint(
    int LineNumber,
    string Token,
    ReviewHintKind Kind,
    string Message,
    string Line);

/// <summary>
/// ملاحظات تقديرية للمراجعة فقط على نوعين من التشويش لاحظهما المستخدم: كلمة رقمية قد تكون رقمًا،
/// وسطر قصير بلا أرقام خارج صفوف المنتجات قد يكون شعارًا. القرينة تقريبية وموسومة كذلك،
/// ولا تدخل في حكم المدمج ولا في النص المعروض «كما قُرئ».
/// </summary>
public static class OcrReviewHints
{
    /// <summary>توضيح حدود الملاحظات، يُعرض كما هو في الواجهة.</summary>
    public const string Note =
        "ملاحظات تقديرية للمراجعة فقط: لا تُغيّر النص المقروء، ولا تدخل حكم المدمج، ولا تُكتب أو تُرسل. " +
        "قد تكون خاطئة؛ راجع الصورة الأصلية قبل أي اعتماد.";

    private const int MaxHints = 12;

    // صيغ مكتوبة بعد توحيد الهمزات والتاء المربوطة عبر TextNormalizer، فتُقارَن بالنص المطبَّع.
    private static readonly HashSet<string> NormalizedNumberWords = new(StringComparer.Ordinal)
    {
        "صفر", "واحد", "واحده", "اثنان", "اثنتان", "اثنين", "ثلاثه", "ثلاث",
        "اربعه", "خمسه", "سته", "سبعه", "ثمانيه", "ثمان", "تسعه", "عشره",
        "ميه", "مايه", "الف"
    };

    /// <summary>يحلّل النص المقروء ويعيد ملاحظات للمراجعة فقط، دون أي تعديل عليه.</summary>
    /// <param name="recognizedText">النص الخام كما جاء من القارئ.</param>
    /// <param name="contentLineIndexes">مؤشرات الأسطر التي صُنّفت صفوف منتجات؛ تُستبعد من قرينة الشعار.</param>
    public static IReadOnlyList<OcrReviewHint> Analyze(
        string? recognizedText,
        IReadOnlySet<int> contentLineIndexes)
    {
        if (string.IsNullOrWhiteSpace(recognizedText)) return Array.Empty<OcrReviewHint>();

        var hints = new List<OcrReviewHint>();
        var lines = recognizedText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;

            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var numberWordHintAdded = false;

            foreach (var token in tokens)
            {
                if (!NormalizedNumberWords.Contains(TextNormalizer.Normalize(token))) continue;
                hints.Add(new OcrReviewHint(
                    i + 1, token, ReviewHintKind.NumberWord,
                    "كلمة رقمية؛ قد تكون رقمًا قرأه التعرف كلمةً.", line));
                numberWordHintAdded = true;
                if (hints.Count >= MaxHints) return hints;
            }

            // قرينة الشعار: سطر قصير بلا أرقام، وليس صف منتجات، ولم يُشر إليه ككلمة رقمية.
            if (numberWordHintAdded || contentLineIndexes.Contains(i)) continue;
            if (line.Any(char.IsDigit) || tokens.Length is < 1 or > 2) continue;
            if (!tokens.All(token => token.Length <= 14)) continue;

            hints.Add(new OcrReviewHint(
                i + 1, line, ReviewHintKind.LogoLike,
                "سطر قصير بلا أرقام خارج صفوف المنتجات؛ قد يكون شعارًا أو نصًا زخرفيًا.", line));
            if (hints.Count >= MaxHints) return hints;
        }

        return hints;
    }

    /// <summary>التسمية المعروضة لنوع الملاحظة.</summary>
    public static string Label(ReviewHintKind kind) => kind switch
    {
        ReviewHintKind.NumberWord => "رقم محتمل",
        ReviewHintKind.LogoLike => "شعار محتمل",
        _ => "للمراجعة"
    };
}
