namespace Wizz.Presentation.Pages.AI;

using Wizz.Application.AI.Artifacts;

/// <summary>بيانات رسالة واحدة في محادثة الذكاء الاصطناعي (محادثة مسطّحة مرتّبة بـ Sequence).</summary>
public record ChatItem
{
    /// <summary>معرف الرسالة الفريد.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>معرف الرسالة التي يرد عليها هذا الرد (سؤال المستخدم السابق).</summary>
    public Guid? ParentId { get; init; }

    /// <summary>ترتيب الرسالة داخل المحادثة.</summary>
    public int Sequence { get; init; }

    /// <summary>محتوى الرسالة.</summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>هل الرسالة من المستخدم أم من المساعد؟</summary>
    public bool IsUser { get; init; }

    /// <summary>وقت إرسال الرسالة.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>اسم الموفر الذي أجاب (لرسائل المساعد).</summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>نوع المسار المستخدم في الاستجابة.</summary>
    public string? Route { get; init; }

    /// <summary>هل الإجابة من بحث الإنترنت؟ (تظهر شارة «🌐 إنترنت» مع المصدر).</summary>
    public bool IsWebAnswer { get; init; }

    /// <summary>هل يمكن إعادة توليد هذه الرسالة؟</summary>
    public bool CanRegenerate { get; init; } = true;

    /// <summary>المخرجات (artifacts) المُستخرجة من نص الرد — تُخزَّن لتثبيت الحالة (عرض/حذف).</summary>
    public List<ChatArtifact> Artifacts { get; init; } = new();

    /// <summary>نص الرسالة بعد إزالة وسوم المخرجات (يُملأ عند التحليل).</summary>
    public string? CleanContent { get; init; }
}