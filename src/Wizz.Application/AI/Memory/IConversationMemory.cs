namespace Wizz.Application.AI.Memory;

/// <summary>
/// ذاكرة محادثة قصيرة المدى: تحتفظ بآخر التبديلات وآخر موضوع (منتج) متذكَّر،
/// ليستطيع المساعد فهم أسئلة المتابعة («كم سعره؟») وتغذية النموذج بسياق سابق.
/// </summary>
public interface IConversationMemory
{
    /// <summary>عدد التبديلات المحفوظة حالياً.</summary>
    int Count { get; }

    /// <summary>آخر دورة محادثة، أو null إن كانت الذاكرة فارغة.</summary>
    ConversationTurn? Last { get; }

    /// <summary>آخر موضوع (اسم منتج) متذكَّر، أو null.</summary>
    string? LastSubject { get; }

    /// <summary>آخر <paramref name="maxTurns"/> دورة (الأحدث في نهاية القائمة).</summary>
    IReadOnlyList<ConversationTurn> Recent(int maxTurns);

    /// <summary>تسجيل دورة جديدة (مع تقليم النصوص وحدّ الذاكرة).</summary>
    void Add(ConversationTurn turn);

    /// <summary>تعيين/مسح الموضوع المتذكَّر.</summary>
    void SetLastSubject(string? subject);

    /// <summary>مسح كل الذاكرة (التبديلات والموضوع).</summary>
    void Clear();
}
