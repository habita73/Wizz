using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.AI;
using Wizz.Application.AI.Memory;
using Wizz.Application.AI.Routing;
using Wizz.Application.Security;
using Wizz.Application.UseCases.Backup;
using Wizz.Application.UseCases.Products;
using Wizz.Application.UseCases.Receipt;
using Wizz.Application.UseCases.Sales;
using Wizz.Application.UseCases.Store;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Extensions;
using Wizz.Presentation.Components;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// الخدمات الأساسية
// ---------------------------------------------------------------------------
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddWizzInfrastructure(
    connectionString: builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=wizz.db",
    ollamaBaseUrl: builder.Configuration["Ollama:BaseUrl"] ?? "http://localhost:11434",
    ollamaModel: builder.Configuration["Ollama:Model"] ?? "qwen2.5:1.5b",
    configuration: builder.Configuration);

// حالات الاستخدام (Application layer)
builder.Services.AddScoped<CreateSaleUseCase>();
builder.Services.AddScoped<GetSaleUseCase>();
builder.Services.AddScoped<CancelSaleUseCase>();
builder.Services.AddScoped<ModifySaleUseCase>();
builder.Services.AddScoped<CreateProductUseCase>();
builder.Services.AddScoped<GetProductUseCase>();
builder.Services.AddScoped<UpdateProductUseCase>();
builder.Services.AddScoped<AdjustStockUseCase>();
builder.Services.AddScoped<SearchProductsUseCase>();
builder.Services.AddScoped<InitializeStoreUseCase>();
builder.Services.AddScoped<GetStoreSettingsUseCase>();
builder.Services.AddScoped<GenerateReceiptUseCase>();
builder.Services.AddScoped<BackupDatabaseUseCase>();
builder.Services.AddScoped<RestoreDatabaseUseCase>();
builder.Services.AddHttpClient<Wizz.Application.AI.Tools.IWebSearch, Wizz.Infrastructure.AI.WebSearchService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("WizzPOS/1.0 (+https://github.com/habita73/wizz)");
});

builder.Services.AddScoped<BuiltInAssistant>(sp => new BuiltInAssistant(
    sp.GetRequiredService<ISaleRepository>(),
    sp.GetRequiredService<IProductRepository>(),
    sp.GetRequiredService<IStoreRepository>(),
    Wizz.Application.AI.Tools.BuiltInToolRegistry.CreateDefault(
        webSearch: sp.GetRequiredService<Wizz.Application.AI.Tools.IWebSearch>(),
        internetAllowed: () => sp.GetRequiredService<UserPreferenceStore>().AllowInternetAccess),
    sp.GetRequiredService<IConversationMemory>(),
    sp.GetRequiredService<UserPreferenceStore>()));
builder.Services.AddScoped<QueryAiUseCase>();
builder.Services.AddScoped<Wizz.Presentation.Pages.AI.AiHistoryStore>();

// ذاكرة المحادثة والتفضيلات (لكل جلسة Blazor) — تُحفظ في localStorage عبر AiMemoryStore
builder.Services.AddScoped<IConversationMemory, ConversationMemory>();
builder.Services.AddScoped<UserPreferenceStore>();
builder.Services.AddScoped<Wizz.Presentation.Pages.AI.AiMemoryStore>();

// أولوية المسارات التلقائية وسجل القرارات (لكل جلسة Blazor) — تُحفظ في localStorage
builder.Services.AddScoped(_ => AutoRoutePriority.Default);
builder.Services.AddScoped<Wizz.Application.AI.Routing.RouteDecisionLog>();
builder.Services.AddScoped<Wizz.Presentation.Pages.AI.AiRoutePriorityStore>();

var app = builder.Build();

