namespace Wizz.Application.AI.Memory;

/// <summary>دورة محادثة واحدة (سؤال + جواب) محفوظة في ذاكرة المحادثة.</summary>
/// <param name="Question">نص السؤال كما كتبه المستخدم.</param>
/// <param name="Answer">نص الجواب الذي أعاده المساعد.</param>
/// <param name="AtUtc">وقتالدورة (UTC).</param>
/// <param name="Subject">الموضوع المتذكَّر (اسم المنتج) إن وُجد.</param>
/// <param name="Intent">النية المستخرجة (مثل: سعر).</param>
public sealed record ConversationTurn(
    string Question,
    string Answer,
    DateTime AtUtc,
    string? Subject = null,
    string? Intent = null);
