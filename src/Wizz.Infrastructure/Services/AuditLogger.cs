namespace Wizz.Infrastructure.Services;

using Wizz.Application.Abstractions;
using Wizz.Domain;
using Wizz.Infrastructure.Data;

/// <summary>كتابة سجل التدقيق في قاعدة البيانات عبر <see cref="WizzDbContext"/> المشترك.</summary>
public sealed class AuditLogger : IAuditLogger
{
    private readonly WizzDbContext _db;

    public AuditLogger(WizzDbContext db) => _db = db;

    public async Task LogAsync(AuditEntry entry, CancellationToken ct = default)
    {
        var log = new AuditLog(
            entry.Action,
            entry.Outcome,
            entry.UserId,
            entry.Username,
            entry.EntityType,
            entry.EntityId,
            entry.Details,
            entry.IpAddress);

        await _db.AuditLogs.AddAsync(log, ct);
        await _db.SaveChangesAsync(ct);
    }
}
