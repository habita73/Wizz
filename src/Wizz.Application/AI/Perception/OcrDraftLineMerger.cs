namespace Wizz.Application.AI.Perception;

using Wizz.Application.AI;

/// <summary>
/// يوحّد الأسطر المكررة في نتيجة القراءة قبل التسليم: يُجمّع الكميات لكل اسم مطبَّع، ويحتفظ بأول سعر مقروء.
/// يمنع هذا إضافة الصنف نفسه أكثر من مرة حين يكرره النص المستخرج.
/// </summary>
public static class OcrDraftLineMerger
{
    /// <summary>يعيد قائمة بالأسماء الفريدة وكمياتها المجمّعة، محافظًا على ترتيب الظهور الأول.</summary>
    public static IReadOnlyList<OcrDraftLine> Merge(IEnumerable<OcrDraftLine> lines)
    {
        var keys = new List<string>();
        var byKey = new Dictionary<string, OcrDraftLine>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            var key = TextNormalizer.Normalize(line.Name);
            if (key.Length == 0) continue;

            if (byKey.TryGetValue(key, out var existing))
            {
                byKey[key] = existing with { Quantity = existing.Quantity + line.Quantity };
                continue;
            }

            keys.Add(key);
            byKey[key] = line;
        }

        return keys.Select(key => byKey[key]).ToArray();
    }
}
