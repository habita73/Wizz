namespace Wizz.Presentation.Services;

using Wizz.Application.Abstractions;

/// <summary>
/// غلاف لا يُفشل العملية التجارية إذا تعذّرت كتابة سجل التدقيق، لكنه يسجل الخطأ
/// في سجل التطبيق بدل تجاهله بصمت.
/// </summary>
public sealed class SafeAudit
{
    private readonly IAuditLogger _inner;
    private readonly ILogger<SafeAudit> _logger;

    public SafeAudit(IAuditLogger inner, ILogger<SafeAudit> logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public async Task LogAsync(AuditEntry entry)
    {
        try
        {
            await _inner.LogAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "تعذر كتابة سجل التدقيق للإجراء {Action}", entry.Action);
        }
    }
}
