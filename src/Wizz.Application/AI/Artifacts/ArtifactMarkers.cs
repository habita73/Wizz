namespace Wizz.Application.AI.Artifacts;

using System.Text;
using System.Text.Json;

/// <summary>
/// يُنتج نصوص وسوم <c>wizz-artifact</c> التي يفهمها <see cref="ArtifactParser"/>.
/// يُستخدم من المساعد المدمج (أو أي مولّد إجابات) لتغليف مخرجات قابلة للتصدير
/// (جداول، نصوص، روابط، صور، ملفات) داخل نص الرد.
/// </summary>
public static class ArtifactMarkers
{
    private const string Fence = "```wizz-artifact";

    /// <summary>وسم جدول مع بيانات صريحة (columns/rows) في البيانات الوصفية (أدق للعربية).</summary>
    public static string Table(string title, IReadOnlyList<string> columns, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var meta = new Dictionary<string, object?>
        {
            ["kind"] = "table",
            ["title"] = title,
            ["format"] = "csv",
            ["columns"] = columns,
            ["rows"] = rows
        };

        return FenceAndMeta(meta, payload: null);
    }

    /// <summary>وسم نص/تقرير منسّق.</summary>
    public static string Text(string title, string content, bool html = false)
    {
        var meta = new Dictionary<string, object?>
        {
            ["kind"] = "text",
            ["title"] = title
        };
        if (html) meta["format"] = "html";

        return FenceAndMeta(meta, content);
    }

    /// <summary>وسم رابط.</summary>
    public static string Link(string title, string url, string? description = null)
    {
        var meta = new Dictionary<string, object?>
        {
            ["kind"] = "link",
            ["title"] = title,
            ["url"] = url
        };
        if (!string.IsNullOrWhiteSpace(description)) meta["description"] = description;

        return FenceAndMeta(meta, payload: null);
    }

    /// <summary>وسم ملف (PDF/مستند) معطى كـ base64.</summary>
    public static string File(string title, string base64, string mimeType, string? fileName = null)
    {
        var meta = new Dictionary<string, object?>
        {
            ["kind"] = "file",
            ["title"] = title,
            ["mimeType"] = mimeType
        };
        if (!string.IsNullOrWhiteSpace(fileName)) meta["fileName"] = fileName;

        return FenceAndMeta(meta, base64);
    }

    /// <summary>وسم صورة معطاة كـ base64 (أو رابط عبر <see cref="Link"/> للصور الخارجية).</summary>
    public static string Image(string title, string base64, string mimeType = "image/png")
    {
        var meta = new Dictionary<string, object?>
        {
            ["kind"] = "image",
            ["title"] = title,
            ["mimeType"] = mimeType
        };

        return FenceAndMeta(meta, base64);
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static string FenceAndMeta(Dictionary<string, object?> meta, string? payload)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Fence);
        sb.AppendLine(JsonSerializer.Serialize(meta, JsonOpts));
        if (!string.IsNullOrEmpty(payload))
        {
            sb.AppendLine("---");
            sb.AppendLine(payload);
        }
        sb.AppendLine("```");
        return sb.ToString();
    }
}
