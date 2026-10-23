namespace Wizz.Application.AI;

/// <summary>الإجراء الذي يُتّخذ عند ضغط مفتاح داخل صندوق سؤال المساعد.</summary>
public enum ChatKeyAction
{
    /// <summary>لا شيء (يُترك السلوك الافتراضي للمتصفح).</summary>
    None,

    /// <summary>إرسال السؤال المكتوب.</summary>
    Send,

    /// <summary>سطر جديد داخل الصندوق.</summary>
    NewLine,

    /// <summary>مسح ما في الصندوق.</summary>
    ClearInput
}

/// <summary>
/// خريطة اختصارات لوحة المفاتيح لصندوق سؤال المساعد (منطق نقي قابل للاختبار،
/// مستقل عن Blazor). القاعدة: Enter يرسل، Shift+Enter سطر جديد، Esc يمسح.
/// </summary>
public static class ChatKeymap
{
    /// <summary>نص التلميح المعروض تحت صندوق الإدخال (يُبنى من نفس القواعد ليبقى متطابقاً).</summary>
    public const string Hint = "⌨️ Enter إرسال · Shift+Enter سطر جديد · Esc مسح";

    /// <summary>يحدّد الإجراء المناسب للمفتاح المضغوط.</summary>
    /// <param name="key">اسم المفتاح كما يعطيه المتصفح (Enter، Escape…).</param>
    /// <param name="ctrlKey">هل Ctrl مضغوط؟ (Ctrl+Enter يرسل أيضاً).</param>
    /// <param name="shiftKey">هل Shift مضغوط؟ (Shift+Enter سطر جديد).</param>
    /// <param name="altKey">هل Alt مضغوط؟ (لا يغيّر السلوك).</param>
    /// <param name="hasText">هل يوجد نص فعلي في الصندوق؟ (Enter على صندوق فارغ لا يرسل).</param>
    public static ChatKeyAction Resolve(string? key, bool ctrlKey, bool shiftKey, bool altKey, bool hasText)
    {
        _ = ctrlKey;
        _ = altKey;

        var name = key ?? string.Empty;

        if (name.Equals("Escape", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("Esc", StringComparison.OrdinalIgnoreCase))
        {
            return ChatKeyAction.ClearInput;
        }

        if (name.Equals("Enter", StringComparison.OrdinalIgnoreCase))
        {
            if (shiftKey)
            {
                return ChatKeyAction.NewLine;
            }

            return hasText ? ChatKeyAction.Send : ChatKeyAction.None;
        }

        return ChatKeyAction.None;
    }
}
