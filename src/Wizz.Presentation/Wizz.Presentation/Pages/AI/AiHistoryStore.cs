namespace Wizz.Presentation.Pages.AI;

using Microsoft.JSInterop;
using Wizz.Application.AI.History;

/// <summary>
/// جسر بين <see cref="AiHistoryService"/> (منطق نقي) و<c>localStorage</c> في المتصفح.
/// يحمّل التاريخ عند أول استخدام ويحفظه بعد كل تسجيل سؤال.
/// </summary>
public sealed class AiHistoryStore
{
    private readonly IJSRuntime _js;
    private AiHistoryService? _service;
    private bool _loaded;

    public AiHistoryStore(IJSRuntime js) => _js = js;

    /// <summary>يعيد خدمة التاريخ (يُحمَّل من localStorage مرة واحدة).</summary>
    public async Task<AiHistoryService> GetAsync()
    {
        if (_service is not null) return _service;

        _service = new AiHistoryService(await LoadEntriesAsync());
        _loaded = true;
        return _service;
    }

    /// <summary>يسجّل سؤالاً ويحفظه.</summary>
    public async Task RecordAsync(string question)
    {
        var service = await GetAsync();
        service.Record(question);
        await SaveAsync(service);
    }

    /// <summary>يمسح كل التاريخ من الذاكرة ومن localStorage.</summary>
    public async Task ClearAsync()
    {
        var service = await GetAsync();
        service.Clear();
        await SaveAsync(service);
    }

    /// <summary>هل حُمّل التاريخ فعلاً (للتفادي إعادة القراءة)؟</summary>
    public bool IsLoaded => _loaded;

    private async Task<List<AiHistoryEntry>> LoadEntriesAsync()
    {
        try
        {
            var rows = await _js.InvokeAsync<List<StoredEntry>>("wizzAiHistory.load");
            if (rows is null) return new List<AiHistoryEntry>();

            return rows
                .Where(r => !string.IsNullOrWhiteSpace(r.Question))
                .Select(r => new AiHistoryEntry(
                    r.Question,
                    Math.Max(1, r.Count),
                    DateTime.TryParse(r.LastAskedAt, out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow))
                .ToList();
        }
        catch
        {
            // SSR أو غياب JS — نرجع تاريخاً فارغاً دون تعطيل الصفحة.
            return new List<AiHistoryEntry>();
        }
    }

    private async Task SaveAsync(AiHistoryService service)
    {
        try
        {
            var payload = service.Entries
                .Select(e => new StoredEntry(e.Question, e.Count, e.LastAskedAt.ToString("o")))
                .ToList();

            await _js.InvokeVoidAsync("wizzAiHistory.save", payload);
        }
        catch
        {
            // فشل الحفظ لا يجب أن يُعطّل المحادثة.
        }
    }

    /// <summary>شكل البيانات المتوقّع من JS (أسماء الحقول بحروف صغيرة).</summary>
    private sealed record StoredEntry(
        [property: System.Text.Json.Serialization.JsonPropertyName("question")] string Question,
        [property: System.Text.Json.Serialization.JsonPropertyName("count")] int Count,
        [property: System.Text.Json.Serialization.JsonPropertyName("lastAskedAt")] string LastAskedAt);
}
