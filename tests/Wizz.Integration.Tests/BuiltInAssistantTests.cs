using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.AI;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.Sales;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Repositories;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات المساعد المدمج: التأكد من توجيه الأسئلة الشائعة إلى الإجابة الصحيحة
/// من بيانات SQLite الحقيقية، دون أي نموذج لغوي.
/// </summary>
public sealed class BuiltInAssistantTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WizzDbContext _db = null!;
    private UnitOfWork _unitOfWork = null!;
    private BuiltInAssistant _assistant = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var options = new DbContextOptionsBuilder<WizzDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new WizzDbContext(options);
        await _db.Database.EnsureCreatedAsync();
        _unitOfWork = new UnitOfWork(_db);
        _assistant = new BuiltInAssistant(_unitOfWork.Sales, _unitOfWork.Products);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task TodaySalesQuestion_ReturnsSalesTotals_NotTopProducts()
    {
        await SeedProductAsync();

        var answer = await _assistant.TryAnswerAsync("ما هي مبيعات اليوم؟");

        Assert.NotNull(answer);
        Assert.Contains("مبيعات اليوم حتى الآن", answer);
        Assert.DoesNotContain("أفضل المنتجات", answer);
    }

    [Fact]
    public async Task TopProductsQuestion_ReturnsRankedProducts()
    {
        var product = await SeedProductAsync();
        var cashier = await SeedUserAsync();
        await CreateCashSaleAsync(product, cashier, quantity: 2m);

        var answer = await _assistant.TryAnswerAsync("أفضل 5 منتجات مبيعاً");

        Assert.NotNull(answer);
        Assert.Contains("أفضل المنتجات مبيعاً", answer);
        Assert.Contains("تفاح أحمر", answer);
    }

    [Fact]
    public async Task DebtsQuestion_ReportsOutstandingCredit()
    {
        var product = await SeedProductAsync();
        var cashier = await SeedUserAsync();

        var sale = await new CreateSaleUseCase(_unitOfWork).ExecuteAsync(
            new SaleInputDto(
                new[] { new SaleLineInputDto(product.Id, product.GetBaseUnit()!.Id, 2m, false) },
                Discount: 0m,
                Paid: 0m,
                PaymentMethodDto.Credit,
                IsCreditSale: true,
                DueDate: DateTime.UtcNow.AddDays(30)),
            cashier.Id);

        Assert.True(sale.IsSuccess);

        var answer = await _assistant.TryAnswerAsync("كم المتبقي من الديون؟");

        Assert.NotNull(answer);
        Assert.Contains("إجمالي الديون المستحقة", answer);
        Assert.Contains("300", answer);
    }

    [Fact]
    public async Task UnknownQuestion_ReturnsNull_SoCallerCanFallBackToModel()
    {
        var answer = await _assistant.TryAnswerAsync("اشرح لي الفرق بين المحاسبة النقدية والاستحقاق");

        Assert.Null(answer);
    }

    private async Task CreateCashSaleAsync(Product product, User cashier, decimal quantity)
    {
        var sale = await new CreateSaleUseCase(_unitOfWork).ExecuteAsync(
            new SaleInputDto(
                new[] { new SaleLineInputDto(product.Id, product.GetBaseUnit()!.Id, quantity, false) },
                Discount: 0m,
                Paid: quantity * product.RetailPrice,
                PaymentMethodDto.Cash,
                IsCreditSale: false,
                DueDate: null),
            cashier.Id);

        Assert.True(sale.IsSuccess);
    }

    private async Task<Product> SeedProductAsync()
    {
        if (!await _unitOfWork.Store.ExistsAsync())
        {
            await _unitOfWork.Store.AddAsync(
                new Store("متجر الاختبار", "الجزائر العاصمة", "0550000000", "000000000000000", "00/00-0000000", true));
            await _unitOfWork.SaveChangesAsync();
        }

        var product = new Product("تفاح أحمر", 150m, 120m, false, "FRUIT-001", "تفاح طازج");
        product.AddUnit("كيلوغرام", "كجم", 1m);
        await _unitOfWork.Products.AddAsync(product);
        await _unitOfWork.SaveChangesAsync();
        return product;
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User("cashier", "hash", "كاشير اختبار", UserRole.Cashier);
        await _unitOfWork.Users.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();
        return user;
    }
}
