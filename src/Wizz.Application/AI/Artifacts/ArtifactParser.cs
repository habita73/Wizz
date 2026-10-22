namespace Wizz.Application.AI.Artifacts;

using System.Text;
using System.Text.Json;

/// <summary>
/// يستخرج كائنات المخرجات (artifacts) من نص رد المساعد، ويُخفي الوسوم من العرض.
///
/// صيغة الوسم (fenced block):
/// <code>
/// ```wizz-artifact
/// { "kind": "receipt", "title": "فاتورة #B43701", "format": "html" }
/// ---
/// &lt;div&gt;...&lt;/div&gt;
/// ```
/// </code>
/// السطر الأول بعد الوسم كائن JSON للبيانات الوصفية، ثم فاصل <c>---</c>، ثم الحمولة (نص/HTML/قاعدة بيانات).
/// </summary>
public static class ArtifactParser
{
    private const string FenceOpen = "```wizz-artifact";
    private const string Separator = "---";

    /// <summary>يستخرج كل المخرجات ويُعيد النص بعد إزالة الوسوم.</summary>
    public static (string Text, List<ChatArtifact> Artifacts) Parse(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains(FenceOpen, StringComparison.OrdinalIgnoreCase))
            return (text ?? string.Empty, new List<ChatArtifact>());

        var artifacts = new List<ChatArtifact>();
        var clean = new StringBuilder();
        var lines = text.Replace("\r\n", "\n").Split('\n');

        int i = 0;
        while (i < lines.Length)
        {
            if (!lines[i].TrimStart().StartsWith(FenceOpen, StringComparison.OrdinalIgnoreCase))
            {
                clean.AppendLine(lines[i]);
                i++;
                continue;
            }

            // وجدنا بداية وسم — نجمع حتى نهاية السياج ```
            var body = new List<string>();
            i++;
            while (i < lines.Length && lines[i].Trim() != "```")
            {
                body.Add(lines[i]);
                i++;
            }
            if (i < lines.Length) i++; // تجاوز ```

            var artifact = BuildFromBody(body);
            if (artifact is not null)
            {
                artifacts.Add(artifact);
            }
            else
            {
                // فشل التحليل: نُبقي الوسم ظاهراً بدل إخفاء محتوى المستخدم.
                clean.AppendLine(FenceOpen);
                foreach (var b in body) clean.AppendLine(b);
                clean.AppendLine("```");
            }
        }

