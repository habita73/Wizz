using Wizz.Application.AI.Memory;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ذاكرة المحادثة: الحد الأقصى، التقليم، الموضوع المتذكَّر،
/// نصّ السياق المُمرَّر للنموذج، والمسح.
/// </summary>
public sealed class ConversationMemoryTests
{
    private static DateTime At(int minutes) => new DateTime(2026, 10, 22, 10, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);

    private static ConversationTurn Turn(int index, string? subject = null)
        => new($"سؤال {index}", $"جواب {index}", At(index), subject, subject is null ? null : "سعر");

    [Fact]
    public void Add_StoresTurn_AndCountsIt()
    {
        var memory = new ConversationMemory();

        memory.Add(Turn(1));

        Assert.Equal(1, memory.Count);
        Assert.Equal("سؤال 1", memory.Last!.Question);
    }

    [Fact]
    public void Add_MoreThanMaxTurns_KeepsOnlyNewestOnes()
    {
        var memory = new ConversationMemory();

        for (var i = 1; i <= ConversationMemory.MaxTurns + 5; i++)
        {
            memory.Add(Turn(i));
        }

        Assert.Equal(ConversationMemory.MaxTurns, memory.Count);
        Assert.Equal($"سؤال {ConversationMemory.MaxTurns + 5}", memory.Last!.Question);
    }

    [Fact]
    public void Add_LongText_IsTruncatedToMaxLength()
    {
        var memory = new ConversationMemory();
        var longText = new string('ا', ConversationMemory.MaxTextLength + 200);

        memory.Add(new ConversationTurn(longText, longText, At(1)));

        Assert.Equal(ConversationMemory.MaxTextLength, memory.Last!.Question.Length);
        Assert.Equal(ConversationMemory.MaxTextLength, memory.Last!.Answer.Length);
    }

    [Fact]
    public void Add_WithSubject_SetsLastSubject()
    {
        var memory = new ConversationMemory();

        memory.Add(Turn(1, "تفاح أحمر"));

        Assert.Equal("تفاح أحمر", memory.LastSubject);
    }

    [Fact]
    public void Add_WithoutSubject_KeepsPreviousSubject()
    {
        var memory = new ConversationMemory();
        memory.Add(Turn(1, "تفاح أحمر"));

        memory.Add(Turn(2));

        Assert.Equal("تفاح أحمر", memory.LastSubject);
    }

    [Fact]
    public void SetLastSubject_Blank_ClearsIt()
    {
        var memory = new ConversationMemory();
        memory.SetLastSubject("تفاح أحمر");

        memory.SetLastSubject("   ");

        Assert.Null(memory.LastSubject);
    }

    [Fact]
    public void Recent_ReturnsNewestTurns_UpToLimit()
    {
        var memory = new ConversationMemory();
        for (var i = 1; i <= 5; i++)
        {
            memory.Add(Turn(i));
        }

        var recent = memory.Recent(2);

        Assert.Equal(2, recent.Count);
        Assert.Equal("سؤال 4", recent[0].Question);
        Assert.Equal("سؤال 5", recent[1].Question);
    }

    [Fact]
    public void Recent_ZeroOrNegative_ReturnsEmpty()
    {
        var memory = new ConversationMemory();
        memory.Add(Turn(1));

        Assert.Empty(memory.Recent(0));
        Assert.Empty(memory.Recent(-3));
    }

    [Fact]
    public void Clear_RemovesTurnsAndSubject()
    {
        var memory = new ConversationMemory();
        memory.Add(Turn(1, "تفاح أحمر"));

        memory.Clear();

        Assert.Equal(0, memory.Count);
        Assert.Null(memory.LastSubject);
        Assert.Null(memory.Last);
    }

    [Fact]
    public void BuildContextText_EmptyTurns_ReturnsEmptyString()
    {
        var text = ConversationMemory.BuildContextText(Array.Empty<ConversationTurn>());

        Assert.True(string.IsNullOrWhiteSpace(text));
    }

    [Fact]
    public void BuildContextText_ContainsQuestionsAnswersAndSubject()
    {
        var memory = new ConversationMemory();
        memory.Add(Turn(1, "تفاح أحمر"));

        var text = ConversationMemory.BuildContextText(memory.Recent(3));

        Assert.Contains("محادثة سابقة", text);
        Assert.Contains("سؤال 1", text);
        Assert.Contains("جواب 1", text);
        Assert.Contains("تفاح أحمر", text);
    }

    [Fact]
    public void BuildContextText_LimitsNumberOfTurns()
    {
        var memory = new ConversationMemory();
        for (var i = 1; i <= 5; i++)
        {
            memory.Add(Turn(i));
        }

        var text = ConversationMemory.BuildContextText(memory.Recent(5), maxTurns: 2);

        Assert.Contains("سؤال 5", text);
        Assert.Contains("سؤال 4", text);
        Assert.DoesNotContain("سؤال 3", text);
    }

    [Fact]
    public void Constructor_WithInitialState_RestoresTurnsAndSubject()
    {
        var initial = new[] { Turn(1, "تفاح أحمر"), Turn(2) };

        var memory = new ConversationMemory(initial, "تفاح أحمر");

        Assert.Equal(2, memory.Count);
        Assert.Equal("تفاح أحمر", memory.LastSubject);
    }
}
