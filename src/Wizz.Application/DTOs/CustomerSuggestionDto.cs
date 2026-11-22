namespace Wizz.Application.DTOs;

/// <summary>اقتراح اسم زبون مستخرج من المبيعات والديون السابقة.</summary>
/// <param name="Name">الاسم كما خُزِّن سابقًا، ليُستخدم نصًّا له عند الاختيار.</param>
/// <param name="SaleCount">عدد المبيعات المسجلة بهذا الاسم.</param>
/// <param name="OpenDebtCount">عدد الديون المفتوحة للاسم نفسه.</param>
public sealed record CustomerSuggestionDto(string Name, int SaleCount, int OpenDebtCount);
