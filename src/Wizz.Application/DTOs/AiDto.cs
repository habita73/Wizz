namespace Wizz.Application.DTOs;

using Wizz.Application.Abstractions;

/// <summary>طلب استفسار AI.</summary>
public sealed record AiQueryInputDto(
    string Question,
    bool IncludeStoreContext,
    bool IncludeRecentSales,
    int RecentSalesLimit,
    RouteType? ForceRoute = null
);

/// <summary>رد AI.</summary>
public sealed record AiQueryResultDto(
    string Answer,
    bool UsedStoreData,
    IReadOnlyList<string> ReferencedData,
    TimeSpan ResponseTime,
    string ProviderName,
    RouteDecision? Decision = null,
    string? WorkMode = null,
    string? Transparency = null,
    bool Adjudicated = false
);

/// <summary>إعدادات AI.</summary>
public sealed record AiSettingsDto(
    string ActiveProvider,
    string ActiveModel,
    IReadOnlyList<string> AvailableProviders,
    IReadOnlyList<string> AvailableModels,
    bool IsCloudEnabled,
    decimal? MonthlyTokenLimit,
    decimal? UsedTokensThisMonth
);