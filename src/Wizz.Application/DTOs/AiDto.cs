namespace Wizz.Application.DTOs;

/// <summary>طلب استفسار AI.</summary>
public sealed record AiQueryInputDto(
    string Question,
    bool IncludeStoreContext,
    bool IncludeRecentSales,
    int RecentSalesLimit
);

/// <summary>رد AI.</summary>
public sealed record AiQueryResultDto(
    string Answer,
    bool UsedStoreData,
    IReadOnlyList<string> ReferencedData,
    TimeSpan ResponseTime,
    string ProviderName
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