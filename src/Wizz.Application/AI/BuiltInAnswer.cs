namespace Wizz.Application.AI;

/// <summary>
/// نتيجة إجابة المساعد المدمج: النص المعروض + الموضوع المتذكَّر (اسم المنتج) + النية.
/// تُستخدم لتغذية ذاكرة المحادثة وفهم أسئلة المتابعة.
/// </summary>
/// <param name="Text">نص الجواب.</param>
/// <param name="Subject">الموضوع الذي تدور عليه الجواب (اسم منتج) إن وُجد.</param>
/// <param name="Intent">النية المستخرجة (مثل: سعر، مبيعات، ديون).</param>
public sealed record BuiltInAnswer(string Text, string? Subject = null, string? Intent = null);
