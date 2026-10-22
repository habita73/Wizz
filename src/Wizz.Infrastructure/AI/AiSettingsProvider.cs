namespace Wizz.Infrastructure.AI;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Wizz.Application.Abstractions;

/// <summary>
/// إعدادات الذكاء الاصطناعي — مزود سحابي واحد (OpenRouter) + نموذج محلي.
/// اختيارات المستخدم تُحفظ في ملف محلي داخل مجلد بيانات المستخدم.
/// </summary>
public sealed class AiSettingsProvider : IAiSettingsProvider
{
    private readonly IConfiguration _configuration;
    private readonly string _settingsPath;
    private readonly object _gate = new();
    private PersistedAiSettings _state;
    private int _usedTokensThisMonth;

    public AiSettingsProvider(IConfiguration configuration)
    {
        _configuration = configuration;
        _settingsPath = ResolveSettingsPath();
        _state = Load(_settingsPath);
    }

    internal static string ResolveSettingsPath()
    {
        var overridePath = Environment.GetEnvironmentVariable("WIZZ_AI_SETTINGS_PATH");
        if (!string.IsNullOrWhiteSpace(overridePath)) return overridePath;

        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseDir)) baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "Wizz", "ai-settings.json");
    }

    public bool IsCloudEnabled
    {
        get => _state.CloudEnabled;
        set
        {
            lock (_gate)
            {
                if (_state.CloudEnabled == value) return;
                _state.CloudEnabled = value;
                Save();
            }
        }
    }

    public string? PreferredLocalModel => _state.PreferredLocalModel ?? _configuration["AI:PreferredLocalModel"];

    public string LocalModelLevel
    {
        get
        {
            var level = _state.LocalModelLevel;
            return string.IsNullOrWhiteSpace(level) ? "light" : level.Trim().ToLowerInvariant();
        }
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "light" : value.Trim().ToLowerInvariant();
            if (normalized != "light" && normalized != "medium") normalized = "light";
            lock (_gate)
            {
                if (_state.LocalModelLevel == normalized) return;
                _state.LocalModelLevel = normalized;
                Save();
            }
        }
    }

    public string? PreferredCloudProvider => _configuration["AI:PreferredCloudProvider"] ?? "openrouter";

    public int? MonthlyFreeTokenLimit
    {
        get
        {
            var val = _configuration["AI:MonthlyFreeTokenLimit"];
            return string.IsNullOrEmpty(val) ? null : int.Parse(val);
        }
    }

    public int UsedTokensThisMonth
    {
        get { lock (_gate) return _usedTokensThisMonth; }
    }

    public bool IsQuotaExceeded =>
        MonthlyFreeTokenLimit.HasValue && UsedTokensThisMonth >= MonthlyFreeTokenLimit.Value;

    public void RecordTokenUsage(int tokens)
    {
        lock (_gate) _usedTokensThisMonth += tokens;
    }

    public void Reset()
    {
        lock (_gate) _usedTokensThisMonth = 0;
    }

    public string? GetCloudApiKey()
    {
        if (!string.IsNullOrWhiteSpace(_state.CloudApiKey)) return _state.CloudApiKey;
        var provider = PreferredCloudProvider ?? "openrouter";
        return _configuration[$"AI:CloudProviders:{provider}:ApiKey"];
    }

    public void SetCloudApiKey(string apiKey)
    {
        lock (_gate)
        {
            _state.CloudApiKey = apiKey ?? "";
            _state.CloudKeyEnabled = !string.IsNullOrWhiteSpace(apiKey);
            Save();
        }
    }

    public string? GetCloudConfigHash()
    {
        var sb = new StringBuilder();
        sb.Append(_state.CloudEnabled);
        sb.Append(_state.CloudApiKey);
        sb.Append(_state.LocalModelLevel);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash);
    }

    public IEnumerable<CloudProviderConfig>? GetCloudProvidersConfig()
    {
        // مزود واحد: OpenRouter (أو المُعدّ في appsettings)
        var provider = PreferredCloudProvider ?? "openrouter";
        var section = _configuration.GetSection($"AI:CloudProviders:{provider}");
        var baseUrl = section["BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl)) baseUrl = "https://openrouter.ai/api/v1";
        var model = section["Model"];
        if (string.IsNullOrWhiteSpace(model)) model = "qwen/qwen3-32b";
        var name = section["ProviderName"];
        if (string.IsNullOrWhiteSpace(name)) name = "OpenRouter (سحابي مجاني)";

        var hasKey = !string.IsNullOrWhiteSpace(_state.CloudApiKey)
                     || !string.IsNullOrWhiteSpace(section["ApiKey"]);

        var apiKey = !string.IsNullOrWhiteSpace(_state.CloudApiKey)
            ? _state.CloudApiKey
            : (section["ApiKey"] ?? "");

        var enabled = _state.CloudEnabled && _state.CloudKeyEnabled && !string.IsNullOrWhiteSpace(apiKey);

        // إرجاع مزود واحد فقط
        if (!enabled && string.IsNullOrWhiteSpace(apiKey))
            return new List<CloudProviderConfig>();

        return new List<CloudProviderConfig>
        {
            new CloudProviderConfig(
                Name: provider,
                DisplayName: name,
                BaseUrl: baseUrl,
                ApiKey: apiKey,
                Model: model,
                Enabled: enabled)
        };
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(_state, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // لا نريد أن يعطل حفظ الإعدادات عمل التطبيق
        }
    }

    private static PersistedAiSettings Load(string settingsPath)
    {
        try
        {
            if (!File.Exists(settingsPath)) return new PersistedAiSettings();
            var json = File.ReadAllText(settingsPath);
            if (string.IsNullOrWhiteSpace(json)) return new PersistedAiSettings();

            // دعم الترحيل من الشكل القديم (CloudProviders dictionary)
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var state = new PersistedAiSettings();
            if (root.TryGetProperty("CloudEnabled", out var ce)) state.CloudEnabled = ce.GetBoolean();
            if (root.TryGetProperty("LocalModelLevel", out var ll)) state.LocalModelLevel = ll.GetString() ?? "light";
            if (root.TryGetProperty("PreferredLocalModel", out var plm) && plm.ValueKind != JsonValueKind.Null) state.PreferredLocalModel = plm.GetString();

            // شكل جديد
            if (root.TryGetProperty("CloudApiKey", out var cak)) state.CloudApiKey = cak.GetString() ?? "";
            if (root.TryGetProperty("CloudKeyEnabled", out var cke)) state.CloudKeyEnabled = cke.GetBoolean();

            // شكل قديم: CloudProviders dictionary
            if (root.TryGetProperty("CloudProviders", out var cps) && cps.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in cps.EnumerateObject())
                {
                    if (prop.Value.TryGetProperty("ApiKey", out var ak))
                    {
                        var key = ak.GetString();
                        if (!string.IsNullOrWhiteSpace(key))
                        {
                            state.CloudApiKey = key;
                            state.CloudKeyEnabled = true;
                        }
                    }
                }
            }

            return state;
        }
        catch
        {
            return new PersistedAiSettings();
        }
    }
}

internal sealed class PersistedAiSettings
{
    public bool CloudEnabled { get; set; }
    public string LocalModelLevel { get; set; } = "light";
    public string? PreferredLocalModel { get; set; }
    public string CloudApiKey { get; set; } = "";
    public bool CloudKeyEnabled { get; set; } = false;
}