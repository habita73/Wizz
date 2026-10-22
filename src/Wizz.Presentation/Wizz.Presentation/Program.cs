using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.AI;
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
builder.Services.AddScoped<CorrectSaleUseCase>();
builder.Services.AddScoped<CreateProductUseCase>();
builder.Services.AddScoped<GetProductUseCase>();
builder.Services.AddScoped<UpdateProductUseCase>();
builder.Services.AddScoped<SearchProductsUseCase>();
builder.Services.AddScoped<InitializeStoreUseCase>();
builder.Services.AddScoped<GetStoreSettingsUseCase>();
builder.Services.AddScoped<GenerateReceiptUseCase>();
builder.Services.AddScoped<BackupDatabaseUseCase>();
builder.Services.AddScoped<RestoreDatabaseUseCase>();
builder.Services.AddScoped<BuiltInAssistant>();
builder.Services.AddScoped<QueryAiUseCase>();
builder.Services.AddScoped<Wizz.Presentation.Pages.AI.AiHistoryStore>();

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
        return;
    }

    var apple = new Product("تفاح أحمر", 150.00m, 120.00m, false, "FRUIT-001", "تفاح أحمر طازج");
    apple.AddUnit("كيلوغرام", "كجم", 1m);
    apple.AddUnit("صندوق", "صندوق", 10m);

    var milk = new Product("حليب كامل الدسم", 120.00m, 100.00m, false, "DAIRY-001", "حليب طازج 1 لتر");
    milk.AddUnit("لتر", "لتر", 1m);
    milk.AddUnit("كرتونة", "كرتونة", 12m);

    var bread = new Product("خبز أبيض", 30.00m, 25.00m, false, "BAKERY-001", "خبز طازج يومي");
    bread.AddUnit("رغيف", "رغيف", 1m);
    bread.AddUnit("كيس", "كيس", 10m);

    var water = new Product("ماء معدني", 20.00m, 15.00m, false, "WATER-001", "ماء معدني طبيعي 1.5 لتر");
    water.AddUnit("قارورة", "قارورة", 1m);
    water.AddUnit("صندوق", "صندوق", 12m);

    var delivery = new Product("خدمة توصيل", 200.00m, 150.00m, true, "SVC-001", "توصيل إلى المنازل");
    delivery.AddUnit("طلب", "طلب", 1m);

    foreach (var product in new[] { apple, milk, bread, water, delivery })
    {
        await productRepo.AddAsync(product);
    }

    await unitOfWork.SaveChangesAsync();
}
