using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.Receipt;
using Wizz.Application.UseCases.Sales;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Repositories;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات تكامل حقيقية على SQLite داخل الذاكرة: من إضافة المنتج حتى إنشاء البيع وتوليد الإيصال.
/// </summary>
public sealed class SaleFlowTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WizzDbContext _db = null!;
    private UnitOfWork _unitOfWork = null!;

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
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task CashSale_ComputesTotals_AndPersists()
    {
        var store = await SeedStoreAsync();
        var product = await SeedProductAsync();
        var cashier = await SeedUserAsync();
        var baseUnit = product.GetBaseUnit()!;

        var useCase = new CreateSaleUseCase(_unitOfWork);
        var input = new SaleInputDto(
            new[] { new SaleLineInputDto(product.Id, baseUnit.Id, 2.5m, false) },
            Discount: 25m,
            Paid: 350m,
            PaymentMethodDto.Cash,
            IsCreditSale: false,
            DueDate: null);

        var result = await useCase.ExecuteAsync(input, cashier.Id);

        Assert.True(result.IsSuccess);
        var sale = result.Value;
        Assert.Equal(375m, sale.Subtotal);
        Assert.Equal(25m, sale.Discount);
        Assert.Equal(350m, sale.Total);
        Assert.Equal(350m, sale.Paid);
        Assert.Equal(0m, sale.Remaining);
        Assert.Equal(store.Id, _db.Sales.Single().StoreId);
        Assert.Single(sale.Lines);
        Assert.Equal("تفاح أحمر", sale.Lines[0].ProductName);
        Assert.Equal("كجم", sale.Lines[0].UnitSymbol);
        Assert.Equal(375m, sale.Lines[0].LineTotal);
    }

    [Fact]
    public async Task WholesaleLine_UsesWholesalePrice()
    {
        var product = await SeedProductAsync();
        await SeedStoreAsync();
        var cashier = await SeedUserAsync();

        var useCase = new CreateSaleUseCase(_unitOfWork);
        var input = new SaleInputDto(
            new[] { new SaleLineInputDto(product.Id, product.GetBaseUnit()!.Id, 4m, true) },
            Discount: 0m,
            Paid: 480m,
            PaymentMethodDto.Card,
            IsCreditSale: false,
            DueDate: null);

        var result = await useCase.ExecuteAsync(input, cashier.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(480m, result.Value.Subtotal);
        Assert.True(result.Value.Lines[0].IsWholesalePrice);
        Assert.Equal(120m, result.Value.Lines[0].UnitPrice);
    }

    [Fact]
    public async Task CreditSale_TracksRemaining_AndDueDate()
    {
        var product = await SeedProductAsync(allowCreditSales: true);
        await SeedStoreAsync(allowCreditSales: true);
        var cashier = await SeedUserAsync();
        var dueDate = DateTime.UtcNow.Date.AddDays(30);

        var useCase = new CreateSaleUseCase(_unitOfWork);
        var input = new SaleInputDto(
            new[] { new SaleLineInputDto(product.Id, product.GetBaseUnit()!.Id, 2m, false) },
            Discount: 0m,
            Paid: 100m,
            PaymentMethodDto.Credit,
            IsCreditSale: true,
            DueDate: dueDate);

        var result = await useCase.ExecuteAsync(input, cashier.Id);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsCreditSale);
        Assert.Equal(300m, result.Value.Total);
        Assert.Equal(100m, result.Value.Paid);
        Assert.Equal(200m, result.Value.Remaining);
        Assert.Equal(dueDate, result.Value.DueDate);
    }

    [Fact]
    public async Task CreditSale_WhenStoreDisallowsCredit_Fails()
    {
        var product = await SeedProductAsync(allowCreditSales: false);
        var cashier = await SeedUserAsync();

        var useCase = new CreateSaleUseCase(_unitOfWork);
        var input = new SaleInputDto(
            new[] { new SaleLineInputDto(product.Id, product.GetBaseUnit()!.Id, 1m, false) },
            Discount: 0m,
            Paid: 0m,
            PaymentMethodDto.Credit,
            IsCreditSale: true,
            DueDate: DateTime.UtcNow.Date.AddDays(15));

        var result = await useCase.ExecuteAsync(input, cashier.Id);

        Assert.False(result.IsSuccess);
        Assert.NotEmpty(result.Errors);
        Assert.Empty(_db.Sales);
    }

    [Fact]
    public async Task InactiveProduct_CannotBeSold()
    {
        var product = await SeedProductAsync();
        await SeedStoreAsync();
        var cashier = await SeedUserAsync();

        product.Deactivate();
        await _unitOfWork.SaveChangesAsync();

        var useCase = new CreateSaleUseCase(_unitOfWork);
        var input = new SaleInputDto(
            new[] { new SaleLineInputDto(product.Id, product.GetBaseUnit()!.Id, 1m, false) },
            Discount: 0m,
            Paid: 150m,
            PaymentMethodDto.Cash,
            IsCreditSale: false,
            DueDate: null);

        var result = await useCase.ExecuteAsync(input, cashier.Id);

        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, e => e.Contains("غير نشط"));
    }

    [Fact]
    public async Task Receipt_IsGenerated_WithStoreAndLines()
    {
        var store = await SeedStoreAsync();
        var product = await SeedProductAsync();
        var cashier = await SeedUserAsync();

        var saleResult = await new CreateSaleUseCase(_unitOfWork).ExecuteAsync(
            new SaleInputDto(
                new[] { new SaleLineInputDto(product.Id, product.GetBaseUnit()!.Id, 3m, false) },
                Discount: 50m,
                Paid: 400m,
                PaymentMethodDto.Cash,
                IsCreditSale: false,
                DueDate: null),
            cashier.Id);

        Assert.True(saleResult.IsSuccess);

        var receipt = await new GenerateReceiptUseCase(_unitOfWork.Sales, _unitOfWork.Store)
            .ExecuteAsync(saleResult.Value.Id);

        Assert.True(receipt.IsSuccess);
        Assert.Equal(store.Name, receipt.Value.Store.Name);
        Assert.Equal(8, receipt.Value.SaleNumber.Length);
        Assert.Single(receipt.Value.Lines);
        Assert.Equal(400m, receipt.Value.Total);
        Assert.Equal(50m, receipt.Value.Discount);
        Assert.Equal("تفاح أحمر", receipt.Value.Lines[0].ProductName);
    }

    [Fact]
    public async Task Sale_IsRetrievable_AfterSave()
    {
        var product = await SeedProductAsync();
        await SeedStoreAsync();
        var cashier = await SeedUserAsync();

        var created = await new CreateSaleUseCase(_unitOfWork).ExecuteAsync(
            new SaleInputDto(
                new[] { new SaleLineInputDto(product.Id, product.GetBaseUnit()!.Id, 10m, true) },
                Discount: 0m,
                Paid: 1200m,
                PaymentMethodDto.Cash,
                IsCreditSale: false,
                DueDate: null),
            cashier.Id);

        Assert.True(created.IsSuccess);

        var getSale = new GetSaleUseCase(_unitOfWork.Sales);
        var fetched = await getSale.ExecuteAsync(created.Value.Id);

        Assert.True(fetched.IsSuccess);
        Assert.Equal(1200m, fetched.Value.Total);
        Assert.Single(fetched.Value.Lines);
        Assert.Equal("تفاح أحمر", fetched.Value.Lines[0].ProductName);
    }

    private async Task<Store> SeedStoreAsync(bool allowCreditSales = true)
    {
        var store = new Store("متجر الاختبار", "الجزائر العاصمة", "0550000000", "000000000000000", "00/00-0000000", allowCreditSales);
        await _unitOfWork.Store.AddAsync(store);
        await _unitOfWork.SaveChangesAsync();
        return store;
    }

    private async Task<Product> SeedProductAsync(bool allowCreditSales = true)
    {
        if (!await _unitOfWork.Store.ExistsAsync())
        {
            await SeedStoreAsync(allowCreditSales);
        }

        var product = new Product("تفاح أحمر", 150m, 120m, false, "FRUIT-001", "تفاح طازج");
        product.AddUnit("كيلوغرام", "كجم", 1m);
        product.AddUnit("صندوق", "صندوق", 10m);
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