// ---------------------------------------------------------------------------
// خط معالجة الطلبات
// ---------------------------------------------------------------------------
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// الثقافة العربية والاتجاه من اليمين إلى اليسار
var arabicCulture = new CultureInfo("ar-DZ");
CultureInfo.DefaultThreadCurrentCulture = arabicCulture;
CultureInfo.DefaultThreadCurrentUICulture = arabicCulture;

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(arabicCulture),
    SupportedCultures = new[] { arabicCulture },
    SupportedUICultures = new[] { arabicCulture },
});

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseStaticFiles();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// ---------------------------------------------------------------------------
// تهيئة قاعدة البيانات والبيانات الأولية
// ---------------------------------------------------------------------------
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WizzDbContext>();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    await SeedAsync(scope.ServiceProvider);
}

await app.RunAsync();

// ---------------------------------------------------------------------------
// البيانات الأولية — بيانات اصطناعية للتجربة فقط
// ---------------------------------------------------------------------------
static async Task SeedAsync(IServiceProvider services)
{
    var storeRepo = services.GetRequiredService<IStoreRepository>();
    var userRepo = services.GetRequiredService<IUserRepository>();
    var productRepo = services.GetRequiredService<IProductRepository>();
    var unitOfWork = services.GetRequiredService<IUnitOfWork>();

    if (!await storeRepo.ExistsAsync())
    {
        await storeRepo.AddAsync(new Store(
            name: "متجر Wizz التجريبي",
            address: "الجزائر العاصمة",
            phone: "+213 21 00 00 00",
            taxNumber: "000000000000000",
            commercialRegister: "00/00-0000000",
            allowCreditSales: true));
        await unitOfWork.SaveChangesAsync();
    }

    if (await userRepo.GetByUsernameAsync("admin") is null)
    {
        await userRepo.AddAsync(new User(
            username: "admin",
            passwordHash: PasswordHasher.Hash("Admin@123"),
            fullName: "مدير النظام",
            role: UserRole.Admin));
    }

    if (await userRepo.GetByUsernameAsync("cashier") is null)
    {
        await userRepo.AddAsync(new User(
            username: "cashier",
            passwordHash: PasswordHasher.Hash("Cashier@123"),
            fullName: "كاشير افتراضي",
            role: UserRole.Cashier));
    }

    await unitOfWork.SaveChangesAsync();

    if (await productRepo.CountAsync() > 0)
    {
        // إضافة المنتجات الجديدة فقط (لا تكرار): المطابقة بالباركود.
        await AddMissingDemoProductsAsync(productRepo, unitOfWork);
        return;
    }

    await AddMissingDemoProductsAsync(productRepo, unitOfWork);
}

// ---------------------------------------------------------------------------
// كتالوج تجريبي: أسماء وأوصاف متشابهة عمداً لاختبار البحث والتمييز (35 منتجاً)
// ---------------------------------------------------------------------------
static async Task AddMissingDemoProductsAsync(IProductRepository productRepo, IUnitOfWork unitOfWork)
{
    var existing = (await productRepo.GetAllAsync())
        .Select(p => p.Barcode)
        .Where(b => !string.IsNullOrWhiteSpace(b))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var d in DemoProductCatalog.All)
    {
        if (existing.Contains(d.Barcode))
        {
            continue;
        }

        var product = new Product(d.Name, d.Retail, d.Wholesale, d.IsService, d.Barcode, d.Description);
        foreach (var unit in d.Units)
        {
            product.AddUnit(unit.Name, unit.Symbol, unit.Quantity);
        }

        await productRepo.AddAsync(product);
    }

    await unitOfWork.SaveChangesAsync();
}

