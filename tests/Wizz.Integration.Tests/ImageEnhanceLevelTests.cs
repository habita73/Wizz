using Wizz.Application.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية لشِدّات التحسين: ثلاث قيم ثابتة، والقوي يساوي الإعداد السابق.
/// لا صور ولا نماذج ولا اتصال في هذه الاختبارات.
/// </summary>
public sealed class ImageEnhanceLevelTests
{
    [Fact]
    public void ThreeLevels_HaveFixedClipLimits()
    {
        Assert.Equal(1.0, ImageEnhanceLevels.ClipLimit(ImageEnhanceLevel.Light));
        Assert.Equal(1.5, ImageEnhanceLevels.ClipLimit(ImageEnhanceLevel.Medium));
        Assert.Equal(2.0, ImageEnhanceLevels.ClipLimit(ImageEnhanceLevel.Strong));
    }

    [Fact]
    public void UnknownLevel_UsesPreviousStrongLimit()
    {
        Assert.Equal(ImageEnhanceLevels.StrongClipLimit,
            ImageEnhanceLevels.ClipLimit((ImageEnhanceLevel)99));
    }

    [Fact]
    public void LevelsHaveArabicLabels()
    {
        Assert.Equal("خفيف", ImageEnhanceLevels.Label(ImageEnhanceLevel.Light));
        Assert.Equal("متوسط", ImageEnhanceLevels.Label(ImageEnhanceLevel.Medium));
        Assert.Equal("قوي", ImageEnhanceLevels.Label(ImageEnhanceLevel.Strong));
    }
}
