using Wizz.Application.AI.Artifacts;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات قالب الطباعة A4 (المرحلة 8-2):
/// مستند RTL بحجم A4، ترويسة باسم المتجر وتاريخ قابل للتحديد،
/// رأس جدول يتكرر في كل صفحة، ومنع قطع الصفوف بين الصفحات.
/// </summary>
public sealed class ArtifactPrintTests
{
    private static ChatArtifact Table() => new()
    {
        Kind = ArtifactKind.Table,
        Title = "قائمة المنتجات",
        Columns = new List<string> { "المنتج", "السعر" },
        Rows = new List<List<string>>
        {
            new() { "حليب كامل الدسم", "120" },
            new() { "حليب نصف الدسم", "115" }
        }
    };

    private static readonly DateTime FixedTime = new(2026, 10, 23, 9, 30, 0);

    [Fact]
    public void Document_IsA4RtL_WithPrintableHeaderAndFooter()
    {
        var html = ArtifactExporter.ToPrintableDocument(Table(), "متجر ويز", FixedTime);

        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("lang=\"ar\"", html);
        Assert.Contains("@page { size: A4;", html);
        Assert.Contains("class=\"doc-header\"", html);
        Assert.Contains("متجر ويز", html);
        Assert.Contains("2026-10-23 09:30", html);
        Assert.Contains("class=\"doc-footer\"", html);
    }

    [Fact]
    public void Table_SplitsAcrossPages_WithRepeatedHeader()
    {
        var html = ArtifactExporter.ToPrintableDocument(Table(), "متجر ويز", FixedTime);

        Assert.Contains("thead { display: table-header-group; }", html);
        Assert.Contains("page-break-inside: avoid", html);
        Assert.Contains("<thead><tr><th>المنتج</th><th>السعر</th></tr></thead>", html);
        Assert.Contains("حليب كامل الدسم", html);
        Assert.Contains("حليب نصف الدسم", html);
        Assert.Contains("2 صف × 2 عمود", html);
    }

    [Fact]
    public void Title_IsHtmlEscaped()
    {
        var artifact = Table();
        artifact.Title = "تقرير <b>خطير</b>";

        var html = ArtifactExporter.ToPrintableDocument(artifact, "متجر", FixedTime);

        Assert.DoesNotContain("<b>خطير</b>", html);
        Assert.Contains("&lt;b&gt;خطير&lt;/b&gt;", html);
    }

    [Fact]
    public void TextArtifact_KeepsPlainTextBlocks()
    {
        var artifact = new ChatArtifact
        {
            Kind = ArtifactKind.Text,
            Title = "ملاحظة",
            Content = "سطر أول\nسطر ثانٍ"
        };

        var html = ArtifactExporter.ToPrintableDocument(artifact, "متجر", FixedTime);

        Assert.Contains("<pre class=\"plain\">", html);
        Assert.Contains("سطر أول", html);
        Assert.DoesNotContain("صف ×", html);
    }
}