/// <summary>بيانات الكتالوج التجريبي (بيانات اصطناعية).</summary>
internal static class DemoProductCatalog
{
    /// <summary>أسماء وأوصاف متشابهة مقصودة: «حليب كامل الدسم» عدة أحجام، «تفاح» بألوان، «خبز» أنواع…</summary>
    public static readonly (string Name, decimal Retail, decimal Wholesale, bool IsService, string Barcode, string Description, (string Name, string Symbol, decimal Quantity)[] Units)[] All =
    {
        ("تفاح أحمر", 150.00m, 120.00m, false, "FRUIT-001", "تفاح أحمر طازج", new[] { ("كيلوغرام", "كجم", 1m), ("صندوق", "صندوق", 10m) }),
        ("تفاح أخضر", 165.00m, 135.00m, false, "FRUIT-002", "تفاح أخضر طازج", new[] { ("كيلوغرام", "كجم", 1m), ("صندوق", "صندوق", 10m) }),
        ("تفاح أصفر", 160.00m, 130.00m, false, "FRUIT-003", "تفاح أصفر طازج", new[] { ("كيلوغرام", "كجم", 1m), ("صندوق", "صندوق", 10m) }),
        ("موز", 240.00m, 200.00m, false, "FRUIT-004", "موز طازج", new[] { ("كيلوغرام", "كجم", 1m), ("صندوق", "صندوق", 15m) }),
        ("برتقال", 110.00m, 90.00m, false, "FRUIT-005", "برتقال طازج", new[] { ("كيلوغرام", "كجم", 1m), ("صندوق", "صندوق", 20m) }),
        ("ليمون", 130.00m, 110.00m, false, "FRUIT-006", "ليمون طازج", new[] { ("كيلوغرام", "كجم", 1m), ("صندوق", "صندوق", 10m) }),
        ("عنب أسود", 320.00m, 280.00m, false, "FRUIT-007", "عنب أسود بدون بذور", new[] { ("كيلوغرام", "كجم", 1m), ("صندوق", "صندوق", 8m) }),
        ("عنب أبيض", 300.00m, 260.00m, false, "FRUIT-008", "عنب أبيض بدون بذور", new[] { ("كيلوغرام", "كجم", 1m), ("صندوق", "صندوق", 8m) }),

        ("حليب كامل الدسم", 120.00m, 100.00m, false, "DAIRY-001", "حليب كامل الدسم طازج 1 لتر", new[] { ("لتر", "لتر", 1m), ("كرتونة", "كرتونة", 12m) }),
        ("حليب كامل الدسم 500 مل", 65.00m, 55.00m, false, "DAIRY-002", "حليب كامل الدسم طازج 500 مل", new[] { ("علبة", "علبة", 1m), ("كرتونة", "كرتونة", 24m) }),
        ("حليب نصف الدسم", 115.00m, 98.00m, false, "DAIRY-003", "حليب نصف الدسم طازج 1 لتر", new[] { ("لتر", "لتر", 1m), ("كرتونة", "كرتونة", 12m) }),
        ("حليب خالي الدسم", 125.00m, 105.00m, false, "DAIRY-004", "حليب خالي الدسم طازج 1 لتر", new[] { ("لتر", "لتر", 1m), ("كرتونة", "كرتونة", 12m) }),
        ("حليب بودرة", 380.00m, 340.00m, false, "DAIRY-005", "حليب بودرة 400 غ", new[] { ("علبة", "علبة", 1m), ("كرتونة", "كرتونة", 12m) }),
        ("حليب مكثف محلى", 210.00m, 190.00m, false, "DAIRY-006", "حليب مكثف محلى 397 غ", new[] { ("علبة", "علبة", 1m), ("كرتونة", "كرتونة", 24m) }),
        ("لبن طازج", 90.00m, 75.00m, false, "DAIRY-007", "لبن طازج 1 لتر", new[] { ("لتر", "لتر", 1m), ("كرتونة", "كرتونة", 12m) }),
        ("رايب", 70.00m, 60.00m, false, "DAIRY-008", "رايب 500 مل", new[] { ("علبة", "علبة", 1m), ("كرتونة", "كرتونة", 12m) }),
        ("جبن مثلثات", 320.00m, 290.00m, false, "DAIRY-009", "جبن مثلثات 8 قطع", new[] { ("علبة", "علبة", 1m), ("كرتونة", "كرتونة", 12m) }),
        ("جبن مبشور", 260.00m, 230.00m, false, "DAIRY-010", "جبن مبشور 200 غ", new[] { ("كيس", "كيس", 1m), ("كرتونة", "كرتونة", 20m) }),
        ("زبدة", 450.00m, 410.00m, false, "DAIRY-011", "زبدة طبيعية 250 غ", new[] { ("علبة", "علبة", 1m), ("كرتونة", "كرتونة", 20m) }),
        ("ياغورت فراولة", 45.00m, 38.00m, false, "DAIRY-012", "ياغورت بنكهة الفراولة", new[] { ("علبة", "علبة", 1m), ("كرتونة", "كرتونة", 24m) }),
        ("ياغورت طبيعي", 40.00m, 34.00m, false, "DAIRY-013", "ياغورت طبيعي بدون سكر", new[] { ("علبة", "علبة", 1m), ("كرتونة", "كرتونة", 24m) }),

        ("خبز أبيض", 30.00m, 25.00m, false, "BAKERY-001", "خبز أبيض طازج يومي", new[] { ("رغيف", "رغيف", 1m), ("كيس", "كيس", 10m) }),
        ("خبز كامل", 35.00m, 28.00m, false, "BAKERY-002", "خبز كامل طازج يومي", new[] { ("رغيف", "رغيف", 1m), ("كيس", "كيس", 10m) }),
        ("خبز صغير (باغيت)", 25.00m, 20.00m, false, "BAKERY-003", "خبز صغير طازج يومي", new[] { ("قطعة", "قطعة", 1m), ("كيس", "كيس", 20m) }),
        ("كرواسون", 60.00m, 50.00m, false, "BAKERY-004", "كرواسون بالزبدة", new[] { ("قطعة", "قطعة", 1m), ("علبة", "علبة", 6m) }),

        ("سميد", 95.00m, 80.00m, false, "GROCERY-001", "سميد متوسط 1 كجم", new[] { ("كيس", "كيس", 1m), ("شوال", "شوال", 25m) }),
        ("دقيق أبيض", 90.00m, 75.00m, false, "GROCERY-002", "دقيق أبيض 1 كجم", new[] { ("كيس", "كيس", 1m), ("شوال", "شوال", 25m) }),
        ("أرز", 180.00m, 160.00m, false, "GROCERY-003", "أرز أبيض 1 كجم", new[] { ("كيس", "كيس", 1m), ("شوال", "شوال", 25m) }),
        ("سكر أبيض", 130.00m, 115.00m, false, "GROCERY-004", "سكر أبيض 1 كجم", new[] { ("كيس", "كيس", 1m), ("شوال", "شوال", 50m) }),
        ("زيت زيتون", 1200.00m, 1050.00m, false, "GROCERY-005", "زيت زيتون بكر 1 لتر", new[] { ("قارورة", "قارورة", 1m), ("كرتونة", "كرتونة", 12m) }),
        ("زيت مازولا", 850.00m, 780.00m, false, "GROCERY-006", "زيت مازولا 5 لتر", new[] { ("قارورة", "قارورة", 1m), ("كرتونة", "كرتونة", 4m) }),

        ("ماء معدني", 20.00m, 15.00m, false, "WATER-001", "ماء معدني طبيعي 1.5 لتر", new[] { ("قارورة", "قارورة", 1m), ("صندوق", "صندوق", 12m) }),
        ("ماء معدني 0.5 لتر", 15.00m, 11.00m, false, "WATER-002", "ماء معدني طبيعي 0.5 لتر", new[] { ("قارورة", "قارورة", 1m), ("صندوق", "صندوق", 24m) }),

        ("قهوة مطحونة", 420.00m, 380.00m, false, "BEV-001", "قهوة مطحونة 250 غ", new[] { ("كيس", "كيس", 1m), ("كرتونة", "كرتونة", 12m) }),

        ("خدمة توصيل", 200.00m, 150.00m, true, "SVC-001", "توصيل إلى المنازل", new[] { ("طلب", "طلب", 1m) }),
    };
}