        return (clean.ToString().TrimEnd(), artifacts);
    }

    private static ChatArtifact? BuildFromBody(List<string> body)
    {
        if (body.Count == 0) return null;

        var metaLine = body[0].Trim();
        ArtifactMeta? meta = null;
        try
        {
            meta = JsonSerializer.Deserialize<ArtifactMeta>(metaLine,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
        if (meta is null) return null;

        // بقية السطور: بعد فاصل اختياري ---
        var payloadLines = body.Skip(1).ToList();
        if (payloadLines.Count > 0 && payloadLines[0].Trim() == Separator)
            payloadLines.RemoveAt(0);
        var payload = string.Join("\n", payloadLines).Trim();

        var kind = ParseKind(meta.Kind, meta.Format, payload);

        var artifact = new ChatArtifact
        {
            Kind = kind,
            Title = string.IsNullOrWhiteSpace(meta.Title) ? DefaultTitle(kind) : meta.Title!.Trim(),
            Description = meta.Description?.Trim(),
            MimeType = meta.MimeType?.Trim(),
            FileName = SanitizeFileName(meta.FileName ?? meta.Title),
            Url = NullIfEmpty(meta.Url)
        };

        if (kind == ArtifactKind.Table)
        {
            PopulateTable(artifact, meta, payload);
        }
        else if (kind is ArtifactKind.Image or ArtifactKind.File)
        {
            PopulateBinary(artifact, meta, payload);
        }
        else if (kind == ArtifactKind.Link)
        {
            var link = !string.IsNullOrWhiteSpace(meta.Url) ? meta.Url : payload;
            artifact.Content = link?.Trim();
            artifact.Url = string.IsNullOrWhiteSpace(artifact.Url) ? artifact.Content : artifact.Url;
        }
        else // Text
        {
            artifact.Content = payload;
            // نعتبره HTML فقط إذا صرّح الوسم بذلك (format = html)، وإلا يُعرض كنص آمن.
            if (string.Equals(meta.Format, "html", StringComparison.OrdinalIgnoreCase))
                artifact.MimeType = "text/html";
            else
                artifact.MimeType ??= "text/plain";
        }

        return artifact;
    }

    private static void PopulateTable(ChatArtifact artifact, ArtifactMeta meta, string payload)
    {
        artifact.MimeType ??= "text/csv";

        if (meta.Columns is { Count: > 0 })
        {
            artifact.Columns = meta.Columns.Select(c => c ?? string.Empty).ToList();
            artifact.Rows = meta.Rows?.Select(r => r.Select(c => c ?? string.Empty).ToList()).ToList()
                           ?? new List<List<string>>();
        }
        else
        {
            // جدول نصي بسيط: أول سطر عناوين، الفاصل فاصلة أو شرطة عمودية.
            var rows = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                              .Where(l => !IsSeparatorRow(l))
                              .Select(SplitRow)
                              .Where(r => r.Count > 0)
                              .ToList();
            if (rows.Count > 0)
            {
                artifact.Columns = rows[0];
                artifact.Rows = rows.Skip(1).ToList();
            }
        }

        artifact.Content = payload;
    }

    /// <summary>هل هذا سطر فاصل Markdown (مثل | --- | :--- |)؟</summary>
    private static bool IsSeparatorRow(string line)
    {
        var t = line.Trim().Trim('|').Replace(":", "").Replace("-", "").Replace("|", "").Trim();
        return t.Length == 0 && line.Contains('-');
    }

    private static void PopulateBinary(ChatArtifact artifact, ArtifactMeta meta, string payload)
    {
        var source = !string.IsNullOrWhiteSpace(meta.Url) ? meta.Url : payload;

        if (string.IsNullOrWhiteSpace(source))
        {
            artifact.Data = null;
            return;
        }

        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("/", StringComparison.Ordinal))
        {
            artifact.Url = source.Trim();
        }
        else
        {
            // نتوقع base64، ونُنقّي أي بادئة data URL.
            var data = source.Trim();
            var comma = data.IndexOf(',');
            if (data.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma > 0)
            {
                artifact.MimeType ??= data[5..comma].Split(';')[0];
                data = data[(comma + 1)..];
            }
            artifact.Data = data;
        }

        artifact.MimeType ??= artifact.Kind == ArtifactKind.Image ? "image/png" : "application/octet-stream";
    }

    private static List<string> SplitRow(string line)
    {
        List<string> cells;
        if (line.Contains('|'))
            cells = line.Split('|', StringSplitOptions.TrimEntries).ToList();
        else if (line.Contains(','))
            cells = line.Split(',', StringSplitOptions.TrimEntries).ToList();
        else
            cells = line.Split('\t', StringSplitOptions.TrimEntries).ToList();

        // إزالة الخلايا الفارغة الطرفية الناتجة عن أشرطة Markdown (| أ | ب |).
        while (cells.Count > 0 && cells[0].Length == 0) cells.RemoveAt(0);
        while (cells.Count > 0 && cells[^1].Length == 0) cells.RemoveAt(cells.Count - 1);
        return cells;
    }

    private static ArtifactKind ParseKind(string? kind, string? format, string payload)
    {
        var k = (kind ?? string.Empty).Trim().ToLowerInvariant();
        var f = (format ?? string.Empty).Trim().ToLowerInvariant();

        return k switch
        {
            "table" or "csv" or "grid" => ArtifactKind.Table,
            "image" or "img" or "picture" or "chart" => ArtifactKind.Image,
            "file" or "pdf" or "document" or "doc" or "download" => ArtifactKind.File,
            "link" or "url" => ArtifactKind.Link,
            _ when f is "csv" or "table" => ArtifactKind.Table,
            _ when f is "png" or "jpg" or "jpeg" or "image" => ArtifactKind.Image,
            _ when f is "pdf" => ArtifactKind.File,
            _ when f is "url" or "link" => ArtifactKind.Link,
            // استنتاج من الحمولة عند غياب النوع
            _ when payload.StartsWith("http://") || payload.StartsWith("https://") => ArtifactKind.Link,
            _ when payload.StartsWith("iVBORw0KGgo") => ArtifactKind.Image, // PNG base64
            _ => ArtifactKind.Text
        };
    }

    private static string DefaultTitle(ArtifactKind kind) => kind switch
    {
        ArtifactKind.Table => "جدول",
        ArtifactKind.Image => "صورة",
        ArtifactKind.File => "ملف",
        ArtifactKind.Link => "رابط",
        _ => "مخرج"
    };

    private static string? SanitizeFileName(string? name)
    {
        var n = NullIfEmpty(name);
        if (n is null) return null;
        foreach (var c in Path.GetInvalidFileNameChars())
            n = n.Replace(c, '_');
        return n.Trim().Replace(' ', '_');
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private sealed class ArtifactMeta
    {
        public string? Kind { get; set; }
        public string? Format { get; set; }
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? MimeType { get; set; }
        public string? FileName { get; set; }
        public string? Url { get; set; }
        public List<string?>? Columns { get; set; }
        public List<List<string?>>? Rows { get; set; }
    }
}
