using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية لتحسين الإدخال ودمج القراءتين: تكبير الصور الصغيرة فقط،
/// واستبدال الرموز الرقمية المشوّشة برقم نظيف من القراءة المساعدة بلا لمس أسماء المنتجات.
/// </summary>
public sealed class OcrInputAndMergeTests
{
    [Fact]
    public void ComputeScale_LeavesAdequateImageUnchanged()
    {
        Assert.Equal(1.0, OcrImagePreprocessor.ComputeScale(1600, 1200));
        Assert.Equal(1.0, OcrImagePreprocessor.ComputeScale(960, 1200));
    }

    [Fact]
    public void ComputeScale_UpscalesSmallImageWithinBounds()
    {
        var scale = OcrImagePreprocessor.ComputeScale(480, 640);

        Assert.True(scale > 1.0);
        Assert.Equal(2.0, scale, 3); // 960 / 480
        Assert.True(640 * scale <= OcrImagePreprocessor.MaximumLongSidePixels);
    }

    [Fact]
    public void ComputeScale_CapsUpscaleAndLongSide()
    {
        Assert.Equal(OcrImagePreprocessor.MaximumUpscaleFactor,
            OcrImagePreprocessor.ComputeScale(100, 200));

        var tallScale = OcrImagePreprocessor.ComputeScale(100, 4000);
        Assert.True(4000 * tallScale <= OcrImagePreprocessor.MaximumLongSidePixels);
    }

    [Fact]
    public void ComputeScale_ReturnsOneForInvalidDimensions()
    {
        Assert.Equal(1.0, OcrImagePreprocessor.ComputeScale(0, 100));
        Assert.Equal(1.0, OcrImagePreprocessor.ComputeScale(100, -5));
    }

    [Fact]
    public void ComputeTargetSize_UsesOrientedDimensionsWithoutUpscalingAdequateImage()
    {
        var target = OcrImagePreprocessor.ComputeTargetSize(1200, 1600);

        Assert.Equal(1200, target.Width);
        Assert.Equal(1600, target.Height);
        Assert.Equal(1.0, target.Scale);
    }

    [Fact]
    public void ComputeTargetSize_ScalesSmallOrientedImage()
    {
        var target = OcrImagePreprocessor.ComputeTargetSize(480, 640);

        Assert.Equal(2.0, target.Scale);
        Assert.Equal(960, target.Width);
        Assert.Equal(1280, target.Height);
    }

    [Fact]
    public void ComputeTargetSize_CapsScaleAndLongSide()
    {
        // عامل 960/240 = 4 يُقيَّد بـ٢٫٥، والضلع الطويل ٤٠٠٠ لا يتجاوز السقف.
        var capped = OcrImagePreprocessor.ComputeTargetSize(240, 320);
        Assert.Equal(OcrImagePreprocessor.MaximumUpscaleFactor, capped.Scale);
        Assert.Equal(600, capped.Width);
        Assert.Equal(800, capped.Height);

        var tall = OcrImagePreprocessor.ComputeTargetSize(100, 4000);
        Assert.True(tall.Height <= OcrImagePreprocessor.MaximumLongSidePixels);
    }

    [Fact]
    public void ComputeTargetSize_ReturnsSafeValuesForInvalidDimensions()
    {
        var target = OcrImagePreprocessor.ComputeTargetSize(0, 100);

        Assert.Equal(0, target.Width);
        Assert.Equal(100, target.Height);
        Assert.Equal(1.0, target.Scale);
    }

    [Fact]
    public void Merge_ReplacesGarbledNumberWithCleanFrenchNumber()
    {
        var arabic = "حليب i.ooo";
        var french = "حليب 1.000";

        Assert.Equal("حليب 1.000", OcrReadingMerger.Merge(arabic, french));
    }

    [Fact]
    public void Merge_KeepsOriginalWhenSecondaryHasNoCleanNumber()
    {
        var arabic = "حليب oo";
        var french = "حليب oo";

        Assert.Equal("حليب oo", OcrReadingMerger.Merge(arabic, french));
    }

    [Fact]
    public void Merge_DoesNotTouchProductNames()
    {
        var arabic = "حليب معقم ll";
        var french = "حليب معقم 11";

        Assert.Equal("حليب معقم 11", OcrReadingMerger.Merge(arabic, french));
    }

    [Fact]
    public void Merge_UsesCleanNumbersInReadingOrderAndLeavesExtraUntouched()
    {
        var arabic = "oo i.ooo ll";
        var french = "00 1.000 11 999";

        Assert.Equal("00 1.000 11", OcrReadingMerger.Merge(arabic, french));
    }

    [Fact]
    public void Merge_AlignsByLineAndReturnsPrimaryWhenSecondaryMissing()
    {
        var arabic = "الرأس\nحليب oo";
        var french = "ترويسة\nحليب 00";

        Assert.Equal("الرأس\nحليب 00", OcrReadingMerger.Merge(arabic, french));
        Assert.Equal(arabic, OcrReadingMerger.Merge(arabic, null));
    }
}
