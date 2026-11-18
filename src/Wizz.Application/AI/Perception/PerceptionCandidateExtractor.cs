namespace Wizz.Application.AI.Perception;

using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// يستخرج «مرشّحات» من نص ناتج عن تعرّف سابق (OCR/وصف صورة) بوسائل .NET القياسية فقط،
/// دون أي مكتبة خارجية ودون أي اتصال أو كتابة. النص يُفترض أنه مُقدَّم من طبقة التقاط محلية؛
/// هذا الصنف لا يلتقط صورة ولا يرسلها، بل يحوّل النص إلى مرشّحات خام يَرى المدمج وحده حكمها.
/// </summary>
public static class PerceptionCandidateExtractor
{
    private static readonly Regex PricePattern = new(
        @"(?<price>\d+(?:[.,]\d{1,2})?)",
        RegexOptions.Compiled);

    private static readonly string[] LabelWords =
    {
        "سعر", "السعر", "ثمن", "الثمن", "التمن", "بشحال", "شحال", "اسم", "الاسم", "منتج", "المنتج",
        "مادة", "المادة", "المجموع", "الاجمالي", "الإجمالي", "قطعه", "قطعة", "وحده", "وحدة"
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

            var priceMatch = PricePattern.Match(line);
            decimal? price = null;
            if (priceMatch.Success && decimal.TryParse(
                    priceMatch.Groups["price"].Value.Replace(',', '.'),
                    NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture,
                    out var parsed))
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

    private static string? CleanName(string segment)
    {
        var tokens = segment
            .Split(new[] { ' ', '\t', '-', '|', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim(TrimChars))
            .Where(t => t.Length > 0 && !LabelWords.Contains(t, StringComparer.Ordinal))
            .Where(t => !Regex.IsMatch(t, @"^\d"))
            .ToList();

        var name = string.Join(' ', tokens).Trim(TrimChars);
        return name.Length == 0 ? null : name;
    }
}
