namespace Wizz.Infrastructure.Extensions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OllamaSharp;
using Wizz.Application.Abstractions;
using Wizz.Infrastructure.AI;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Devices;
using Wizz.Infrastructure.Repositories;
using Wizz.Infrastructure.Services;

/// <summary>تسجيل خدمات Infrastructure في DI Container.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddWizzInfrastructure(this IServiceCollection services, string connectionString, string ollamaBaseUrl, string ollamaModel)
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

        // AI - Local Ollama
        services.AddSingleton<ILlmProvider>(sp =>
        {
            var logger = sp.GetService<ILogger<LocalLlmProvider>>();
            return new LocalLlmProvider(ollamaBaseUrl, ollamaModel, logger);
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