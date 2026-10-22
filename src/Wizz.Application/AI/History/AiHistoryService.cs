namespace Wizz.Application.AI.History;

using Wizz.Application.DTOs;

/// <summary>
/// منطق تاريخ أسئلة المساعد وعَتَاد تكرارها — نقي وقابل للاختبار،
/// بلا أي اعتماد على المتصفح أو التخزين. طبقة الواجهة تتولّى حفظ/قراءة localStorage.
/// </summary>
public sealed class AiHistoryService
{
    /// <summary>الحد الأقصى لعدد الأسئلة المحفوظة (الأحدث/الأكثر تكراراً).</summary>
    public const int MaxEntries = 50;

    /// <summary>الحد الأقصى لطول السؤال المخزَّن.</summary>
    public const int MaxQuestionLength = 200;

    private readonly List<AiHistoryEntry> _entries;

    public AiHistoryService(IEnumerable<AiHistoryEntry>? initial = null)
    {
        _entries = initial?.ToList() ?? new List<AiHistoryEntry>();
        PruneAndNormalize();
    }

    /// <summary>كل الأسئلة المحفوظة (نسخة للقراءة فقط).</summary>
    public IReadOnlyList<AiHistoryEntry> Entries => _entries;

    /// <summary>عدد الأسئلة المحفوظة.</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// يسجّل سؤالاً: يزيد عَدّاد التكرار إن وُجد، وإلا يضيفه بوصفه الأحدث.
    /// الأسئلة الفارغة أو المتطابقة بعد التطبيع لا تُكرَّر.
    /// </summary>
    public void Record(string question, DateTime? at = null)
    {
        var clean = Clean(question);
        if (clean.Length == 0) return;

        var key = KeyOf(clean);
        var existing = _entries.FirstOrDefault(e => KeyOf(e.Question) == key);
        var now = at ?? DateTime.UtcNow;

        if (existing is not null)
        {
            _entries.Remove(existing);
            _entries.Add(existing with { Count = existing.Count + 1, LastAskedAt = now });
        }
        else
        {
            _entries.Add(new AiHistoryEntry(clean, 1, now));
        }

        PruneAndNormalize();
    }

    /// <summary>أعلى N أسئلة تكراراً (وعند التساوي: الأحدث استخداماً أولاً).</summary>
    public IReadOnlyList<AiHistoryEntry> Top(int count)
    {
        if (count <= 0) return Array.Empty<AiHistoryEntry>();

        return _entries
            .OrderByDescending(e => e.Count)
            .ThenByDescending(e => e.LastAskedAt)
            .Take(count)
            .ToList();
    }

    /// <summary>آخر N أسئلة (الأحدث أولاً).</summary>
    public IReadOnlyList<AiHistoryEntry> Recent(int count)
        => count <= 0
            ? Array.Empty<AiHistoryEntry>()
            : _entries.OrderByDescending(e => e.LastAskedAt).Take(count).ToList();

    /// <summary>يمسح كل التاريخ.</summary>
    public void Clear() => _entries.Clear();

    /// <summary>يحذف سؤالاً واحداً بمطابقة النص المطبَّع.</summary>
    public bool Remove(string question)
    {
        var key = KeyOf(Clean(question));
        var removed = _entries.RemoveAll(e => KeyOf(e.Question) == key) > 0;
        return removed;
    }

    /// <summary>تطبيع السؤال: تنظيف المسافات وتوحيد الهمزات والأرقام (لمقارنة التكرار).</summary>
    public static string KeyOf(string question)
    {
        var sb = new System.Text.StringBuilder(question.Length);
        foreach (var raw in question.Trim().ToLowerInvariant())
        {
            if (raw is >= '\u0660' and <= '\u0669') { sb.Append((char)('0' + (raw - '\u0660'))); continue; }
            if (raw is >= '\u06F0' and <= '\u06F9') { sb.Append((char)('0' + (raw - '\u06F0'))); continue; }
            if (char.IsWhiteSpace(raw)) { sb.Append(' '); continue; }

            sb.Append(raw switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ؤ' => 'و',
                'ئ' => 'ي',
                'ة' => 'ه',
                '\u064B' or '\u064C' or '\u064D' or '\u064E' or '\u064F' or '\u0650' or '\u0651' or '\u0652' or '\u0640' => '\0',
                _ => raw
            });
        }

        var normalized = sb.ToString().Replace("\0", string.Empty);
        // دمج المسافات المتتالية الناتجة عن إزالة اللاحقات
        while (normalized.Contains("  ")) normalized = normalized.Replace("  ", " ");
        return normalized.Trim();
    }

    /// <summary>تنظيف نص السؤال قبل الحفظ (حدّ الطول + إزالة الأسطر الزائدة).</summary>
    public static string Clean(string? question)
    {
        if (string.IsNullOrWhiteSpace(question)) return string.Empty;

        var one = string.Join(' ', question
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        one = one.Trim();

        return one.Length <= MaxQuestionLength ? one : one[..MaxQuestionLength];
    }

    /// <summary>يبقي الأكثر تكراراً/أحدث استخداماً ضمن الحد الأقصى.</summary>
    private void PruneAndNormalize()
    {
        if (_entries.Count <= MaxEntries) return;

        var keep = _entries
            .OrderByDescending(e => e.Count)
            .ThenByDescending(e => e.LastAskedAt)
            .Take(MaxEntries)
            .ToList();

        _entries.Clear();
        _entries.AddRange(keep);
    }
}

/// <summary>سؤال محفوظ مع عَدّاد تكراره وآخر استخدام.</summary>
public sealed record AiHistoryEntry(string Question, int Count, DateTime LastAskedAt);
