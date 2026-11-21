namespace Wizz.Application.AI.Perception;

using Wizz.Application.AI;

/// <summary>
/// يطابق اسم صنف مقروء على منتج حقيقي تطابقًا تامًا بعد التطبيع، بلا أي تخمين.
/// إذا طابق الاسم أكثر من منتج واحد يُعاد <c>null</c>؛ اختيار أحدها عشوائيًا قد يضع صنفًا خاطئًا في فاتورة.
/// </summary>
public static class OcrProductMatcher
{
    /// <summary>يعيد المنتج الوحيد الذي يطابق الاسم المقروء، أو <c>null</c> عند الغموض أو عدم التطابق.</summary>
    public static T? Match<T>(string? ocrName, IEnumerable<T> products, Func<T, string?> nameSelector)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(ocrName)) return null;

        var wanted = TextNormalizer.Normalize(ocrName);
        if (wanted.Length == 0) return null;

        T? found = null;
        foreach (var product in products)
        {
            if (!string.Equals(TextNormalizer.Normalize(nameSelector(product)), wanted, StringComparison.Ordinal))
            {
                continue;
            }

            if (found is not null)
            {
                return null;
            }

            found = product;
        }

        return found;
    }

    /// <summary>
    /// يقترح منتجات قريبة الاسم للمراجعة فقط، بدون تطبيق تلقائي. يستبعد التطابق التام لأنه يُطبَّق عبر <see cref="Match{T}"/>.
    /// </summary>
    public static IReadOnlyList<T> Suggest<T>(
        string? ocrName,
        IEnumerable<T> products,
        Func<T, string?> nameSelector,
        int max = 3)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(ocrName) || max <= 0) return Array.Empty<T>();

        var wanted = TextNormalizer.Normalize(ocrName);
        if (wanted.Length == 0) return Array.Empty<T>();

        var suggestions = new List<T>();
        foreach (var product in products)
        {
            var candidate = TextNormalizer.Normalize(nameSelector(product));
            if (candidate.Length == 0 || string.Equals(candidate, wanted, StringComparison.Ordinal)) continue;
            if (!IsNear(wanted, candidate)) continue;

            suggestions.Add(product);
            if (suggestions.Count >= max) break;
        }

        return suggestions;
    }

    private static bool IsNear(string left, string right)
    {
        if (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal))
        {
            return true;
        }

        var leftTokens = left.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3)
            .ToHashSet(StringComparer.Ordinal);
        var rightTokens = right.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3)
            .ToHashSet(StringComparer.Ordinal);

        return leftTokens.Overlaps(rightTokens);
    }
}
