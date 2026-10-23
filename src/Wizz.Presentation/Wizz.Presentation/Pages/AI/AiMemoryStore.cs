namespace Wizz.Presentation.Pages.AI;

using Microsoft.JSInterop;
using Wizz.Application.AI.Memory;

/// <summary>
/// جسر بين ذاكرة المحادثة والتفضيلات (منطق نقي في Wizz.Application) و<c>localStorage</c> في المتصفح.
/// يحمّل الحالة عند أول استخدام، يحفظها بعد كل تبديل، ويوفّر «مسح الذاكرة».
/// </summary>
public sealed class AiMemoryStore
{
    private readonly IJSRuntime _js;
    private readonly IConversationMemory _memory;
    private readonly UserPreferenceStore _preferences;
    private bool _loaded;

    public AiMemoryStore(IJSRuntime js, IConversationMemory memory, UserPreferenceStore preferences)
    {
        _js = js;
        _memory = memory;
        _preferences = preferences;
    }

    /// <summary>ذاكرة المحادثة (لكل جلسة Blazor).</summary>
    public IConversationMemory Memory => _memory;

    /// <summary>تفضيلات المستخدم.</summary>
    public UserPreferenceStore Preferences => _preferences;

    /// <summary>هل حُمّلت الحالة المحفوظة؟</summary>
    public bool IsLoaded => _loaded;

    /// <summary>يحمّل الذاكرة والتفضيلات من localStorage (مرة واحدة لكل جلسة).</summary>
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        try
        {
            var memory = await _js.InvokeAsync<StoredMemory>("wizzAiMemory.loadMemory");
            if (memory is not null)
            {
                _memory.Clear();
                foreach (var turn in memory.Turns ?? new List<StoredTurn>())
                {
                    if (string.IsNullOrWhiteSpace(turn.Question))
                    {
                        continue;
                    }

                    _memory.Add(new ConversationTurn(
                        turn.Question,
                        turn.Answer ?? string.Empty,
                        DateTime.TryParse(turn.AtUtc, out var dt) ? dt.ToUniversalTime() : DateTime.UtcNow,
                        turn.Subject,
                        turn.Intent));
                }

                _memory.SetLastSubject(memory.LastSubject);
            }

            var prefs = await _js.InvokeAsync<StoredPrefs>("wizzAiMemory.loadPrefs");
            if (prefs is not null)
            {
                _preferences.SetPriceMode(string.Equals(prefs.PriceMode, "Wholesale", StringComparison.OrdinalIgnoreCase)
                    ? PriceMode.Wholesale
                    : PriceMode.Retail);
                _preferences.SetPreferredUnit(prefs.PreferredUnit);
                _preferences.SetRememberContext(prefs.RememberContext);
                _preferences.SetAllowInternetAccess(prefs.AllowBuiltInInternet);
            }
        }
        catch
        {
            // SSR أو غياب JS — نكمل بلا ذاكرة محفوظة دون تعطيل الصفحة.
        }

        _loaded = true;
    }

    /// <summary>يحفظ الذاكرة والتفضيلات في localStorage.</summary>
    public async Task SaveAsync()
    {
        try
        {
            var payload = new StoredMemory
            {
                Turns = _memory.Recent(ConversationMemory.MaxTurns)
                    .Select(t => new StoredTurn
                    {
                        Question = t.Question,
                        Answer = t.Answer,
                        AtUtc = t.AtUtc.ToString("o"),
                        Subject = t.Subject,
                        Intent = t.Intent
                    })
                    .ToList(),
                LastSubject = _memory.LastSubject
            };

            await _js.InvokeVoidAsync("wizzAiMemory.saveMemory", payload);

            await _js.InvokeVoidAsync("wizzAiMemory.savePrefs", new StoredPrefs
            {
                PriceMode = _preferences.IsWholesalePreferred ? "Wholesale" : "Retail",
                PreferredUnit = _preferences.PreferredUnit ?? string.Empty,
                RememberContext = _preferences.RememberContext,
                AllowBuiltInInternet = _preferences.AllowInternetAccess
            });
        }
        catch
        {
            // فشل الحفظ لا يجب أن يُعطّل المحادثة.
        }
    }

    /// <summary>يمسح ذاكرة المحادثة فقط (من الذاكرة ومن localStorage) — التفضيلات تبقى.</summary>
    public async Task ClearMemoryAsync()
    {
        _memory.Clear();

        try
        {
            await _js.InvokeVoidAsync("wizzAiMemory.clearMemory");
        }
        catch
        {
            // تجاهل — الذاكرة في هذه الجلسة مُسحت أصلاً.
        }
    }

    /// <summary>يعيد التفضيلات إلى القيم الافتراضية ويمسحها من localStorage.</summary>
    public async Task ResetPreferencesAsync()
    {
        _preferences.Reset();

        try
        {
            await _js.InvokeVoidAsync("wizzAiMemory.clearPrefs");
        }
        catch
        {
            // تجاهل.
        }
    }

    /// <summary>شكل ذاكرة المحادثة كما يتوقّعها JS.</summary>
    private sealed class StoredMemory
    {
        [System.Text.Json.Serialization.JsonPropertyName("turns")]
        public List<StoredTurn>? Turns { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("lastSubject")]
        public string? LastSubject { get; set; }
    }

    /// <summary>شكل دورة المحادثة كما يتوقّعها JS.</summary>
    private sealed class StoredTurn
    {
        [System.Text.Json.Serialization.JsonPropertyName("question")]
        public string Question { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("answer")]
        public string Answer { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("atUtc")]
        public string AtUtc { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("intent")]
        public string? Intent { get; set; }
    }

    /// <summary>شكل التفضيلات كما يتوقّعها JS.</summary>
    private sealed class StoredPrefs
    {
        [System.Text.Json.Serialization.JsonPropertyName("priceMode")]
        public string PriceMode { get; set; } = "Retail";

        [System.Text.Json.Serialization.JsonPropertyName("preferredUnit")]
        public string PreferredUnit { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("rememberContext")]
        public bool RememberContext { get; set; } = true;

        [System.Text.Json.Serialization.JsonPropertyName("allowBuiltInInternet")]
        public bool AllowBuiltInInternet { get; set; }
    }
}
