namespace Wizz.Application.AI.Semantic;

/// <summary>دورة محادثة دلالية محلية (سؤال + فهمه). لا تحتوي أي بيانات مالية أو شخصية.</summary>
/// <param name="Question">نص السؤال كما كتبه المستخدم.</param>
/// <param name="Understanding">الفهم الدلالي المستخرج.</param>
/// <param name="AtUtc">وقت الدورة.</param>
public sealed record SemanticTurn(string Question, SemanticUnderstanding Understanding, DateTime AtUtc);

/// <summary>
/// ذاكرة دلالية قصيرة المدى تعمل محلياً فقط: تحتفظ بالنية والموضوع الأخيرين لفهم أسئلة المتابعة،
/// وتحترم مفتاح «حفظ السياق». لا تكتب في قاعدة البيانات ولا تلمس جداول الأعمال،
/// ولا تُرسل أي شيء إلى أي جهة خارج الجهاز.
/// </summary>
public sealed class SemanticMemory
{
    /// <summary>الحد الأقصى لعدد التبديلات المحفوظة.</summary>
    public const int MaxEntries = 8;

    /// <summary>الحد الأقصى لطول نص السؤال المخزَّن.</summary>
    public const int MaxQuestionLength = 200;

    private readonly List<SemanticTurn> _turns = new();
    private readonly bool _enabled;

    public SemanticMemory(bool enabled = true)
    {
        _enabled = enabled;
    }

    public int Count => _enabled ? _turns.Count : 0;

    public SemanticIntent LastIntent
        => _enabled && _turns.Count > 0
            ? _turns[^1].Understanding.Intent
            : SemanticIntent.General;

    public string? LastSubject
        => _enabled && _turns.Count > 0
            ? _turns[^1].Understanding.ResolvedSubject
            : null;

    /// <summary>آخر <paramref name="maxEntries"/> دورة، الأحدث في النهاية.</summary>
    public IReadOnlyList<SemanticTurn> Recent(int maxEntries)
    {
        if (!_enabled || maxEntries <= 0) return Array.Empty<SemanticTurn>();
        var take = Math.Min(maxEntries, _turns.Count);
        return _turns.GetRange(_turns.Count - take, take).AsReadOnly();
    }

    /// <summary>يفهم السؤال باستخدام هذه الذاكرة، ثم يحفظ الدورة إن كان حفظ السياق مفعّلاً.</summary>
    public SemanticUnderstanding Understand(
        string? question,
        IReadOnlyList<string>? knownProductNames = null)
    {
        var understanding = SemanticIntentResolver.Resolve(question, LastSubject, knownProductNames: knownProductNames);
        Remember(question, understanding);
        return understanding;
    }

    /// <summary>يسجّل دورة دلالية جديدة، ويقليم النصوص والعدد.</summary>
    public void Remember(string? question, SemanticUnderstanding understanding)
    {
        if (!_enabled || string.IsNullOrWhiteSpace(question) || understanding is null) return;

        var clean = question.Trim();
        if (clean.Length > MaxQuestionLength) clean = clean[..MaxQuestionLength];

        _turns.Add(new SemanticTurn(clean, understanding, DateTime.UtcNow));
        if (_turns.Count > MaxEntries) _turns.RemoveRange(0, _turns.Count - MaxEntries);
    }

    /// <summary>يمسح الذاكرة الدلالية المحلية.</summary>
    public void Clear() => _turns.Clear();
}
