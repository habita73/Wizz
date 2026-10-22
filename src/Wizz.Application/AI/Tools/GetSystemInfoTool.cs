namespace Wizz.Application.AI.Tools;

using System.Globalization;
using System.Reflection;
using System.Text;

/// <summary>
/// أداة معلومات النظام: تجيب عن «معلومات التطبيق»، «إصدار Wizz»، «مواصفات الجهاز»،
/// «كم رام»، «هل الجهاز ضعيف». تفيد في تشخيص الأداء وقرار المسار المحلي.
/// </summary>
public sealed class GetSystemInfoTool : IBuiltInTool
{
    private static readonly string[] Keywords =
    {
        "معلومات النظام", "معلومات الجهاز", "مواصفات", "مواصفات الجهاز", "رام", "ram",
        "معالج", "نظام التشغيل", "الاصدار", "اصدار", "نسخه التطبيق", "نسخه Wizz",
        "system info", "version", "المواصفات"
    };

    private readonly Func<SystemInfo> _info;

    /// <param name="info">
    /// مزوّد معلومات النظام (قابل للحقن للاختبار).
    /// الافتراضي: جمع المعلومات الفعلية من البيئة الحالية.
    /// </param>
    public GetSystemInfoTool(Func<SystemInfo>? info = null)
    {
        _info = info ?? CaptureSystemInfo;
    }

    public string Name => "GetSystemInfo";

    public string Description => "إصدار التطبيق ومواصفات النظام والذاكرة وعدد المعالجات.";

    public int Order => 40;

    public Task<string?> TryAnswerAsync(BuiltInToolContext context, CancellationToken ct = default)
    {
        if (!HasAny(context.Question, Keywords))
            return Task.FromResult<string?>(null);

        var info = _info();

        var sb = new StringBuilder();
        sb.AppendLine("🖥️ معلومات النظام:");
        sb.AppendLine($"• التطبيق: Wizz — الإصدار {info.AppVersion}");
        sb.AppendLine($"• نظام التشغيل: {info.OsDescription}");
        sb.AppendLine($"• معمارية المعالج: {info.Architecture}");
        sb.AppendLine($"• عدد أنوية المعالج: {info.ProcessorCount}");
        sb.AppendLine($"• الذاكرة المتاحة: {info.TotalRamGb:0.#} غيغابايت");
        sb.AppendLine($"• إصدار .NET: {info.DotNetVersion}");
        sb.AppendLine($"• منطقة العرض: {info.Culture} (RTL: {(info.IsRtl ? "نعم" : "لا")})");

        if (info.TotalRamGb > 0 && info.TotalRamGb < 4)
        {
            sb.AppendLine();
            sb.AppendLine("ℹ️ الجهاز بذاكرة محدودة — يُنصح باستخدام المساعد المدمج أو نموذج محلي خفيف.");
        }

        return Task.FromResult<string?>(sb.ToString().TrimEnd());
    }

    private static SystemInfo CaptureSystemInfo()
    {
        long ram = 0;
        try
        {
            var gc = GC.GetGCMemoryInfo();
            if (gc.TotalAvailableMemoryBytes > 0) ram = gc.TotalAvailableMemoryBytes;
        }
        catch
        {
            // تجاهل — نُبلّغ 0 إذا تعذّر القياس
        }

        var culture = CultureInfo.CurrentCulture;

        return new SystemInfo(
            AppVersion: ResolveAppVersion(),
            OsDescription: System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Architecture: System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ProcessorCount: Environment.ProcessorCount,
            TotalRamGb: ram / 1024d / 1024d / 1024d,
            DotNetVersion: Environment.Version.ToString(),
            Culture: culture.Name,
            IsRtl: culture.TextInfo.IsRightToLeft);
    }

    private static string ResolveAppVersion()
    {
        try
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational)) return informational;
            return asm.GetName().Version?.ToString() ?? "غير معروف";
        }
        catch
        {
            return "غير معروف";
        }
    }

    private static bool HasAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k, StringComparison.Ordinal));
}

/// <summary>معلومات النظام المعروضة من الأداة (نسخة مستقلة لسهولة الاختبار).</summary>
public sealed record SystemInfo(
    string AppVersion,
    string OsDescription,
    string Architecture,
    int ProcessorCount,
    double TotalRamGb,
    string DotNetVersion,
    string Culture,
    bool IsRtl);
