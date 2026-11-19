using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية لإعادة بناء الأسطر: الحدّ نسبي لارتفاع الكلمات،
/// ومستوى السطر يُحدَّث أثناء الجمع، والترتيب داخل السطر من اليمين لليسار.
/// </summary>
public sealed class OcrRowReconstructorTests
{
    [Fact]
    public void Reconstruct_GroupsNearbyWordsInRightToLeftOrder()
    {
        var words = new[]
        {
            new OcrWordBox("حليب", 300, 10, 20),
            new OcrWordBox("12.5", 100, 18, 20),
            new OcrWordBox("معقم", 200, 14, 20)
        };

        Assert.Equal("حليب معقم 12.5", OcrRowReconstructor.Reconstruct(words));
    }

    [Fact]
    public void Reconstruct_UpdatesRowBaseline_SoSlantedLineStaysOneRow()
    {
        // مع حدّ ثابت قديم كان الرقم الثالث (فرق ٢٤ عن أول كلمة) يبدأ سطرًا جديدًا.
        var words = new[]
        {
            new OcrWordBox("تفاح", 300, 10, 40),
            new OcrWordBox("كغ", 200, 22, 40),
            new OcrWordBox("450.00", 100, 34, 40)
        };

        Assert.Equal("تفاح كغ 450.00", OcrRowReconstructor.Reconstruct(words));
    }

    [Fact]
    public void Reconstruct_UsesFontHeight_InsteadOfFixedPixels()
    {
        // خط كبير: فرق ٤٠ بكسل ما زال ضمن السطر لأن الحدّ ٠٫٦ من الارتفاع = ٦٠.
        var words = new[]
        {
            new OcrWordBox("مطحونة", 300, 10, 100),
            new OcrWordBox("قهوة", 100, 50, 100)
        };

        Assert.Equal("مطحونة قهوة", OcrRowReconstructor.Reconstruct(words));
    }

    [Fact]
    public void Reconstruct_KeepsClearlySeparateLinesApart()
    {
        var words = new[]
        {
            new OcrWordBox("الرأس", 200, 10, 20),
            new OcrWordBox("المحتوى", 200, 80, 20)
        };

        Assert.Equal("الرأس\nالمحتوى", OcrRowReconstructor.Reconstruct(words));
    }

    [Fact]
    public void Reconstruct_IgnoresBlankWords_AndReturnsEmptyForNoWords()
    {
        Assert.Equal(string.Empty, OcrRowReconstructor.Reconstruct(Array.Empty<OcrWordBox>()));
        Assert.Equal("حليب", OcrRowReconstructor.Reconstruct(new[]
        {
            new OcrWordBox("  ", 300, 10, 20),
            new OcrWordBox("حليب", 200, 10, 20)
        }));
    }
}
