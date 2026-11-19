namespace Wizz.Application.AI.Perception;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// يستخرج «مرشّحات» من نص ناتج عن تعرّف سابق (OCR/وصف صورة) بوسائل .NET القياسية فقط،
/// دون أي مكتبة خارجية ودون أي اتصال أو كتابة. النص يُفترض أن يُقدَّم من طبقة التقاط محلية؛
/// هذا الصنف لا يلتقط صورة ولا يرسلها، بل يحوّل النص إلى مرشّحات خام يرى المدمج وحده حكمها.
/// </summary>
public static class PerceptionCandidateExtractor
{
    private static readonly Regex PricePattern = new(
        @"(?<price>\d+(?:[.,]\d{1,2})?)",
        RegexOptions.Compiled);

    // نمط أوسع للصفوف المكتظة: يلتقط الكميات مثل 1.000 لاستبعادها من الأسعار.
    private static readonly Regex DenseNumberPattern = new(
        @"\d+(?:[.,]\d+)?",
        RegexOptions.Compiled);

    private static readonly string[] LabelWords =
    {
        "سعر", "السعر", "ثمن", "الثمن", "التمن", "بشحال", "شحال", "اسم", "الاسم", "منتج", "المنتج",
        "مادة", "المادة", "المجموع", "الاجمالي", "الإجمالي", "قطعه", "قطعة", "وحده", "وحدة",
        "كغ", "حبه", "حبة", "علبه", "علبة", "الكميه", "الكمية", "الوحده", "الوحدة"
    };

    private static readonly char[] TrimChars = { ' ', '\t', '*', '-', '•', ':', '؛', '|', '.', '،' };

    /// <summary>يحوّل نص التعرّف إلى مرشّحات. سطر فارغ أو بلا محتوى مُفيد يُتجاهل.</summary>
    public static IReadOnlyList<PerceptionCandidate> Extract(string? recognizedText, PerceptionSource source = PerceptionSource.Embedded)
    {
        var result = new List<PerceptionCandidate>();
        if (string.IsNullOrWhiteSpace(recognizedText)) return result;

        var normalized = TextNormalizer.Normalize(recognizedText);
        foreach (var rawLine in normalized.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            var denseMatches = DenseNumberPattern.Matches(line);
            var firstPrice = PricePattern.Match(line);

            // قرة Windows تُخرج النص بترتيبه البصري: الرقم قبل الاسم، وقد تجمع عدة منتجات في سطر واحد
            // (مثال: "12.5 حليب 5 خبز 150 تفاح"). هذا النمط يعني أن كل رقم متبوع باسم قبلي، بلا رقمين
            // متجاورين. إن وُجد رقم بلا اسم بعده (كمية أو مجموع سطر) فالصف مكتظ ويُعالَج أدناه؛ بهذا
            // لا يُقرأ الرقم الزائد في الكمية (1.000 ← 1.00 + 0) سعرًا مستقلًا.
            if (firstPrice.Success && firstPrice.Index == 0 && IsNumberFirstList(line, denseMatches))
            {
                result.AddRange(ExtractNumberFirstLine(line, source));
                continue;
            }

            // صفوف الوصولات المكتظة قد تحوي أكثر من رقم (كمية وسعر وحدة ومجموع سطر).
            // نُنتج مرشّحات لكل صيغة اسم محتملة وكل سعر، والمدمج وحده يقرّر أيّها يُعتمد.
            if (denseMatches.Count > 1)
            {
                var dense = ExtractDenseLine(line, source);
                if (dense.Count > 0)
                {
                    result.AddRange(dense);
                    continue;
                }
            }

            // النمط المعتاد (الاسم ثم السعر) — سلوكه محفوظ كما كان تمامًا.
            var priceMatch = firstPrice;
            decimal? price = null;
            if (priceMatch.Success && TryParsePrice(priceMatch.Value, out var parsed))
            {
                price = parsed;
            }

            var namePart = priceMatch.Success
                ? line.Remove(priceMatch.Index, priceMatch.Length)
                : line;
            var productName = CleanName(namePart);

            // مرشّح يُسجَّل حتى لو ناقص — المدمج هو من يرفضه لاحقاً، لا يُسقط هنا صامتاً.
            var confidence = productName is not null && price.HasValue
                ? 1.0
                : productName is not null || price.HasValue ? 0.5 : 0.0;

            result.Add(new PerceptionCandidate(
                productName,
                price,
                line,
                confidence,
                source));
        }

        return result;
    }

