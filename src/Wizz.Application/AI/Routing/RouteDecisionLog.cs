namespace Wizz.Application.AI.Routing;

using Wizz.Application.Abstractions;

/// <summary>سطر واحد في سجل قرارات التوجيه.</summary>
public sealed record RouteDecisionEntry(
    DateTime AtUtc,
    string Question,
    RouteType Route,
    string Reason,
    IReadOnlyList<RouteCandidate> Candidates);

/// <summary>
/// سجل قرارات التوجيه التلقائي (لكل جلسة Blazor) — للشفافية ومراجعة سبب كل اختيار.
/// يُخزَّن في الذاكرة فقط ولا يُحفظ على القرص (بيانات تشخيصية مؤقتة).
/// </summary>
public sealed class RouteDecisionLog
{
    /// <summary>الحد الأقصى لعدد القرارات المحفوظة.</summary>
    public const int MaxEntries = 20;

    private readonly List<RouteDecisionEntry> _entries = new();

    /// <summary>عدد القرارات المحفوظة حالياً.</summary>
    public int Count => _entries.Count;

    /// <summary>يضيف قراراً جديداً ويُبقي أحدث <see cref="MaxEntries"/> فقط.</summary>
    public void Add(RouteDecisionEntry entry)
    {
        _entries.Add(entry);
        if (_entries.Count > MaxEntries)
        {
            _entries.RemoveRange(0, _entries.Count - MaxEntries);
        }
    }

    /// <summary>أحدث القرارات (الأحدث أولاً).</summary>
    public IReadOnlyList<RouteDecisionEntry> Recent(int count = 10)
    {
        if (count <= 0)
        {
            return Array.Empty<RouteDecisionEntry>();
        }

        return _entries
            .AsEnumerable()
            .Reverse()
            .Take(count)
            .ToList();
    }

    /// <summary>يمسح السجل بالكامل.</summary>
    public void Clear() => _entries.Clear();
}
