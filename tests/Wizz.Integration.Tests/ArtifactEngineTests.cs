using Wizz.Application.AI.Artifacts;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات محرك المخرجات (artifacts): تحليل الوسوم من نص الرد،
/// والتصدير إلى الصيغ المختلفة (HTML/MD/JSON/CSV/TXT)، والتنسيق للطباعة.
/// </summary>
public sealed class ArtifactEngineTests
{
    [Fact]
    public void Parse_NoMarker_ReturnsOriginalTextAndNoArtifacts()
    {
        var (text, artifacts) = ArtifactParser.Parse("مبيعات اليوم تبلغ 0.00 د.ج.");

        Assert.Equal("مبيعات اليوم تبلغ 0.00 د.ج.", text);
        Assert.Empty(artifacts);
    }

    [Fact]
    public void Parse_NullText_ReturnsEmpty()
    {
        var (text, artifacts) = ArtifactParser.Parse(null);

        Assert.Equal(string.Empty, text);
        Assert.Empty(artifacts);
    }

    [Fact]
    public void Parse_TableArtifact_ExtractsColumnsRowsAndRemovesMarker()
    {
        var answer = """
            إليك تقرير المبيعات:
            ```wizz-artifact
            { "kind": "table", "title": "تقرير المبيعات", "format": "csv" }
            ---
            | المنتج | الكمية | الإجمالي
            | --- | --- | ---
            | سكر | 10 | 500
            | زيت | 5 | 750
            ```
            """;

        var (text, artifacts) = ArtifactParser.Parse(answer);

        Assert.Contains("تقرير المبيعات", text);
        Assert.DoesNotContain("wizz-artifact", text);
        var a = Assert.Single(artifacts);
        Assert.Equal(ArtifactKind.Table, a.Kind);
        Assert.Equal("تقرير المبيعات", a.Title);
        Assert.Equal(new[] { "المنتج", "الكمية", "الإجمالي" }, a.Columns);
        Assert.Equal(2, a.Rows.Count);
        Assert.Equal("سكر", a.Rows[0][0]);
    }

    [Fact]
    public void Parse_TableFromMetadata_PopulatesRows()
    {
        var answer = """
            ```wizz-artifact
            { "kind": "table", "title": "قائمة", "columns": ["أ", "ب"], "rows": [["1", "2"], ["3", "4"]] }
            ```
            """;

        var (_, artifacts) = ArtifactParser.Parse(answer);

        var a = Assert.Single(artifacts);
        Assert.Equal(ArtifactKind.Table, a.Kind);
        Assert.Equal(2, a.Columns.Count);
        Assert.Equal(2, a.Rows.Count);
        Assert.Equal("4", a.Rows[1][1]);
    }

    [Fact]
    public void Parse_PdfFileArtifact_DetectsFileKindAndBase64()
    {
        var answer = """
            ```wizz-artifact
            { "kind": "file", "title": "فاتورة #B43701", "mimeType": "application/pdf", "fileName": "invoice-B43701" }
            ---
            JVBERi0xLjQK
            ```
            """;

        var (text, artifacts) = ArtifactParser.Parse(answer);

        Assert.Equal(string.Empty, text);
        var a = Assert.Single(artifacts);
        Assert.Equal(ArtifactKind.File, a.Kind);
        Assert.Equal("application/pdf", a.MimeType);
        Assert.Equal("JVBERi0xLjQK", a.Data);
    }

    [Fact]
    public void Parse_LinkArtifact_DetectsLinkKind()
    {
        var answer = """
            ```wizz-artifact
            { "kind": "link", "title": "لوحة التحكم", "url": "https://example.com/dash" }
            ```
            """;

        var (_, artifacts) = ArtifactParser.Parse(answer);

        var a = Assert.Single(artifacts);
        Assert.Equal(ArtifactKind.Link, a.Kind);
        Assert.Equal("https://example.com/dash", a.Url);
    }

    [Fact]
    public void Parse_ImageBase64_DetectedFromPayload()
    {
        var answer = """
            ```wizz-artifact
            { "title": "رسم بياني" }
            ---
            iVBORw0KGgoAAAANSUhEUgAAAAEAAAAB
            ```
            """;

        var (_, artifacts) = ArtifactParser.Parse(answer);

        var a = Assert.Single(artifacts);
        Assert.Equal(ArtifactKind.Image, a.Kind);
        Assert.Equal("image/png", a.MimeType);
        Assert.Null(a.Url);
        Assert.NotNull(a.Data);
    }

    [Fact]
    public void Parse_InvalidMetadata_KeepsMarkerVisible()
    {
        var answer = """
            ```wizz-artifact
            { this is not json }
            ```
            """;

        var (text, artifacts) = ArtifactParser.Parse(answer);

        Assert.Empty(artifacts);
        Assert.Contains("wizz-artifact", text);
    }

