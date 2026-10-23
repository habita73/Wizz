namespace Wizz.Infrastructure.AI;

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Wizz.Application.AI.Tools;

/// <summary>
/// بحث ويب بلا مفاتيح API وبلا مكتبات إضافية:
/// ويكيبيديا العربية (مصدر أساسي)، ثم DuckDuckGo Instant Answer (بديل).
/// يُستخدم فقط عند تفعيل المستخدم للبحث، ويفشل بلطف عند انقطاع الشبكة.
/// </summary>
public sealed partial class WebSearchService : IWebSearch
{
    /// <summary>اسم المزوّد الأساسي.</summary>
    public const string WikipediaProvider = "ويكيبيديا العربية";

    /// <summary>اسم المزوّد البديل.</summary>
    public const string DuckDuckGoProvider = "DuckDuckGo";

    private readonly HttpClient _httpClient;
    private readonly ILogger<WebSearchService>? _logger;

    public WebSearchService(HttpClient httpClient, ILogger<WebSearchService>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WebSearchResult?> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return null;
        }

        var wikipedia = await TryWikipediaAsync(query.Trim(), ct);
        if (wikipedia is not null)
        {
            return wikipedia;
        }

        return await TryDuckDuckGoAsync(query.Trim(), ct);
    }

    private async Task<WebSearchResult?> TryWikipediaAsync(string query, CancellationToken ct)
    {
        try
        {
            var url = "https://ar.wikipedia.org/w/api.php?action=query&list=search&format=json&utf8=1&srlimit=3" +
                      "&srprop=snippet&srsearch=" + Uri.EscapeDataString(query);

            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!document.RootElement.TryGetProperty("query", out var queryElement) ||
                !queryElement.TryGetProperty("search", out var searchElement) ||
                searchElement.ValueKind != JsonValueKind.Array ||
                searchElement.GetArrayLength() == 0)
            {
                return null;
            }

            var snippets = new List<WebSearchSnippet>();
            foreach (var item in searchElement.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() : null;
                var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() : null;
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                snippets.Add(new WebSearchSnippet(
                    title,
                    StripHtml(snippet) is { Length: > 0 } clean ? clean : "…",
                    "https://ar.wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_'))));
            }

            if (snippets.Count == 0)
            {
                return null;
            }

            return new WebSearchResult(
                WikipediaProvider,
                $"عثرت على {snippets.Count} مقالة في ويكيبيديا العربية:",
                snippets);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "تعذّر البحث في ويكيبيديا");
            return null;
        }
    }

    private async Task<WebSearchResult?> TryDuckDuckGoAsync(string query, CancellationToken ct)
    {
        try
        {
            var url = "https://api.duckduckgo.com/?format=json&no_html=1&skip_disambig=1&q=" +
                      Uri.EscapeDataString(query);

            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = document.RootElement;

            var summary = root.TryGetProperty("AbstractText", out var abstractText)
                ? WebUtility.HtmlDecode(abstractText.GetString() ?? string.Empty)
                : string.Empty;
            var heading = root.TryGetProperty("Heading", out var h) ? h.GetString() : null;
            var abstractUrl = root.TryGetProperty("AbstractURL", out var u) ? u.GetString() : null;

            var snippets = new List<WebSearchSnippet>();
            if (!string.IsNullOrWhiteSpace(summary))
            {
                snippets.Add(new WebSearchSnippet(
                    string.IsNullOrWhiteSpace(heading) ? query : heading,
                    summary,
                    abstractUrl));
            }

            if (root.TryGetProperty("RelatedTopics", out var topics) && topics.ValueKind == JsonValueKind.Array)
            {
                foreach (var topic in topics.EnumerateArray())
                {
                    if (snippets.Count >= 3)
                    {
                        break;
                    }

                    if (topic.TryGetProperty("Text", out var text) &&
                        topic.TryGetProperty("FirstURL", out var firstUrl) &&
                        text.GetString() is { Length: > 0 } value)
                    {
                        snippets.Add(new WebSearchSnippet(query, value, firstUrl.GetString()));
                    }
                }
            }

            if (snippets.Count == 0)
            {
                return null;
            }

            return new WebSearchResult(
                DuckDuckGoProvider,
                string.IsNullOrWhiteSpace(summary) ? "ملخص سريع من الويب:" : summary,
                snippets);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "تعذّر البحث في DuckDuckGo");
            return null;
        }
    }

    /// <summary>إزالة وسوم HTML من مقتطفات ويكيبيديا (‎<span class="searchmatch">…</span>‎).</summary>
    private static string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        return WebUtility.HtmlDecode(TagsRegex().Replace(html, string.Empty)).Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagsRegex();
}
