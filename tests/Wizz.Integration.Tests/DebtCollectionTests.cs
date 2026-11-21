using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.Sales;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Repositories;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات تكامل لتسجيل اسم الزبون على البيع الآجل ولتحصيل الديون.
/// </summary>
public sealed class DebtCollectionTests : IAsyncLifetime
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
    public async Task CreditSale_PersistsOptionalCustomerName()
    {
        var store = await SeedStoreAsync();
        var product = await SeedProductAsync();
        var cashier = await SeedUserAsync();

        var sale = await CreateCreditAsync(product, cashier.Id, "أمين");

        Assert.True(sale.IsSuccess);
        Assert.Equal("أمين", sale.Value.CustomerName);
        Assert.Equal("أمين", (await _db.Sales.SingleAsync()).CustomerName);
        Assert.True(store.AllowCreditSales);
    }

    [Fact]
    public async Task CollectDebt_ReducesRemaining_AndKeepsOriginalPaymentMethod()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync();
        var cashier = await SeedUserAsync();
        var created = await CreateCreditAsync(product, cashier.Id, "سلمى");

        var collect = new RecordDebtPaymentUseCase(_unitOfWork);
        var result = await collect.ExecuteAsync(
            new DebtPaymentInputDto(created.Value.Id, 250m, PaymentMethodDto.Cash, "دفعة أولى"),
            cashier.Id, cashier.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(250m, result.Value.Amount);
        Assert.Equal(500m, result.Value.RemainingAfter);

        var sale = await _db.Sales.SingleAsync();
        Assert.Equal(500m, sale.Remaining);
        Assert.Equal(250m, sale.Paid);
        Assert.Equal(PaymentMethod.Credit, sale.PaymentMethod);
        Assert.Equal("سلمى", sale.CustomerName);

        var payment = await _db.DebtPayments.SingleAsync();
        Assert.Equal(created.Value.Id, payment.SaleId);
        Assert.Equal(250m, payment.Amount);
        Assert.Equal("دفعة أولى", payment.Note);
        Assert.Equal(cashier.Id, payment.RecordedByUserId);
    }

    [Fact]
    public async Task CollectDebt_Cash_CreatesDepositOnOpenShift()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync();
        var cashier = await SeedUserAsync();
        var created = await CreateCreditAsync(product, cashier.Id, null);

        Assert.NotNull((await _unitOfWork.CashierShifts.GetOpenAsync(
            (await _unitOfWork.Store.GetAsync())!.Id, cashier.Id)));

        var collect = new RecordDebtPaymentUseCase(_unitOfWork);
        var result = await collect.ExecuteAsync(
            new DebtPaymentInputDto(created.Value.Id, 100m, PaymentMethodDto.Cash),
            cashier.Id, cashier.Id);

        Assert.True(result.IsSuccess);
        var movement = await _db.CashMovements.SingleAsync();
        Assert.Equal(CashMovementType.Deposit, movement.Type);
        Assert.Equal(100m, movement.Amount);
        Assert.Contains("تحصيل دين", movement.Reason);
    }

    [Fact]
    public async Task CollectDebt_RejectsAmountAboveRemaining()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync();
        var cashier = await SeedUserAsync();
        var created = await CreateCreditAsync(product, cashier.Id, null);

        var collect = new RecordDebtPaymentUseCase(_unitOfWork);
        var result = await collect.ExecuteAsync(
            new DebtPaymentInputDto(created.Value.Id, 900m, PaymentMethodDto.Cash),
            cashier.Id, cashier.Id);

        Assert.True(result.IsFailure);
        Assert.Empty(_db.DebtPayments);
        Assert.Equal(750m, (await _db.Sales.SingleAsync()).Remaining);
    }

    private async Task<Wizz.Application.Common.Result<SaleDto>> CreateCreditAsync(
        Product product, Guid cashierId, string? customerName)
    {
        var useCase = new CreateSaleUseCase(_unitOfWork);
        return await useCase.ExecuteAsync(
            new SaleInputDto(
                new[] { new SaleLineInputDto(product.Id, product.GetBaseUnit()!.Id, 5m, false) },
                Discount: 0m,
                Paid: 0m,
                PaymentMethodDto.Credit,
                IsCreditSale: true,
                DueDate: DateTime.Today.AddDays(15),
                CustomerName: customerName),
            cashierId);
    }

    private async Task<Store> SeedStoreAsync()
    {
        var store = new Store("متجر الاختبار", "الجزائر العاصمة", "0550000000", "000000000000000", "00/00-0000000", true);
        await _unitOfWork.Store.AddAsync(store);
        await _unitOfWork.SaveChangesAsync();
        return store;
    }

    private async Task<Product> SeedProductAsync()
    {
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
