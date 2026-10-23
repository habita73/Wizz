namespace Wizz.Application.AI.Artifacts;

using System.Text;
using System.Text.Json;

/// <summary>صيغ التصدير المدعومة للمخرجات.</summary>
public enum ArtifactExportFormat
{
    Html,
    Markdown,
    Json,
    Csv,
    Txt
}

/// <summary>
/// يحوّل مخرجاً (artifact) إلى الصيغ المختلفة: HTML/Markdown/JSON/CSV/TXT،
/// وينتج مستند HTML كامل للتنسيق/الطباعة.
/// </summary>
public static class ArtifactExporter
{
    /// <summary>يُنتج محتوى نصياً للصيغة المطلوبة.</summary>
    public static string Export(ChatArtifact a, ArtifactExportFormat format) => format switch
    {
        ArtifactExportFormat.Html => ToHtmlFragment(a),
        ArtifactExportFormat.Markdown => ToMarkdown(a),
        ArtifactExportFormat.Json => ToJson(a),
        ArtifactExportFormat.Csv => ToCsv(a),
        ArtifactExportFormat.Txt => ToTxt(a),
        _ => ToTxt(a)
    };

    /// <summary>النوع (MIME) المقابل للصيغة.</summary>
    public static string MimeFor(ArtifactExportFormat format) => format switch
    {
        ArtifactExportFormat.Html => "text/html",
        ArtifactExportFormat.Markdown => "text/markdown",
        ArtifactExportFormat.Json => "application/json",
        ArtifactExportFormat.Csv => "text/csv",
        ArtifactExportFormat.Txt => "text/plain",
        _ => "text/plain"
    };

    /// <summary>الامتداد المقابل للصيغة.</summary>
    public static string ExtensionFor(ArtifactExportFormat format) => format switch
    {
        ArtifactExportFormat.Html => "html",
        ArtifactExportFormat.Markdown => "md",
        ArtifactExportFormat.Json => "json",
        ArtifactExportFormat.Csv => "csv",
        ArtifactExportFormat.Txt => "txt",
        _ => "txt"
    };

    /// <summary>
    /// يُنتج مستند HTML كامل جاهز للطباعة على ورق A4 باتجاه RTL:
    /// ترويسة باسم المتجر وتاريخ الإنشاء، وجدول يتكرر رأسه في كل صفحة بلا قطع للصفوف، وتذييل ثابت.
    /// </summary>
    /// <param name="a">المخرج المطلوب طباعته.</param>
    /// <param name="storeName">اسم المتجر الذي يظهر في الترويسة.</param>
    /// <param name="generatedAt">وقت الإنشاء (لاختبارات قابلة للتكرار)؛ الافتراضي الآن.</param>
    public static string ToPrintableDocument(ChatArtifact a, string storeName = "Wizz", DateTime? generatedAt = null)
    {
        var body = a.Kind switch
        {
            ArtifactKind.Table => TableHtml(a),
            ArtifactKind.Image => ImageHtml(a),
            ArtifactKind.Link => $"<a href=\"{Html(a.Url ?? a.Content ?? "#")}\">{Html(a.Title)}</a>",
            _ => IsHtmlPayload(a) ? (a.Content ?? string.Empty) : $"<pre class=\"plain\">{Html(a.Content ?? string.Empty)}</pre>"
        };

        var generated = (generatedAt ?? DateTime.Now).ToString("yyyy-MM-dd HH:mm");
        var summary = a.Kind == ArtifactKind.Table && a.Rows.Count > 0
            ? $"{a.Rows.Count} صف × {a.Columns.Count} عمود"
            : string.Empty;
        var store = Html(string.IsNullOrWhiteSpace(storeName) ? "Wizz" : storeName.Trim());

        return $$"""
        <!DOCTYPE html>
        <html lang="ar" dir="rtl">
        <head>
        <meta charset="utf-8" />
        <title>{{Html(a.Title)}} — {{store}}</title>
        <style>
          @page { size: A4; margin: 14mm 12mm 18mm; }
          * { box-sizing: border-box; }
          html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }
          body {
            font-family: 'Segoe UI', 'Tahoma', Arial, sans-serif;
            color: #111; margin: 0; padding: 16px; font-size: 13px; line-height: 1.6;
          }
          .doc-header { border-bottom: 2px solid #0d6efd; padding-bottom: 8px; margin-bottom: 14px; }
          .doc-header .store { color: #0d6efd; font-size: 12px; font-weight: 600; }
          h1 { font-size: 20px; margin: 2px 0 4px; }
          .meta { color: #666; font-size: 11.5px; }
          .meta .dot { margin: 0 6px; }
          table { width: 100%; border-collapse: collapse; margin-top: 8px; }
          thead { display: table-header-group; }
          tfoot { display: table-row-group; }
          tr { page-break-inside: avoid; break-inside: avoid; }
          th, td { border: 1px solid #ccc; padding: 6px 8px; text-align: start; font-size: 12.5px; vertical-align: top; }
          th { background: #eef2f7; font-weight: 600; }
          tbody tr:nth-child(even) td { background: #fafafa; }
          pre.plain { white-space: pre-wrap; word-break: break-word; font-family: inherit; font-size: 13px; }
          img { max-width: 100%; }
          .doc-footer {
            position: fixed; bottom: 0; right: 0; left: 0;
            color: #888; font-size: 10.5px; border-top: 1px solid #ddd; padding-top: 4px;
            background: #fff;
          }
          @media print {
            body { padding: 0; }
            .no-print { display: none !important; }
          }
        </style>
        </head>
        <body>
          <header class="doc-header">
            <div class="store">{{store}}</div>
            <h1>{{Html(a.Title)}}</h1>
            <div class="meta">
              تاريخ الطبع: {{generated}}{{ (summary.Length > 0 ? $"<span class=\"dot\">•</span>{summary}" : string.Empty) }}
            </div>
          </header>
          {{body}}
          <footer class="doc-footer">{{store}} — Wizz · {{generated}}</footer>
        </body>
        </html>
        """;
    }

