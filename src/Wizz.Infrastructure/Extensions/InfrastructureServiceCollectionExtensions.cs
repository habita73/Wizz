namespace Wizz.Infrastructure.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using System.Linq;
using Wizz.Application.Abstractions;
using Wizz.Application.AI;
using Wizz.Infrastructure.AI;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Devices;
using Wizz.Infrastructure.Repositories;
using Wizz.Infrastructure.Services;

/// <summary>تسجيل خدمات Infrastructure في DI Container.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddWizzInfrastructure(
        this IServiceCollection services,
        string connectionString,
        string ollamaBaseUrl,
        string ollamaModel,
        IConfiguration configuration)
    {
        // DbContext
        services.AddDbContext<WizzDbContext>(options =>
            options.UseSqlite(connectionString));

        // Unit of Work & Repositories
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ISaleRepository, SaleRepository>();
        services.AddScoped<IStoreRepository, StoreRepository>();
        services.AddScoped<IUserRepository, UserRepository>();

        // Devices (Mock for MVP)
        services.AddSingleton<IBarcodeReader, MockScanner>();
        services.AddSingleton<IPrinter, MockPrinter>();

        // AI - Local Ollama (Singleton واحد للمحلي)
        // ملاحظة: AiSettingsProvider Singleton (أُضيف أدناه) — نحقنه لاحقاً لتجنب lifetime mismatch
        services.AddKeyedSingleton<ILlmProvider>("local", (sp, _) =>
        {
            var settings = sp.GetRequiredService<IAiSettingsProvider>();
            var logger = sp.GetService<ILogger<LocalLlmProvider>>();
            return new LocalLlmProvider(ollamaBaseUrl, ollamaModel, settings, logger);
        });

        // AI - الإعدادات (Singleton حتى تُشارَك عبر الخدمات والعمليات)
        services.AddSingleton<IAiSettingsProvider, AiSettingsProvider>();

        // AI - مصنع موفري السحابة
        services.AddSingleton<ILlmProviderFactory, AiProviderFactory>();

        // AI - مقيّم قدرة الجهاز (Singleton)
        services.AddSingleton<IDeviceCapabilityEvaluator>(sp =>
        {
            var localProvider = sp.GetKeyedService<ILlmProvider>("local");
            return new DeviceCapabilityEvaluator(localProvider);
        });

        // AI - مُتبّع الحصة المجانية (Scoped)
        services.AddScoped<IQuotaTracker>(sp =>
        {
            var settings = sp.GetRequiredService<IAiSettingsProvider>();
            return new QuotaTracker(settings.MonthlyFreeTokenLimit);
        });

        // AI - سياسة الإرجاع الآمن (Singleton)
        services.AddSingleton<IFallbackPolicy>(sp =>
        {
            var maxAttemptsStr = configuration["AI:FallbackMaxAttempts"];
            var maxAttempts = string.IsNullOrEmpty(maxAttemptsStr) ? 2 : int.Parse(maxAttemptsStr);
            return new FallbackPolicy(maxAttempts);
        });

        // AI - موجه النماذج المستقل (Scoped)
        services.AddScoped<IModelRouter>(sp =>
        {
            var builtIn = sp.GetRequiredService<BuiltInAssistant>();
            var localLlm = sp.GetKeyedService<ILlmProvider>("local");
            var settings = sp.GetRequiredService<IAiSettingsProvider>();
            var deviceEvaluator = sp.GetRequiredService<IDeviceCapabilityEvaluator>();
            var quotaTracker = sp.GetRequiredService<IQuotaTracker>();
            var fallbackPolicy = sp.GetRequiredService<IFallbackPolicy>();
            var providerFactory = sp.GetRequiredService<ILlmProviderFactory>();

            return new ModelRouter(builtIn, localLlm, settings, deviceEvaluator, quotaTracker, fallbackPolicy, providerFactory);
        });

        // AI Tools
        services.AddScoped<GetSalesSummaryTool>();
        services.AddScoped<SearchProductTool>();
        services.AddScoped<GetStoreInfoTool>();

        // سجل أدوات الذكاء الاصطناعي — يُبنى مرة واحدة لكل نطاق ويُسجَّل فيه الأدوات
        services.AddScoped<IAiToolRegistry>(sp =>
        {
            var registry = new AiToolRegistry();
            registry.Register(sp.GetRequiredService<GetSalesSummaryTool>());
            registry.Register(sp.GetRequiredService<SearchProductTool>());
            registry.Register(sp.GetRequiredService<GetStoreInfoTool>());
            return registry;
        });

        // Backup Service — يعتمد على DbContext المُدار، لذا يجب أن يكون Scoped
        services.AddScoped<IBackupService>(sp =>
            new BackupService(sp.GetRequiredService<WizzDbContext>()));

        return services;
    }
}