namespace Wizz.Application.AI.Perception;

using System.Globalization;
using System.Text.RegularExpressions;
using Wizz.Application.AI;

/// <summary>
/// يستخرج الكمية من السطر الخام المقروء بأسلوب متحفظ: إن لم يبقَ رقم واحد لا لبس فيه بعد إزالة اسم الصنف
/// والسعر، تُعيد الكمية الافتراضية ١. لا تخمين، ولا رقم يتجاوز حدًا معقولًا.
/// </summary>
public static class OcrQuantityParser
{
    private const decimal DefaultQuantity = 1m;
    private const decimal MaxQuantity = 1000m;

    private static readonly Regex NumberToken = new(
        @"(?<![\d.,])(\d{1,4}(?:[.,]\d{1,3})?)(?![\d.,])",
        RegexOptions.Compiled);

    /// <summary>يعيد الكمية المقروءة، أو ١ عند الغموض أو عدم وجود رقم صريح.</summary>
    public static decimal Parse(string? rawLine, string? productName, decimal? price)
    {
        if (string.IsNullOrWhiteSpace(rawLine)) return DefaultQuantity;

        var normalized = TextNormalizer.Normalize(rawLine);
        if (!string.IsNullOrWhiteSpace(productName))
        {
            var name = TextNormalizer.Normalize(productName);
            if (name.Length > 0)
            {
                normalized = normalized.Replace(name, " ", StringComparison.Ordinal);
            }
        }

        var tokens = NumberToken.Matches(normalized).Cast<Match>().ToList();
        if (tokens.Count == 0) return DefaultQuantity;

        if (price is { } knownPrice)
        {
            tokens = tokens.Where(token => ParseDecimal(token.Value) != knownPrice).ToList();
        }

        if (tokens.Count != 1) return DefaultQuantity;

        var value = ParseDecimal(tokens[0].Value);
        return value is >= DefaultQuantity and <= MaxQuantity ? value : DefaultQuantity;
    }

    private static decimal ParseDecimal(string raw) =>
        decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : DefaultQuantity;
}