    private static string ToHtmlFragment(ChatArtifact a) => a.Kind switch
    {
        ArtifactKind.Table => TableHtml(a),
        ArtifactKind.Image => ImageHtml(a),
        ArtifactKind.Link => $"<a href=\"{Html(a.Url ?? a.Content ?? "#")}\">{Html(a.Title)}</a>",
        _ => IsHtmlPayload(a) ? (a.Content ?? string.Empty) : $"<pre>{Html(a.Content ?? "")}</pre>"
    };

    private static string ToMarkdown(ChatArtifact a)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {a.Title}");
        if (!string.IsNullOrWhiteSpace(a.Description)) sb.AppendLine().AppendLine(a.Description);
        sb.AppendLine();

        switch (a.Kind)
        {
            case ArtifactKind.Table when a.Columns.Count > 0:
                sb.AppendLine("| " + string.Join(" | ", a.Columns) + " |");
                sb.AppendLine("| " + string.Join(" | ", a.Columns.Select(_ => "---")) + " |");
                foreach (var row in a.Rows)
                    sb.AppendLine("| " + string.Join(" | ", row) + " |");
                break;
            case ArtifactKind.Image:
                sb.AppendLine($"![{a.Title}]({(a.Url ?? "data:" + a.MimeType + ";base64," + a.Data)})");
                break;
            case ArtifactKind.Link:
                sb.AppendLine($"[{a.Title}]({a.Url ?? a.Content})");
                break;
            default:
                sb.AppendLine(a.Content ?? string.Empty);
                break;
        }
        return sb.ToString().TrimEnd();
    }

    private static string ToJson(ChatArtifact a) => JsonSerializer.Serialize(new
    {
        a.Kind,
        a.Title,
        a.Description,
        a.MimeType,
        a.FileName,
        a.Url,
        a.Columns,
        a.Rows,
        Content = a.Kind == ArtifactKind.Image || a.Kind == ArtifactKind.File ? null : a.Content
    }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    private static string ToCsv(ChatArtifact a)
    {
        if (a.Kind != ArtifactKind.Table || a.Columns.Count == 0)
            return a.Content ?? string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", a.Columns.Select(CsvCell)));
        foreach (var row in a.Rows)
            sb.AppendLine(string.Join(",", row.Select(CsvCell)));
        return sb.ToString().TrimEnd();
    }

    private static string ToTxt(ChatArtifact a) => a.Kind switch
    {
        ArtifactKind.Table => CsvToReadable(a),
        ArtifactKind.Link => a.Url ?? a.Content ?? string.Empty,
        ArtifactKind.Image or ArtifactKind.File => a.Description ?? a.Title,
        _ => a.Content ?? string.Empty
    };

    private static string CsvToReadable(ChatArtifact a)
    {
        var sb = new StringBuilder();
        sb.AppendLine(a.Title);
        if (a.Columns.Count > 0) sb.AppendLine(string.Join("\t", a.Columns));
        foreach (var row in a.Rows) sb.AppendLine(string.Join("\t", row));
        return sb.ToString().TrimEnd();
    }

    private static string TableHtml(ChatArtifact a)
    {
        var sb = new StringBuilder();
        sb.Append("<table><thead><tr>");
        foreach (var c in a.Columns) sb.Append($"<th>{Html(c)}</th>");
        sb.Append("</tr></thead><tbody>");
        foreach (var row in a.Rows)
        {
            sb.Append("<tr>");
            foreach (var cell in row) sb.Append($"<td>{Html(cell)}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</tbody></table>");
        return sb.ToString();
    }

    private static string ImageHtml(ChatArtifact a)
    {
        var src = a.Url ?? (a.Data is not null ? $"data:{a.MimeType};base64,{a.Data}" : "");
        return $"<img src=\"{Html(src)}\" alt=\"{Html(a.Title)}\" />";
    }

    private static bool IsHtmlPayload(ChatArtifact a) => a.IsHtml;

    private static string CsvCell(string v)
    {
        if (v.Contains('"') || v.Contains(',') || v.Contains('\n'))
            return "\"" + v.Replace("\"", "\"\"") + "\"";
        return v;
    }

    private static string Html(string? s) =>
        (s ?? string.Empty)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
            .Replace("\"", "&quot;");
}