    [Fact]
    public void Export_TableToCsv_EscapesAndJoins()
    {
        var a = new ChatArtifact
        {
            Kind = ArtifactKind.Table,
            Title = "جدول",
            Columns = { "المنتج", "الإجمالي" },
            Rows =
            {
                new List<string> { "سكر", "500" },
                new List<string> { "زيت, نقي", "750" }
            }
        };

        var csv = ArtifactExporter.Export(a, ArtifactExportFormat.Csv);
        var lines = csv.Split('\n', StringSplitOptions.TrimEntries);

        Assert.Equal("المنتج,الإجمالي", lines[0]);
        Assert.Contains("\"زيت, نقي\"", lines[2]);
    }

    [Fact]
    public void Export_TableToMarkdown_ProducesPipeTable()
    {
        var a = new ChatArtifact
        {
            Kind = ArtifactKind.Table,
            Title = "جدول",
            Columns = { "أ", "ب" },
            Rows = { new List<string> { "1", "2" } }
        };

        var md = ArtifactExporter.Export(a, ArtifactExportFormat.Markdown);

        Assert.Contains("# جدول", md);
        Assert.Contains("| أ | ب |", md);
        Assert.Contains("| 1 | 2 |", md);
    }

    [Fact]
    public void Export_Json_IsValidAndContainsTitle()
    {
        var a = new ChatArtifact { Kind = ArtifactKind.Text, Title = "تقرير", Content = "مرحباً" };

        var json = ArtifactExporter.Export(a, ArtifactExportFormat.Json);

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.Equal("تقرير", doc.RootElement.GetProperty("Title").GetString());
        Assert.Equal("مرحباً", doc.RootElement.GetProperty("Content").GetString());
    }

    [Fact]
    public void ToPrintableDocument_IsRtlHtmlWithTitle()
    {
        var a = new ChatArtifact
        {
            Kind = ArtifactKind.Table,
            Title = "تقرير المبيعات",
            Columns = { "المنتج", "الكمية" },
            Rows = { new List<string> { "سكر", "10" } }
        };

        var html = ArtifactExporter.ToPrintableDocument(a, "متجر الاختبار");

        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("تقرير المبيعات", html);
        Assert.Contains("<table>", html);
        Assert.Contains("متجر الاختبار", html);
        Assert.Contains("@page { size: A4", html);
    }

    [Fact]
    public void ToPrintableDocument_HtmlContent_NotDoubleEncoded()
    {
        var a = new ChatArtifact
        {
            Kind = ArtifactKind.Text,
            Title = "فاتورة",
            MimeType = "text/html",
            Content = "<div><strong>الفاتورة</strong></div>"
        };

        var html = ArtifactExporter.ToPrintableDocument(a);

        Assert.Contains("<div><strong>الفاتورة</strong></div>", html);
    }

    [Fact]
    public void Parse_TextWithoutHtmlFormat_IsPlainTextNotHtml()
    {
        var answer = """
            ```wizz-artifact
            { "kind": "text", "title": "نص" }
            ---
            <b>ليس HTML مصرّحاً</b>
            ```
            """;

        var (_, artifacts) = ArtifactParser.Parse(answer);

        var a = Assert.Single(artifacts);
        Assert.False(a.IsHtml);
        Assert.Equal("text/plain", a.MimeType);
    }

    [Fact]
    public void Parse_TextWithHtmlFormat_IsHtml()
    {
        var answer = """
            ```wizz-artifact
            { "kind": "text", "title": "فاتورة", "format": "html" }
            ---
            <table><tr><td>سكر</td></tr></table>
            ```
            """;

        var (_, artifacts) = ArtifactParser.Parse(answer);

        var a = Assert.Single(artifacts);
        Assert.True(a.IsHtml);
    }

    [Fact]
    public void ToPrintableDocument_PlainText_IsEscaped()
    {
        var a = new ChatArtifact
        {
            Kind = ArtifactKind.Text,
            Title = "نص",
            Content = "<script>alert(1)</script>"
        };

        var html = ArtifactExporter.ToPrintableDocument(a);

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Theory]
    [InlineData(ArtifactExportFormat.Html, "html")]
    [InlineData(ArtifactExportFormat.Markdown, "md")]
    [InlineData(ArtifactExportFormat.Json, "json")]
    [InlineData(ArtifactExportFormat.Csv, "csv")]
    [InlineData(ArtifactExportFormat.Txt, "txt")]
    public void ExtensionFor_ReturnsExpected(ArtifactExportFormat format, string expected)
    {
        Assert.Equal(expected, ArtifactExporter.ExtensionFor(format));
    }
}
