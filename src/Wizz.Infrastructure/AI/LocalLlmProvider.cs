namespace Wizz.Infrastructure.AI;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using Wizz.Application.Abstractions;

public sealed class LocalLlmProvider : ILlmProvider, IDisposable
{
    private readonly OllamaApiClient _client;
    private readonly ILogger<LocalLlmProvider>? _logger;
    private string _model;

    public string ProviderName => "Ollama (محلي)";

    public LocalLlmProvider(string baseUrl, string model, ILogger<LocalLlmProvider>? logger = null)
    {
        _model = model;
        _logger = logger;
        _client = new OllamaApiClient(new Uri(baseUrl), model);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var models = await _client.ListLocalModelsAsync(ct);
            return models.Any(m => m.Name.Contains(_model, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Ollama غير متاح: {Message}", ex.Message);
            return false;
        }
    }

    public async Task<string> CompleteAsync(string prompt, CancellationToken ct = default)
    {
        try
        {
            var request = new OllamaSharp.Models.GenerateRequest
            {
                Model = _model,
                Prompt = prompt,
                Stream = false
            };
            var fullResponse = string.Empty;
            await foreach (var chunk in _client.GenerateAsync(request, ct))
            {
                if (chunk?.Response != null)
                    fullResponse += chunk.Response;
            }
            return fullResponse;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "خطأ في استكمال Ollama: {Message}", ex.Message);
            throw;
        }
    }

    public async Task<string> CompleteWithContextAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        try
        {
            var fullPrompt = $"{systemPrompt}\n\nالمستخدم: {userPrompt}\nالمساعد:";
            return await CompleteAsync(fullPrompt, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "خطأ في استكمال Ollama مع سياق: {Message}", ex.Message);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var models = await _client.ListLocalModelsAsync(ct);
            return models.Select(m => m.Name).ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "فشل الحصول على النماذج: {Message}", ex.Message);
            return Array.Empty<string>();
        }
    }

    public async Task SetModelAsync(string modelName, CancellationToken ct = default)
    {
        var models = await GetAvailableModelsAsync(ct);
        if (!models.Any(m => m.Equals(modelName, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"النموذج '{modelName}' غير متاح محلياً", nameof(modelName));

        _model = modelName;
        _client.SelectedModel = modelName;
    }

    public void Dispose() => _client?.Dispose();
}