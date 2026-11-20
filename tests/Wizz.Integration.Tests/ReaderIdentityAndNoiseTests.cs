using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية: اسم القارئ يصل إلى النتيجة، واختيار القارئ للمقارنة لا يغيّر
/// المسار الافتراضي، وأسطر الرأس والذيل لا تُنتج قراءات محتملة مزعجة.
/// </summary>
public sealed class ReaderIdentityAndNoiseTests
{
    [Fact]
    public void OcrReadResult_CarriesReaderName()
    {
        var ok = OcrReadResult.Success("نص اصطناعي", "ar", OcrReaderNames.Windows);
        var failed = OcrReadResult.Unavailable("فشل اصطناعي", OcrReaderNames.RapidOcr);

        Assert.Equal(OcrReaderNames.Windows, ok.ReaderName);
        Assert.Equal(OcrReaderNames.RapidOcr, failed.ReaderName);
    }

    [Fact]
    public async Task SelectableReader_WithDefaultChoice_UsesCompositePrimary()
    {
        var windows = new FakeReader(OcrReadResult.Success("من Windows", "ar", OcrReaderNames.Windows));
        var rapid = new FakeReader(OcrReadResult.Success("من المضمّن", "ar", OcrReaderNames.RapidOcr));
        var selection = new OcrReaderSelection();
        var reader = new SelectableOcrTextReader(
            selection,
            new CompositeOcrTextReader(windows, rapid),
            windows,
            rapid);

        var result = await reader.ReadAsync(new byte[] { 1 });

        Assert.Equal("من Windows", result.Text);
        Assert.Equal(OcrReaderNames.Windows, result.ReaderName);
        Assert.Equal(1, windows.Calls);
        Assert.Equal(0, rapid.Calls);
    }

    [Fact]
    public async Task SelectableReader_WithManualChoice_UsesRequestedReaderWithoutChangingDefault()
    {
        var windows = new FakeReader(OcrReadResult.Success("من Windows", "ar", OcrReaderNames.Windows));
        var rapid = new FakeReader(OcrReadResult.Success("من المضمّن", "ar", OcrReaderNames.RapidOcr));
        var selection = new OcrReaderSelection();
        var reader = new SelectableOcrTextReader(
            selection,
            new CompositeOcrTextReader(windows, rapid),
            windows,
            rapid);

        selection.Set(OcrReaderChoice.RapidOcr);
        var first = await reader.ReadAsync(new byte[] { 1 });
        selection.Set(OcrReaderChoice.Default);
        var second = await reader.ReadAsync(new byte[] { 2 });

        Assert.Equal("من المضمّن", first.Text);
        Assert.Equal(OcrReaderNames.RapidOcr, first.ReaderName);
        Assert.Equal("من Windows", second.Text);
        Assert.Equal(OcrReaderChoice.Default, selection.Current);
    }

    [Fact]
    public async Task SelectableReader_DoesNotFallBackWhenChosenReaderFails()
    {
        // التبديل عند غياب القدرة فقط، ولا تبديل تلقائي عند فشل القارئ المختار للمقارنة.
        var windows = new FakeReader(OcrReadResult.Unavailable("فشل اصطناعي", OcrReaderNames.Windows));
        var rapid = new FakeReader(OcrReadResult.Success("من المضمّن", "ar", OcrReaderNames.RapidOcr));
        var selection = new OcrReaderSelection();
        var reader = new SelectableOcrTextReader(
            selection,
            new CompositeOcrTextReader(windows, rapid),
            windows,
            rapid);

        selection.Set(OcrReaderChoice.Windows);
        var result = await reader.ReadAsync(new byte[] { 1 });

        Assert.False(result.IsAvailable);
        Assert.Equal(OcrReaderNames.Windows, result.ReaderName);
        Assert.Equal(0, rapid.Calls);
    }

    [Fact]
    public void ExtractGroups_MetadataLines_ProduceNoCandidates()
    {
        // بيانات اصطناعية: الرأس والذيل لا يُعدّان منتجات، فلا يُنشأ منهما عشرات القراءات المحتملة.
        var text = string.Join('\n', new[]
        {
            "الهاتف : 11 22 33 444",
            "رقم الفاتورة : 0001111 التاريخ : 02/03/2027",
            "الوقت : 09:05",
            "المجموع : 12.50 1",
            "حي 05 ماي 2027 - مدينة الاختبار",
            "شكرا لتسوقكم"
        });

        var groups = PerceptionCandidateExtractor.ExtractGroups(text);

        Assert.Empty(groups);
    }

    [Fact]
    public void ExtractGroups_ProductRows_StillProduceCandidates()
    {
        // الصفوف الحقيقية ما زالت تُنتج مرشّحاتها، بما فيها السعر الصحيح الواحد.
        var groups = PerceptionCandidateExtractor.ExtractGroups(
            "حليب معقم 2.000 حبة 120.00 240.00\nخبز 5");

        Assert.NotEmpty(groups);
        Assert.Contains(groups.SelectMany(g => g.Candidates), c => c.ProductName == "حليب معقم" && c.Price == 120m);
        Assert.Contains(groups.SelectMany(g => g.Candidates), c => c.ProductName == "خبز" && c.Price == 5m);
    }

    private sealed class FakeReader : IOcrTextReader
    {
        private readonly OcrReadResult _result;

        public FakeReader(OcrReadResult result) => _result = result;

        public int Calls { get; private set; }
        public bool IsSupported => true;

        public Task<OcrReadResult> ReadAsync(byte[] imageBytes, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }
}
