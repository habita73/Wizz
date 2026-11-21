using Wizz.Application.AI.Perception;
using Wizz.Infrastructure.AI.Perception;

namespace Wizz.Integration.Tests;

/// <summary>اختبارات ببيانات اصطناعية لتعلّم الأسماء محليًا وقياس الدقة بلا تدريب نموذج.</summary>
public sealed class OcrLearningAndEvaluationTests : IDisposable
{
    private readonly string _dir;

    public OcrLearningAndEvaluationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "wizz-ocr-learning-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* تنظيف أفضل جهد */ }
    }

    [Fact]
    public void Alias_LearnedFromConfirmation_ResolvesNormalizedName()
    {
        var store = NewAliasStore();

        store.Remember("الارز", "أرز");

        Assert.Equal("أرز", store.Resolve("الارز"));
        // توحيد الهمزة يجعل «الأرز» و«الارز» الاسم المقروء نفسه.
        Assert.Equal("أرز", store.Resolve("الأرز"));
        Assert.Equal("أرز", store.Resolve("الأَرْز"));
        Assert.Null(store.Resolve("خبز"));
        Assert.Null(store.Resolve(null));
    }

    [Fact]
    public void Alias_PersistsAcrossInstances_AndCanBeForgotten()
    {
        var path = Path.Combine(_dir, "aliases.json");
        var first = new OcrAliasStore(path);
        first.Remember("الارز", "أرز");
        first.Remember("الارز", "أرز");

        var second = new OcrAliasStore(path);
        Assert.Equal("أرز", second.Resolve("الارز"));
        var saved = Assert.Single(second.GetAll());
        Assert.Equal(2, saved.Confirmations);

        second.Forget("الارز");
        Assert.Null(second.Resolve("الارز"));
        Assert.Empty(new OcrAliasStore(path).GetAll());
    }

    [Fact]
    public void Alias_Clear_RemovesEverything()
    {
        var store = NewAliasStore();
        store.Remember("الارز", "أرز");
        store.Remember("زيت زيتون", "زيت");

        store.Clear();

        Assert.Empty(store.GetAll());
        Assert.Null(store.Resolve("الارز"));
    }

    [Fact]
    public void Evaluation_WithoutAlias_DoesNotGuessTheName()
    {
        var evaluator = new OcrEvaluationService(
            new StubSampleStore(new OcrEvaluationSample("s1", "الارز 12.50", "أرز", 12.50m)),
            NewAliasStore());

        var result = evaluator.Run();

        Assert.Equal(1, result.Total);
        Assert.Equal(0, result.NameHits);
        Assert.NotEmpty(result.Failures);
    }

    [Fact]
    public void Evaluation_WithLearnedAlias_MatchesNameAndPrice()
    {
        var aliases = NewAliasStore();
        aliases.Remember("الارز", "أرز");
        var evaluator = new OcrEvaluationService(
            new StubSampleStore(new OcrEvaluationSample("s1", "الارز 12.50", "أرز", 12.50m)),
            aliases);

        var result = evaluator.Run();

        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.NameHits);
        Assert.Equal(1, result.PriceHits);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void SampleStore_SeedsSyntheticSamplesOnly()
    {
        var path = Path.Combine(_dir, "samples.json");
        var store = new OcrEvaluationSampleStore(path);

        var samples = store.GetAll();

        Assert.Equal(5, samples.Count);
        Assert.All(samples, s => Assert.StartsWith("synthetic-", s.Id));
        Assert.True(File.Exists(path));
    }

    private OcrAliasStore NewAliasStore() => new(Path.Combine(_dir, "aliases-" + Guid.NewGuid().ToString("N") + ".json"));

    private sealed class StubSampleStore(params OcrEvaluationSample[] samples) : IOcrEvaluationSampleStore
    {
        public IReadOnlyList<OcrEvaluationSample> GetAll() => samples;
    }
}
