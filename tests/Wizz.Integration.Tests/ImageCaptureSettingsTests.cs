using Wizz.Application.Abstractions;
using Wizz.Infrastructure.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية لتخصيصات التقاط الصورة: القيم الافتراضية، الحفظ المحلي،
/// والقراءة الآمنة لملف تالف. لا قاعدة بيانات ولا إرسال في هذه الاختبارات.
/// </summary>
public sealed class ImageCaptureSettingsTests
{
    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var path = NewPath();
        try
        {
            var store = new ImageCaptureSettingsStore(path);

            Assert.Equal(ImageCaptureSettings.Default, store.Get());
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void Save_ThenReload_KeepsAllChoices()
    {
        var path = NewPath();
        try
        {
            var expected = new ImageCaptureSettings(
                EnhanceEnabled: true,
                PreviewBeforeAnalysis: false,
                ShowEnhancedPreview: true);
            new ImageCaptureSettingsStore(path).Save(expected);

            var actual = new ImageCaptureSettingsStore(path).Get();

            Assert.Equal(expected, actual);
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void CorruptFile_ReturnsDefaults()
    {
        var path = NewPath();
        try
        {
            File.WriteAllText(path, "{ هذا ليس JSON صالحًا ");

            Assert.Equal(ImageCaptureSettings.Default, new ImageCaptureSettingsStore(path).Get());
        }
        finally
        {
            Delete(path);
        }
    }

    [Fact]
    public void PartialFile_UsesDefaultsForMissingChoices()
    {
        var path = NewPath();
        try
        {
            File.WriteAllText(path, "{\"EnhanceEnabled\": true}");

            var actual = new ImageCaptureSettingsStore(path).Get();

            Assert.True(actual.EnhanceEnabled);
            Assert.Equal(ImageCaptureSettings.Default.PreviewBeforeAnalysis, actual.PreviewBeforeAnalysis);
            Assert.Equal(ImageCaptureSettings.Default.ShowEnhancedPreview, actual.ShowEnhancedPreview);
        }
        finally
        {
            Delete(path);
        }
    }

    private static string NewPath() =>
        Path.Combine(Path.GetTempPath(), $"wizz-image-capture-{Guid.NewGuid():N}.json");

    private static void Delete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // تنظيف أفضل جهد؛ لا يُفشل الاختبار.
        }
    }
}
