namespace Wizz.Application.AI.Tools;

/// <summary>
/// أداة نظام/بيانات واحدة يستخدمها المساعد المدمج (بلا نموذج لغوي).
/// كل أداة تعرف كلماتها المفتاحية، وتُجرَّب بالترتيب حتى تُطابق سؤالاً.
/// </summary>
public interface IBuiltInTool
{
    /// <summary>اسم الأداة (للتشخيص والسجل).</summary>
    string Name { get; }

    /// <summary>وصف مختصر لِما تجيبه الأداة.</summary>
    string Description { get; }

    /// <summary>أولوية التجربة (الأقل يُجرَّب أولاً).</summary>
    int Order { get; }

    /// <summary>
    /// يحاول الإجابة على السؤال. يعيد <c>null</c> إذا لم تكن الأداة مسؤولة عن هذا السؤال.
    /// </summary>
    Task<string?> TryAnswerAsync(BuiltInToolContext context, CancellationToken ct = default);
}
