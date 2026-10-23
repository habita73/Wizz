namespace Wizz.Application.AI.Memory;

using System.Text;

/// <summary>
/// تنفيذ نقي لذاكرة المحادثة: قائمة محدودة بآخر التبديلات + آخر موضوع متذكَّر.
/// بلا أي اعتماد على التخزين أو المتصفح — طبقة الواجهة تتولى الحفظ في localStorage.
/// </summary>
public sealed class ConversationMemory : IConversationMemory
{
    /// <summary>الحد الأقصى لعدد التبديلات المحفوظة.</summary>
    public const int MaxTurns = 8;

    /// <summary>الحد الأقصى لطول نص السؤال أو الجواب المخزَّن.</summary>
    public const int MaxTextLength = 400;

    /// <summary>الحد الأقصى لطول الموضوع (اسم المنتج) المخزَّن.</summary>
    public const int MaxSubjectLength = 80;

    /// <summary>الحد الأقصى لطول سطر واحد في سياق المحادثة المُمرَّر للنموذج.</summary>
    private const int MaxContextLineLength = 160;

    private readonly List<ConversationTurn> _turns = new();
    private string? _lastSubject;

    /// <summary>ذاكرة فارغة.</summary>
    public ConversationMemory()
    {
    }

    /// <summary>ذاكرة مُهيّأة بحالة محفوظة (استعادة من localStorage).</summary>
    public ConversationMemory(IEnumerable<ConversationTurn>? initial, string? lastSubject = null)
    {
        foreach (var turn in initial ?? Enumerable.Empty<ConversationTurn>())
        {
            Add(turn);
        }

        SetLastSubject(lastSubject);
    }

    /// <inheritdoc/>
    public int Count => _turns.Count;

    /// <inheritdoc/>
    public ConversationTurn? Last => _turns.Count > 0 ? _turns[^1] : null;

    /// <inheritdoc/>
    public string? LastSubject => _lastSubject;

    /// <inheritdoc/>
    public IReadOnlyList<ConversationTurn> Recent(int maxTurns)
    {
        if (maxTurns <= 0 || _turns.Count == 0)
        {
            return Array.Empty<ConversationTurn>();
        }

        return _turns.Skip(Math.Max(0, _turns.Count - maxTurns)).ToList();
    }

    /// <inheritdoc/>
    public void Add(ConversationTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);

        _turns.Add(turn with
        {
            Question = Truncate(turn.Question, MaxTextLength),
            Answer = Truncate(turn.Answer, MaxTextLength),
            Subject = TruncateOrNull(turn.Subject, MaxSubjectLength),
            Intent = TruncateOrNull(turn.Intent, MaxSubjectLength)
        });

        while (_turns.Count > MaxTurns)
        {
            _turns.RemoveAt(0);
        }

        if (!string.IsNullOrWhiteSpace(turn.Subject))
        {
            SetLastSubject(turn.Subject);
        }
    }

    /// <inheritdoc/>
    public void SetLastSubject(string? subject)
    {
        var trimmed = TruncateOrNull(subject, MaxSubjectLength);
        _lastSubject = trimmed;
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _turns.Clear();
        _lastSubject = null;
    }

    /// <summary>
    /// يبني نصّ سياق مضغوطاً من آخر التبديلات — يُمرَّر في موجّه النظام للنموذج المحلي/السحابي.
    /// </summary>
    public static string BuildContextText(IReadOnlyList<ConversationTurn>? turns, int maxTurns = 3)
    {
        if (turns is null || turns.Count == 0)
        {
            return string.Empty;
        }

        var recent = turns.Skip(Math.Max(0, turns.Count - maxTurns)).ToList();
        if (recent.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("محادثة سابقة (سياق):");
        foreach (var turn in recent)
        {
            sb.AppendLine($"• المستخدم: {OneLine(turn.Question)}");
            sb.AppendLine($"• المساعد: {OneLine(turn.Answer)}");
            if (!string.IsNullOrWhiteSpace(turn.Subject))
            {
                sb.AppendLine($"  (الموضوع: {turn.Subject})");
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? TruncateOrNull(string? value, int maxLength)
    {
        var truncated = Truncate(value, maxLength);
        return string.IsNullOrEmpty(truncated) ? null : truncated;
    }

    private static string OneLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "—";
        }

        var flat = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= MaxContextLineLength ? flat : flat[..MaxContextLineLength] + "…";
    }
}
