namespace Wizz.Application.AI.Tools;

using System.Text;

/// <summary>
/// أداة معلومات الأقراص: تجيب عن «مساحة القرص»، «كم مساحة متبقية»، «القرص ممتلئ؟».
/// مفيدة لأن التطبيق محلي ويخزّن قاعدة البيانات والنسخ الاحتياطي على القرص.
/// </summary>
public sealed class GetDriveInfoTool : IBuiltInTool
{
    private static readonly string[] Keywords =
    {
        "مساحه", "القرص", "الهارد", "هارد", "تخزين", "disk", "space", "storage"
    };

    private readonly Func<IReadOnlyList<DriveSpace>> _drives;

    /// <param name="drives">
    /// مزوّد قائمة الأقراص. الافتراضي: الأقراص الثابتة/القابلة للإزالة في النظام.
    /// </param>
    public GetDriveInfoTool(Func<IReadOnlyList<DriveSpace>>? drives = null)
    {
        _drives = drives ?? DefaultDrives;
    }

    public string Name => "GetDriveInfo";

    public string Description => "مساحة الأقراص الحرة والمستخدمة وتنبيه عند انخفاض المساحة.";

    public int Order => 20;

    public Task<string?> TryAnswerAsync(BuiltInToolContext context, CancellationToken ct = default)
    {
        if (!HasAny(context.Question, Keywords))
            return Task.FromResult<string?>(null);

        IReadOnlyList<DriveSpace> drives;
        try
        {
            drives = _drives();
        }
        catch (Exception ex)
        {
            return Task.FromResult<string?>($"تعذر قراءة معلومات الأقراص: {ex.Message}");
        }

        if (drives.Count == 0)
            return Task.FromResult<string?>("لم أجد أقراصاً قابلة للقراءة على هذا الجهاز.");

        var sb = new StringBuilder("💾 حالة الأقراص:\n");
        var warnings = new List<string>();

        foreach (var d in drives)
        {
            if (!d.IsReady)
            {
                sb.AppendLine($"• {d.Name} — غير جاهز");
                continue;
            }

            var totalGb = d.TotalBytes / 1024d / 1024d / 1024d;
            var freeGb = d.FreeBytes / 1024d / 1024d / 1024d;
            var usedPct = d.TotalBytes > 0
                ? (int)Math.Round((1 - (double)d.FreeBytes / d.TotalBytes) * 100)
                : 0;

            sb.AppendLine($"• {d.Name} ({d.Format}): حر {freeGb:0.##} من {totalGb:0.##} GB — مستخدم {usedPct}%");

            // تنبيه عند انخفاض المساحة الحرة (أقل من 10% أو أقل من 1 GB).
            if (usedPct >= 90 || freeGb < 1)
                warnings.Add($"{d.Name} شبه ممتلئ — حر {freeGb:0.##} GB فقط");
        }

        if (warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("⚠️ تنبيهات:");
            foreach (var w in warnings) sb.AppendLine($"• {w}");
            sb.AppendLine("ننصح بحذف نسخ احتياطية قديمة من الإعدادات لتفريغ مساحة.");
        }

        return Task.FromResult<string?>(sb.ToString().TrimEnd());
    }

    private static IReadOnlyList<DriveSpace> DefaultDrives()
        => DriveInfo.GetDrives()
            .Where(d => d.DriveType is DriveType.Fixed or DriveType.Removable)
            .Select(d =>
            {
                try
                {
                    if (d.IsReady)
                        return new DriveSpace(d.Name, d.DriveFormat, d.TotalSize, d.AvailableFreeSpace, true);
                }
                catch
                {
                    // تجاهل — نُبلغ أنه غير جاهز
                }
                return new DriveSpace(d.Name, "?", 0, 0, false);
            })
            .ToList();

    private static bool HasAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k, StringComparison.Ordinal));
}

/// <summary>معلومات قرص واحدة (نسخة مستقلة لسهولة الاختبار).</summary>
public sealed record DriveSpace(string Name, string Format, long TotalBytes, long FreeBytes, bool IsReady);
