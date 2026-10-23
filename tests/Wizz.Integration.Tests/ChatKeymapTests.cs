using Wizz.Application.AI;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات اختصارات لوحة المفاتيح في صندوق سؤال المساعد (المرحلة 8-3):
/// Enter يرسل، Shift+Enter سطر جديد، Esc يمسح، والحقل الفارغ لا يُرسل.
/// </summary>
public sealed class ChatKeymapTests
{
    [Fact]
    public void Enter_Sends_WhenThereIsText()
        => Assert.Equal(ChatKeyAction.Send, ChatKeymap.Resolve("Enter", false, false, false, true));

    [Fact]
    public void ShiftEnter_InsertsNewLine()
        => Assert.Equal(ChatKeyAction.NewLine, ChatKeymap.Resolve("Enter", false, true, false, true));

    [Fact]
    public void CtrlEnter_Sends()
        => Assert.Equal(ChatKeyAction.Send, ChatKeymap.Resolve("Enter", true, false, false, true));

    [Fact]
    public void Enter_OnEmptyInput_DoesNothing()
        => Assert.Equal(ChatKeyAction.None, ChatKeymap.Resolve("Enter", false, false, false, false));

    [Theory]
    [InlineData("Escape")]
    [InlineData("Esc")]
    public void Escape_ClearsInput(string key)
        => Assert.Equal(ChatKeyAction.ClearInput, ChatKeymap.Resolve(key, false, false, false, true));

    [Fact]
    public void OtherKeys_AreLeftToBrowser()
        => Assert.Equal(ChatKeyAction.None, ChatKeymap.Resolve("a", false, false, false, true));

    [Fact]
    public void Hint_MentionsAllThreeShortcuts()
    {
        Assert.Contains("Enter", ChatKeymap.Hint);
        Assert.Contains("Shift", ChatKeymap.Hint);
        Assert.Contains("Esc", ChatKeymap.Hint);
    }
}