    /// <summary>
    /// يعالج سطرًا يبدأ برقم (نمط قرة Windows): يقرن كل رقم بالقطعة التي تليه حتى الرقم التالي،
    /// فيُنتج مرشّحًا لكل منتج. القطعة بلا اسم تُتجاهل — لا يُختلق رقم ولا اسم.
    /// </summary>
    private static IReadOnlyList<PerceptionCandidate> ExtractNumberFirstLine(string line, PerceptionSource source)
    {
        var result = new List<PerceptionCandidate>();
        var matches = PricePattern.Matches(line);

        for (var i = 0; i < matches.Count; i++)
        {
            var priceMatch = matches[i];
            if (!TryParsePrice(priceMatch.Value, out var price)) continue;

            var nameStart = priceMatch.Index + priceMatch.Length;
            var nameEnd = i + 1 < matches.Count ? matches[i + 1].Index : line.Length;
            var nameSegment = line[nameStart..nameEnd];
            var productName = CleanName(nameSegment);
            if (productName is null) continue;

            result.Add(new PerceptionCandidate(productName, price, line, 1.0, source));
        }

        return result;
    }

    /// <summary>
    /// يقرّر هل الصف نمط «رقم ثم اسم» لقرة Windows: كل رقم متبوع بقطعة اسم قبل الرقم التالي،
    /// بلا رقمين متجاورين. رقمان متجاوران يعنيان صف وصل مكتظ (كمية + سعر + مجموع).
    /// </summary>
    private static bool IsNumberFirstList(string line, MatchCollection numbers)
    {
        for (var i = 0; i < numbers.Count - 1; i++)
        {
            var between = line[(numbers[i].Index + numbers[i].Length)..numbers[i + 1].Index];
            if (string.IsNullOrWhiteSpace(between)) return false;
        }

        if (numbers.Count > 0)
        {
            var last = numbers[^1];
            if (string.IsNullOrWhiteSpace(line[(last.Index + last.Length)..])) return false;
        }

        return true;
    }

    /// <summary>
    /// يعالج صفًا يضم أكثر من رقم. الكميات (ذات الخانات العشرية الثلاث) تُستبعد،
    /// ويُكوَّن مرشّح للاسم الكامل ولكل كلمة مفردة مقابل كل سعر محتمل.
    /// لا يُختلق اسم ولا رقم، والمدمج وحده يحكم على المرشّحات.
    /// </summary>
    private static IReadOnlyList<PerceptionCandidate> ExtractDenseLine(string line, PerceptionSource source)
    {
        var matches = DenseNumberPattern.Matches(line);
        var prices = new List<decimal>();
        foreach (Match match in matches)
        {
            if (!IsPriceToken(match.Value)) continue;
            if (!TryParsePrice(match.Value, out var price)) continue;
            if (!prices.Contains(price)) prices.Add(price);
        }
        if (prices.Count == 0) return Array.Empty<PerceptionCandidate>();

        var withoutNumbers = line;
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            withoutNumbers = withoutNumbers.Remove(matches[i].Index, matches[i].Length);
        }

        var tokens = CleanTokens(withoutNumbers);
        if (tokens.Count == 0) return Array.Empty<PerceptionCandidate>();

        var names = new List<string> { string.Join(' ', tokens) };
        names.AddRange(tokens.Where(t => t.Length >= 2));

        var result = new List<PerceptionCandidate>();
        foreach (var name in names.Distinct(StringComparer.Ordinal))
        {
            foreach (var price in prices)
            {
                if (result.Count >= 24) return result;
                result.Add(new PerceptionCandidate(name, price, line, 0.8, source));
            }
        }
        return result;
    }

    private static bool IsPriceToken(string raw)
    {
        var separator = raw.IndexOfAny(new[] { '.', ',' });
        return separator < 0 || raw.Length - separator - 1 <= 2;
    }

    private static bool TryParsePrice(string raw, out decimal price) =>
        decimal.TryParse(
            raw.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out price);

    private static string? CleanName(string segment)
    {
        var name = string.Join(' ', CleanTokens(segment)).Trim(TrimChars);
        return name.Length == 0 ? null : name;
    }

    private static IReadOnlyList<string> CleanTokens(string segment) =>
        segment
            .Split(new[] { ' ', '\t', '-', '|', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim(TrimChars))
            .Where(t => t.Length > 0 && !LabelWords.Contains(t, StringComparer.Ordinal))
            .Where(t => !t.Any(char.IsDigit))
            .ToList();
}
