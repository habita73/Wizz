namespace Wizz.Integration.Tests;

using Wizz.Application.Abstractions;
using Wizz.Application.AI.Perception;

/// <summary>
/// موفر محلي وهمي للاختبار فقط: لا يفتح شبكة ولا يقرأ نموذجًا حقيقيًا.
/// </summary>
internal sealed class FakeLocalLlmProvider : ILlmProvider
{
    private readonly bool _available;
    private readonly string _response;
    private readonly bool _throwOnComplete;

    public FakeLocalLlmProvider(bool available, string response, bool throwOnComplete = false)
    {
        _available = available;
        _response = response;
        _throwOnComplete = throwOnComplete;
    }

    public int CompletionCalls { get; private set; }
    public string? LastUserPrompt { get; private set; }
    public string? LastSystemPrompt { get; private set; }

    public string ProviderName => "FakeLocal";

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(_available);

    public Task<string> CompleteAsync(string prompt, CancellationToken ct = default) => Task.FromResult(_response);

    public Task<string> CompleteWithContextAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        CompletionCalls++;
        LastSystemPrompt = systemPrompt;
        LastUserPrompt = userPrompt;
        if (_throwOnComplete) throw new InvalidOperationException("provider failure");
        return Task.FromResult(_response);
    }

    public Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>(new[] { "fake:1" });

    public Task SetModelAsync(string modelName, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// المساعدة تعمل عند الطلب فقط، وبالموفر المحلي المُمرَّر، ولا تُطبّق النص تلقائيًا.
/// كل النصوص هنا اصطناعية.
/// </summary>
public class OcrTextAssistServiceTests
{
    private const string SyntheticText = "حليب 12.5\nخبز 5";

    [Fact]
    public async Task Suggest_WhenProviderUnavailable_DoesNotCallCompletion()
    {
        var provider = new FakeLocalLlmProvider(available: false, response: "ignored");
        var service = new OcrTextAssistService(provider);

        var result = await service.SuggestAsync(SyntheticText);

        Assert.False(result.Available);
        Assert.Equal(0, provider.CompletionCalls);
        Assert.NotNull(result.Reason);
        Assert.DoesNotContain(SyntheticText, result.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Suggest_WhenAvailable_ReturnsSuggestionWithoutApplyingIt()
    {
        const string suggestion = "حليب 12,5\nخبز 5";
        var provider = new FakeLocalLlmProvider(available: true, response: suggestion);
        var service = new OcrTextAssistService(provider);

        var result = await service.SuggestAsync(SyntheticText);

        Assert.True(result.Available);
        Assert.Equal(suggestion, result.Suggestion);
        Assert.Equal(1, provider.CompletionCalls);
        Assert.Equal(SyntheticText, provider.LastUserPrompt);
        Assert.False(string.IsNullOrWhiteSpace(provider.LastSystemPrompt));
    }

    [Fact]
    public async Task Suggest_TruncatesLongTextToSafeLimit()
    {
        var longText = new string('x', OcrTextAssistService.MaxTextLength + 250);
        var provider = new FakeLocalLlmProvider(available: true, response: "اقتراح");
        var service = new OcrTextAssistService(provider);

        await service.SuggestAsync(longText);

        Assert.Equal(OcrTextAssistService.MaxTextLength, provider.LastUserPrompt!.Length);
    }

    [Fact]
    public async Task Suggest_StripsMarkdownFenceFromResponse()
    {
        var provider = new FakeLocalLlmProvider(available: true, response: "```\nحليب 12.5\n```");
        var service = new OcrTextAssistService(provider);

        var result = await service.SuggestAsync(SyntheticText);

        Assert.True(result.Available);
        Assert.Equal("حليب 12.5", result.Suggestion);
    }

    [Fact]
    public async Task Suggest_WhenProviderThrows_ReturnsUnavailableWithoutTextLeak()
    {
        var provider = new FakeLocalLlmProvider(available: true, response: "", throwOnComplete: true);
        var service = new OcrTextAssistService(provider);

        var result = await service.SuggestAsync(SyntheticText);

        Assert.False(result.Available);
        Assert.NotNull(result.Reason);
        Assert.DoesNotContain(SyntheticText, result.Reason!, StringComparison.Ordinal);
        Assert.DoesNotContain("provider failure", result.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Suggest_WhenTextEmpty_DoesNotCallProvider()
    {
        var provider = new FakeLocalLlmProvider(available: true, response: "ignored");
        var service = new OcrTextAssistService(provider);

        var result = await service.SuggestAsync("   ");

        Assert.False(result.Available);
        Assert.Equal(0, provider.CompletionCalls);
    }
}
