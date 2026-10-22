namespace Wizz.Application.AI.Tools;

/// <summary>
/// سجل أدوات المساعد المدمج: يحتفظ بالأدوات مرتّبة حسب الأولوية،
/// ويجرّبها بالتتابع على السؤال، ويعيد أول إجابة غير فارغة.
/// </summary>
public sealed class BuiltInToolRegistry
{
    private readonly IReadOnlyList<IBuiltInTool> _tools;

    /// <summary>السجل الافتراضي بأدوات النظام الأساسية.</summary>
    public static BuiltInToolRegistry CreateDefault(
        Func<DateTime>? now = null,
        TimeZoneInfo? timeZone = null,
        Func<IReadOnlyList<DriveSpace>>? drives = null,
        Func<PrinterInfo>? printerInfo = null,
        Func<SystemInfo>? systemInfo = null)
        => new(new IBuiltInTool[]
        {
            new GetTimeTool(now, timeZone),
            new GetDriveInfoTool(drives),
            new GetPrintTemplatesTool(printerInfo),
            new GetSystemInfoTool(systemInfo)
        });

    public BuiltInToolRegistry(IEnumerable<IBuiltInTool> tools)
    {
        _tools = tools.OrderBy(t => t.Order).ToList();
    }

    /// <summary>الأدوات المسجّلة (مرتّبة).</summary>
    public IReadOnlyList<IBuiltInTool> Tools => _tools;

    /// <summary>
    /// يجرّب الأدوات بالتتابع. يعيد إجابة أول أداة تُطابق السؤال، أو <c>null</c> إن لم تُطابق أي أداة.
    /// عطل أداة واحدة لا يوقف السلسلة؛ تُسجَّل الأخطاء وتُعرض فقط إن لم تُطابق أي أداة.
    /// </summary>
    public async Task<string?> TryAnswerAsync(BuiltInToolContext context, CancellationToken ct = default)
    {
        var errors = new List<string>();

        foreach (var tool in _tools)
        {
            ct.ThrowIfCancellationRequested();

            string? answer;
            try
            {
                answer = await tool.TryAnswerAsync(context, ct);
            }
            catch (Exception ex)
            {
                // عطل أداة واحدة لا يجب أن يُسقط المساعد المدمج كله — نتجاوزها ونواصل.
                errors.Add($"{tool.Name}: {ex.Message}");
                continue;
            }

            if (!string.IsNullOrWhiteSpace(answer))
                return answer;
        }

        if (errors.Count > 0)
            return "[تعذر تنفيذ بعض الأدوات: " + string.Join("؛ ", errors) + "]";

        return null;
    }
}
