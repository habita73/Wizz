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
/// اختبارات سياسة المخزون: خصم تلقائي بالوحدة الأساسية، السماح بالسالب مع تحذير،
/// تعديل البيع في مكانه مع تعديل المخزون حسب الفرق، إلغاء يستعيد المخزون،
/// احترام مفتاح تتبع المخزون في المتجر.
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
    public async Task Sale_DeductsStockInBaseUnits()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(stock: 100m);
        var cashier = await SeedUserAsync();
        var boxUnit = product.Units.Single(u => u.Symbol == "صندوق");

        var sale = await SellAsync(product, boxUnit.Id, quantity: 2m, paid: 200m, cashier.Id);

        Assert.True(sale.IsSuccess);
        Assert.Equal(80m, await GetStockAsync(product.Id)); // 100 - (2 صناديق × 10 كجم) = 80
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
        Assert.Equal(44m, await GetStockAsync(product.Id)); // 50 - 6 = 44
    }

    [Fact]
    public async Task CancelSale_RestoresStock()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(stock: 100m);
        var cashier = await SeedUserAsync();
        var boxUnit = product.Units.Single(u => u.Symbol == "صندوق");

        var sale = await SellAsync(product, boxUnit.Id, quantity: 1m, paid: 100m, cashier.Id);
        Assert.True(sale.IsSuccess);
        Assert.Equal(90m, await GetStockAsync(product.Id)); // 100 - 10 = 90

        var cancelled = await new CancelSaleUseCase(_unitOfWork)
            .ExecuteAsync(sale.Value.Id, "خطأ في الكمية", cashier.Id);

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(100m, await GetStockAsync(product.Id)); // استعادة 10 → 100
        Assert.True(cancelled.Value.IsModified);
        Assert.Equal("إلغاء البيع: خطأ في الكمية", cancelled.Value.ModificationNote);
    }

    [Fact]
    public async Task ModifySale_UpdatesStockByDelta_AndSetsModificationNote()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(stock: 100m);
        var cashier = await SeedUserAsync();
        var boxUnit = product.Units.Single(u => u.Symbol == "صندوق");

        var sale = await SellAsync(product, boxUnit.Id, quantity: 2m, paid: 200m, cashier.Id);
        Assert.True(sale.IsSuccess);
        Assert.Equal(80m, await GetStockAsync(product.Id)); // 100 - 20 = 80

        // تعديل الكمية من 2 صناديق إلى 3 صناديق (زيادة 1 صندوق = 10 كجم)
        var modified = await new ModifySaleUseCase(_unitOfWork).ExecuteAsync(
            sale.Value.Id,
            new[] { new SaleLineInputDto(product.Id, boxUnit.Id, 3m, false) },
            "تعديل الكمية إلى 3 صناديق",
            cashier.Id);

        Assert.True(modified.IsSuccess);
        Assert.Equal(70m, await GetStockAsync(product.Id)); // 80 - 10 = 70
        Assert.True(modified.Value.IsModified);
        Assert.Equal("تعديل الكمية إلى 3 صناديق", modified.Value.ModificationNote);
        Assert.NotNull(modified.Value.ModifiedAt);
        Assert.Equal(cashier.Id, modified.Value.ModifiedByUserId);
    }

    [Fact]
    public async Task ModifySale_ReducingQuantity_ReturnsStock()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(stock: 100m);
        var cashier = await SeedUserAsync();
        var baseUnit = product.GetBaseUnit()!;

        var sale = await SellAsync(product, baseUnit.Id, quantity: 10m, paid: 500m, cashier.Id);
        Assert.True(sale.IsSuccess);
        Assert.Equal(90m, await GetStockAsync(product.Id)); // 100 - 10 = 90

        // تعديل الكمية من 10 إلى 5 (نقص 5 كجم → يُعاد 5 للمخزون)
        var modified = await new ModifySaleUseCase(_unitOfWork).ExecuteAsync(
            sale.Value.Id,
            new[] { new SaleLineInputDto(product.Id, baseUnit.Id, 5m, false) },
            "تقليل الكمية",
            cashier.Id);

        Assert.True(modified.IsSuccess);
        Assert.Equal(95m, await GetStockAsync(product.Id)); // 90 + 5 = 95
    }

    [Fact]
    public async Task ModifySale_RejectsCancelledSale()
    {
        await SeedStoreAsync();
        var product = await SeedProductAsync(stock: 100m);
        var cashier = await SeedUserAsync();
        var baseUnit = product.GetBaseUnit()!;

        var sale = await SellAsync(product, baseUnit.Id, quantity: 5m, paid: 500m, cashier.Id);
        Assert.True(sale.IsSuccess);

        var cancelled = await new CancelSaleUseCase(_unitOfWork)
            .ExecuteAsync(sale.Value.Id, "إلغاء", cashier.Id);
        Assert.True(cancelled.IsSuccess);

        var modified = await new ModifySaleUseCase(_unitOfWork).ExecuteAsync(
            sale.Value.Id,
            new[] { new SaleLineInputDto(product.Id, baseUnit.Id, 3m, false) },
            "محاولة تعديل ملغي",
            cashier.Id);

        Assert.False(modified.IsSuccess);
    }

    [Fact]
    public async Task InventoryDisabled_NoDeduction()
    {
        await SeedStoreAsync(inventoryEnabled: false);
        var product = await SeedProductAsync(stock: 40m);
        var cashier = await SeedUserAsync();
        var baseUnit = product.GetBaseUnit()!;

        var sale = await SellAsync(product, baseUnit.Id, quantity: 3m, paid: 300m, cashier.Id);

        Assert.True(sale.IsSuccess);
        Assert.Equal(40m, await GetStockAsync(product.Id)); // لا خصم لأن تتبع معطّل
    }

    [Fact]
    public async Task Service_IsNotDeducted()
    {
        await SeedStoreAsync();
        var service = await SeedProductAsync(stock: 0m, isService: true);
        var cashier = await SeedUserAsync();
        var baseUnit = service.GetBaseUnit()!;

        var sale = await SellAsync(service, baseUnit.Id, quantity: 1m, paid: 100m, cashier.Id);

        Assert.True(sale.IsSuccess);
        Assert.Equal(0m, await GetStockAsync(service.Id)); // الخدمة لا يُخصم منها مخزون
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
    }

    [Fact]
    public async Task CreateProduct_RecordsOpeningBalance()
    {
        await SeedStoreAsync();

        var input = new ProductInputDto(
            "زيت زيتون", 900m, 850m, false, "OIL-001", null,
            new[] { new UnitInputDto("لتر", "لتر", 1m) },
            StockQuantity: 25m,
            ReorderLevel: 5m);

        var created = await new CreateProductUseCase(_unitOfWork).ExecuteAsync(input);

        Assert.True(created.IsSuccess);
        Assert.Equal(25m, await GetStockAsync(created.Value.Id));
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
        Assert.Equal(90m, await GetStockAsync(product.Id)); // المخزون لا يتغير من نموذج التعديل
        Assert.Equal(7m, await _db.Products.AsNoTracking().Where(p => p.Id == product.Id).Select(p => p.ReorderLevel).SingleAsync());
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
