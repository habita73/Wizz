using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.Products;
using Wizz.Application.UseCases.Sales;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Repositories;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات سياسة المخزون المعتمدة: خصم تلقائي بالوحدة الأساسية، السماح بالسالب مع تحذير،
/// إرجاع عند الإلغاء، احترام مفتاح تتبع المخزون، وتسجيل كل حركة بسببها وفاعلها.
/// </summary>
public sealed class StockPolicyTests : IAsyncLifetime
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
    public async Task Sale_DeductsStockInBaseUnits_AndLogsMovement()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(stock: 100m);
        var cashier = await SeedUserAsync();
        var boxUnit = product.Units.Single(u => u.Symbol == "صندوق");

        var sale = await SellAsync(product, boxUnit.Id, quantity: 2m, paid: 200m, cashier.Id);

        Assert.True(sale.IsSuccess);
        Assert.Equal(80m, await GetStockAsync(product.Id));

        var movements = await GetMovementsAsync(product.Id);
        var movement = Assert.Single(movements);
        Assert.Equal(StockMovementType.Sale, movement.Type);
        Assert.Equal(-20m, movement.QuantityDelta);
        Assert.Equal(100m, movement.QuantityBefore);
        Assert.Equal(80m, movement.QuantityAfter);
        Assert.Equal(sale.Value.Id, movement.SaleId);
        Assert.Equal(cashier.Id, movement.CreatedByUserId);
    }

    [Fact]
    public async Task Sale_BelowAvailable_IsAllowed_AndStockGoesNegative()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(stock: 5m);
        var cashier = await SeedUserAsync();
        var baseUnit = product.GetBaseUnit()!;

        var sale = await SellAsync(product, baseUnit.Id, quantity: 8m, paid: 800m, cashier.Id);

        Assert.True(sale.IsSuccess);
        Assert.Equal(-3m, await GetStockAsync(product.Id));
        Assert.True(product.IsLowStock);

        var movement = Assert.Single(await GetMovementsAsync(product.Id));
        Assert.Equal(-8m, movement.QuantityDelta);
        Assert.Equal(-3m, movement.QuantityAfter);
    }

    [Fact]
    public async Task CreditSale_DeductsStock()
    {
        await SeedStoreAsync(allowCreditSales: true);
        var product = await SeedProductAsync(stock: 50m);
        var cashier = await SeedUserAsync();
        var baseUnit = product.GetBaseUnit()!;

        var input = new SaleInputDto(
            new[] { new SaleLineInputDto(product.Id, baseUnit.Id, 6m, false) },
            Discount: 0m,
            Paid: 0m,
            PaymentMethodDto.Credit,
            IsCreditSale: true,
            DueDate: DateTime.UtcNow.Date.AddDays(30));

        var sale = await new CreateSaleUseCase(_unitOfWork).ExecuteAsync(input, cashier.Id);

        Assert.True(sale.IsSuccess);
        Assert.Equal(44m, await GetStockAsync(product.Id));
        Assert.Single(await GetMovementsAsync(product.Id));
    }

    [Fact]
    public async Task CancelSale_RestoresStock_AndLogsCancellation()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(stock: 100m);
        var cashier = await SeedUserAsync();
        var boxUnit = product.Units.Single(u => u.Symbol == "صندوق");

        var sale = await SellAsync(product, boxUnit.Id, quantity: 1m, paid: 100m, cashier.Id);
        Assert.True(sale.IsSuccess);
        Assert.Equal(90m, await GetStockAsync(product.Id));

        var cancelled = await new CancelSaleUseCase(_unitOfWork)
            .ExecuteAsync(sale.Value.Id, "خطأ في الكمية", cashier.Id);

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(100m, await GetStockAsync(product.Id));

        var movements = await GetMovementsAsync(product.Id);
        Assert.Equal(2, movements.Count);
        Assert.Equal(StockMovementType.Sale, movements[0].Type);
        Assert.Equal(StockMovementType.SaleCancellation, movements[1].Type);
        Assert.Equal(10m, movements[1].QuantityDelta);
        Assert.Equal(100m, movements[1].QuantityAfter);
        Assert.Equal("خطأ في الكمية", movements[1].Reason);
        Assert.Equal(cashier.Id, movements[1].CreatedByUserId);
    }

    [Fact]
    public async Task Correction_KeepsNetStock_AndLogsTwoAuditMovements()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(stock: 100m);
        var cashier = await SeedUserAsync();
        var boxUnit = product.Units.Single(u => u.Symbol == "صندوق");

        var sale = await SellAsync(product, boxUnit.Id, quantity: 2m, paid: 200m, cashier.Id);
        Assert.True(sale.IsSuccess);
        Assert.Equal(80m, await GetStockAsync(product.Id));

        var corrected = await new CorrectSaleUseCase(_unitOfWork)
            .ExecuteAsync(sale.Value.Id, "تصحيح بيانات", cashier.Id);

        Assert.True(corrected.IsSuccess);
        Assert.Equal(80m, await GetStockAsync(product.Id));

        var correctionMovements = (await GetMovementsAsync(product.Id))
            .Where(m => m.Type == StockMovementType.SaleCorrection)
            .ToList();

        Assert.Equal(2, correctionMovements.Count);
        Assert.Equal(0m, correctionMovements.Sum(m => m.QuantityDelta));
        Assert.All(correctionMovements, m => Assert.Equal("تصحيح بيانات", m.Reason));
    }

    [Fact]
    public async Task InventoryDisabled_NoDeduction_AndNoMovement()
    {
        await SeedStoreAsync(inventoryEnabled: false);
        var product = await SeedProductAsync(stock: 40m);
        var cashier = await SeedUserAsync();
        var baseUnit = product.GetBaseUnit()!;

        var sale = await SellAsync(product, baseUnit.Id, quantity: 3m, paid: 300m, cashier.Id);

        Assert.True(sale.IsSuccess);
        Assert.Equal(40m, await GetStockAsync(product.Id));
        Assert.Empty(await GetMovementsAsync(product.Id));
    }

    [Fact]
    public async Task Service_IsNotDeducted_NorLogged()
    {
        await SeedStoreAsync();
        var service = await SeedProductAsync(stock: 0m, isService: true);
        var cashier = await SeedUserAsync();
        var baseUnit = service.GetBaseUnit()!;

        var sale = await SellAsync(service, baseUnit.Id, quantity: 1m, paid: 100m, cashier.Id);

        Assert.True(sale.IsSuccess);
        Assert.Empty(await GetMovementsAsync(service.Id));
    }

    [Fact]
    public async Task ManualAdjustment_UpdatesStock_AndLogsReasonAndUser()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(stock: 100m);
        var cashier = await SeedUserAsync();

        var result = await new AdjustStockUseCase(_unitOfWork)
            .ExecuteAsync(product.Id, 120m, "جرد دوري", cashier.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(120m, await GetStockAsync(product.Id));

        var movement = Assert.Single(await GetMovementsAsync(product.Id));
        Assert.Equal(StockMovementType.ManualAdjustment, movement.Type);
        Assert.Equal(20m, movement.QuantityDelta);
        Assert.Equal("جرد دوري", movement.Reason);
        Assert.Equal(cashier.Id, movement.CreatedByUserId);
    }

    [Fact]
    public async Task ManualAdjustment_RejectsNegativeQuantity_AndMissingReason()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(stock: 10m);
        var useCase = new AdjustStockUseCase(_unitOfWork);

        var negative = await useCase.ExecuteAsync(product.Id, -1m, "جرد", null);
        var noReason = await useCase.ExecuteAsync(product.Id, 20m, "  ", null);

        Assert.False(negative.IsSuccess);
        Assert.False(noReason.IsSuccess);
        Assert.Equal(10m, await GetStockAsync(product.Id));
        Assert.Empty(await GetMovementsAsync(product.Id));
    }

    [Fact]
    public async Task CreateProduct_RecordsOpeningBalanceMovement()
    {
        await SeedStoreAsync();

        var input = new ProductInputDto(
            "زيت زيتون", 900m, 850m, false, "OIL-001", null,
            new[] { new UnitInputDto("لتر", "لتر", 1m) },
            StockQuantity: 25m,
            ReorderLevel: 5m);

        var created = await new CreateProductUseCase(_unitOfWork).ExecuteAsync(input);

        Assert.True(created.IsSuccess);
        var movement = Assert.Single(await GetMovementsAsync(created.Value.Id));
        Assert.Equal(StockMovementType.OpeningBalance, movement.Type);
        Assert.Equal(25m, movement.QuantityDelta);
        Assert.Equal(25m, movement.QuantityAfter);
    }

    [Fact]
    public async Task UpdateProduct_DoesNotOverwriteStock_FromFormValue()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(stock: 100m);
        var cashier = await SeedUserAsync();
        var baseUnit = product.GetBaseUnit()!;

        var sale = await SellAsync(product, baseUnit.Id, quantity: 10m, paid: 1000m, cashier.Id);
        Assert.True(sale.IsSuccess);
        Assert.Equal(90m, await GetStockAsync(product.Id));

        var input = new ProductInputDto(
            "منتج معدّل", 100m, 80m, false, product.Barcode, null,
            new[] { new UnitInputDto("كيلوغرام", "كجم", 1m) },
            StockQuantity: 999m,
            ReorderLevel: 7m);

        var updated = await new UpdateProductUseCase(_unitOfWork).ExecuteAsync(product.Id, input);

        Assert.True(updated.IsSuccess);
        Assert.Equal(90m, await GetStockAsync(product.Id));
        Assert.Equal(7m, await _db.Products.AsNoTracking().Where(p => p.Id == product.Id).Select(p => p.ReorderLevel).SingleAsync());
        Assert.Single(await GetMovementsAsync(product.Id));
    }

    private async Task<Wizz.Application.Common.Result<SaleDto>> SellAsync(
        Product product, Guid unitId, decimal quantity, decimal paid, Guid cashierId)
    {
        var input = new SaleInputDto(
            new[] { new SaleLineInputDto(product.Id, unitId, quantity, false) },
            Discount: 0m,
            Paid: paid,
            PaymentMethodDto.Cash,
            IsCreditSale: false,
            DueDate: null);

        return await new CreateSaleUseCase(_unitOfWork).ExecuteAsync(input, cashierId);
    }

    private async Task<decimal> GetStockAsync(Guid productId)
    {
        _db.ChangeTracker.Clear();
        return await _db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => p.StockQuantity)
            .SingleAsync();
    }

    private async Task<List<StockMovement>> GetMovementsAsync(Guid productId)
    {
        _db.ChangeTracker.Clear();
        return await _db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId)
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.QuantityDelta)
            .ToListAsync();
    }

    private async Task<Store> SeedStoreAsync(bool inventoryEnabled = true, bool allowCreditSales = true)
    {
        var store = new Store(
            "متجر الاختبار", "الجزائر العاصمة", "0550000000", "000000000000000", "00/00-0000000",
            allowCreditSales, inventoryEnabled: inventoryEnabled);

        await _unitOfWork.Store.AddAsync(store);
        await _unitOfWork.SaveChangesAsync();
        return store;
    }

    private async Task<Product> SeedProductAsync(decimal stock = 0m, bool isService = false)
    {
        var product = new Product("زيت زيتون", 100m, 80m, isService, "OIL-001", null, stock);
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
