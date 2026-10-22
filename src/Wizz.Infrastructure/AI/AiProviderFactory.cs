namespace Wizz.Infrastructure.AI;

using Microsoft.Extensions.Logging;
using Wizz.Application.Abstractions;

/// <summary>
/// مصنع موفري الذكاء الاصطناعي: ينشئ موفرين سحابيين من إعداداتهم
/// (طبقة Infrastructure فقط تعرف تفاصيل CloudLlmProvider).
/// </summary>
public sealed class AiProviderFactory : ILlmProviderFactory
{
    private readonly ILogger<CloudLlmProvider>? _logger;

    public AiProviderFactory(ILogger<CloudLlmProvider>? logger = null)
    {
        _logger = logger;
    }

    public ILlmProvider? CreateCloudProvider(CloudProviderConfig config)
    {
        if (config is null) return null;
        if (!config.Enabled) return null;
        if (string.IsNullOrWhiteSpace(config.BaseUrl)) return null;
        if (string.IsNullOrWhiteSpace(config.ApiKey)) return null;

        try
        {
            return new CloudLlmProvider(
                config.BaseUrl,
                config.ApiKey,
                config.Model ?? "",
                string.IsNullOrWhiteSpace(config.DisplayName) ? config.Name : config.DisplayName,
                _logger);
        }
        catch
        {
            return null;
        }
    }
}