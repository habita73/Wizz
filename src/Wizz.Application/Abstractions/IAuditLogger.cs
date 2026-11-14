namespace Wizz.Application.Abstractions;

using Wizz.Domain;

/// <summary>مدخل سجل تدقيق — يُبنى في الواجهة أو حالة الاستخدام ثم يُحفظ مستقلًا.</summary>
public sealed record AuditEntry(
    AuditAction Action,
    AuditOutcome Outcome,
    Guid? UserId = null,
    string? Username = null,
    string? EntityType = null,
    string? EntityId = null,
    string? Details = null,
    string? IpAddress = null);

/// <summary>كتابة سجل التدقيق. لا تُمرَّر كلمات مرور أو أسرار في <see cref="AuditEntry.Details"/>.</summary>
public interface IAuditLogger
{
    Task LogAsync(AuditEntry entry, CancellationToken ct = default);
}
