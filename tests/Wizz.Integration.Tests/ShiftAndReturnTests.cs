using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.Products;
using Wizz.Application.UseCases.Sales;
using Wizz.Application.UseCases.StockDocuments;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Repositories;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات المرحلة 2: بوابة إغلاق الوردية (T-02)، تجميد الإجماليات (T-03)،
/// المرتجع MRB (T-09)، الوردية المتزامنة (T-11)، الفتح التلقائي (T-12)،
/// بطاقة المخزون بعد الإلغاء (T-13)، وترقية قاعدة بيانات المرحلة 1 (T-14).
/// </summary>
public sealed class ShiftAndReturnTests : IAsyncLifetime
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

    // T-02 (أ): بيع داخل وردية مغلقة يُرفض تعديله وإلغاؤه، والمخزون لا يتغير.
    [Fact]
    public async Task T02_ClosedShift_RejectsModifyAndCancel()
    {
        await SeedStoreAsync();
        var cashier = await SeedUserAsync();
        var product = await CreateProductAsync(stock: 100m);
        var baseUnit = product.BaseUnit!;

        var sale = await SellAsync(product.Id, baseUnit.Id, 2m, 200m, cashier.Id);
        Assert.True(sale.IsSuccess);
        Assert.Equal(98m, await GetStockAsync(product.Id));

        var shiftId = await _db.Sales.AsNoTracking()
            .Where(s => s.Id == sale.Value.Id)
            .Select(s => s.ShiftId)
            .SingleAsync();
        Assert.NotNull(shiftId);

        var closed = await new CloseShiftUseCase(_unitOfWork).ExecuteAsync(shiftId!.Value, cashier.Id);
        Assert.True(closed.IsSuccess);

        var modified = await new ModifySaleUseCase(_unitOfWork).ExecuteAsync(
            sale.Value.Id,
            new SaleInputDto(
                new[] { new SaleLineInputDto(product.Id, baseUnit.Id, 3m, false) },
                Discount: 0m,
                Paid: 300m,
                PaymentMethodDto.Cash,
                IsCreditSale: false,
                DueDate: null));
        Assert.True(modified.IsFailure);
        Assert.Contains("مغلقة", modified.Errors.Single());

        var cancelled = await new CancelSaleUseCase(_unitOfWork)
            .ExecuteAsync(sale.Value.Id, "محاولة بعد الإغلاق", cashier.Id);
        Assert.True(cancelled.IsFailure);
        Assert.Contains("مغلقة", cancelled.Errors.Single());

        Assert.Equal(98m, await GetStockAsync(product.Id));
        _db.ChangeTracker.Clear();
        Assert.Equal(SaleStatus.Completed, await _db.Sales.AsNoTracking()
            .Where(s => s.Id == sale.Value.Id)
            .Select(s => s.Status)
            .SingleAsync());
    }

    // T-02 (ب): بيع في يوم عمل سابق يُرفض حتى لو كانت ورديته ما زالت مفتوحة.
    [Fact]
    public async Task T02_PreviousBusinessDay_RejectsModifyEvenWithOpenShift()
    {
        await SeedStoreAsync();
        var cashier = await SeedUserAsync();
        var product = await CreateProductAsync(stock: 100m);
        var baseUnit = product.BaseUnit!;

        var sale = await SellAsync(product.Id, baseUnit.Id, 2m, 200m, cashier.Id);
        Assert.True(sale.IsSuccess);

        var previousDay = DateTime.UtcNow.AddDays(-2);
        await _db.Sales.Where(s => s.Id == sale.Value.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.SaleDate, previousDay));
        _db.ChangeTracker.Clear();

        var modified = await new ModifySaleUseCase(_unitOfWork).ExecuteAsync(
            sale.Value.Id,
            new SaleInputDto(
                new[] { new SaleLineInputDto(product.Id, baseUnit.Id, 3m, false) },
                Discount: 0m,
                Paid: 300m,
                PaymentMethodDto.Cash,
                IsCreditSale: false,
                DueDate: null));

        Assert.True(modified.IsFailure);
        Assert.Contains("سابق", modified.Errors.Single());
        Assert.Equal(98m, await GetStockAsync(product.Id));

        _db.ChangeTracker.Clear();
        var shift = await _db.CashierShifts.AsNoTracking().SingleAsync();
        Assert.True(shift.IsOpen);
    }

    // T-03: إغلاق الوردية يجمّد الإجماليات، ومحاولة الإلغاء بعده لا تغيّر أي رقم.
    [Fact]
    public async Task T03_CloseShift_FreezesTotals_AndRejectsLaterCancel()
    {
        await SeedStoreAsync();
        var cashier = await SeedUserAsync();
        var product = await CreateProductAsync(stock: 100m);
        var baseUnit = product.BaseUnit!;

        var cashSale = await SellAsync(product.Id, baseUnit.Id, 1m, 100m, cashier.Id);
        Assert.True(cashSale.IsSuccess);

        var creditSale = await SellAsync(
            product.Id, baseUnit.Id, 2m, 100m, cashier.Id,
            isCredit: true, dueDate: DateTime.UtcNow.Date.AddDays(30));
        Assert.True(creditSale.IsSuccess);

        var shiftId = await _db.Sales.AsNoTracking()
            .Where(s => s.Id == cashSale.Value.Id)
            .Select(s => s.ShiftId)
            .SingleAsync();

        var closed = await new CloseShiftUseCase(_unitOfWork).ExecuteAsync(shiftId!.Value, cashier.Id);
        Assert.True(closed.IsSuccess);
        Assert.Equal(300m, closed.Value.SalesTotal);
        Assert.Equal(100m, closed.Value.CashTotal);
        Assert.Equal(100m, closed.Value.CreditTotal);
        Assert.False(closed.Value.IsOpen);
        Assert.NotNull(closed.Value.ClosedAt);
        Assert.Equal(cashier.Id, closed.Value.ClosedByUserId);

        var secondClose = await new CloseShiftUseCase(_unitOfWork).ExecuteAsync(shiftId.Value, cashier.Id);
        Assert.True(secondClose.IsFailure);

        var cancelled = await new CancelSaleUseCase(_unitOfWork)
            .ExecuteAsync(cashSale.Value.Id, "بعد الإغلاق", cashier.Id);
        Assert.True(cancelled.IsFailure);

        _db.ChangeTracker.Clear();
        var stored = await _db.CashierShifts.AsNoTracking().SingleAsync(s => s.Id == shiftId.Value);
        Assert.Equal(300m, stored.SalesTotal);
        Assert.Equal(100m, stored.CashTotal);
        Assert.Equal(100m, stored.CreditTotal);
        Assert.Equal(97m, await GetStockAsync(product.Id)); // 100 - 1 - 2
    }

    // T-09: MRB مرتبط بالبيع، يزيد المخزون بقيمته، ويرفض تجاوز المتبقي.
    [Fact]
    public async Task T09_SalesReturn_IncreasesStock_AndRejectsOverReturn()
    {
        await SeedStoreAsync();
        var cashier = await SeedUserAsync();
        var product = await CreateProductAsync(stock: 100m, units: new[]
        {
            new UnitInputDto("كيلوغرام", "كجم", 1m),
            new UnitInputDto("صندوق", "صندوق", 10m),
        });
        var box = product.Units.Single(u => u.Symbol == "صندوق");

        var sale = await SellAsync(product.Id, box.Id, 2m, 200m, cashier.Id);
        Assert.True(sale.IsSuccess);
        Assert.Equal(80m, await GetStockAsync(product.Id));

        var shiftId = await _db.Sales.AsNoTracking()
            .Where(s => s.Id == sale.Value.Id)
            .Select(s => s.ShiftId)
            .SingleAsync();
        Assert.True((await new CloseShiftUseCase(_unitOfWork).ExecuteAsync(shiftId!.Value, cashier.Id)).IsSuccess);

        var returned = await new CreateSalesReturnUseCase(_unitOfWork).ExecuteAsync(
            sale.Value.Id,
            new[] { new SalesReturnLineInputDto(product.Id, box.Id, 1m) },
            "مرتجع بعد الإغلاق",
            cashier.Id);

        Assert.True(returned.IsSuccess);
        Assert.Equal(90m, await GetStockAsync(product.Id));
        Assert.Equal(StockDocumentType.SalesReturn, returned.Value.Type);
        Assert.StartsWith("MRB-", returned.Value.Number);
        Assert.Equal(sale.Value.Id, await _db.StockDocuments.AsNoTracking()
            .Where(d => d.Id == returned.Value.Id)
            .Select(d => d.ReferenceSaleId)
            .SingleAsync());

        var stored = await _db.StockDocumentLines.AsNoTracking()
            .Where(l => l.StockDocumentId == returned.Value.Id)
            .SingleAsync();
        Assert.Equal(1m, stored.Quantity);
        Assert.Equal(10m, stored.BaseQuantityDelta);
        Assert.Equal(80m, stored.QuantityBefore);
        Assert.Equal(90m, stored.QuantityAfter);

        var overReturn = await new CreateSalesReturnUseCase(_unitOfWork).ExecuteAsync(
            sale.Value.Id,
            new[] { new SalesReturnLineInputDto(product.Id, box.Id, 2m) },
            "تجاوز المتبقي",
            cashier.Id);
        Assert.True(overReturn.IsFailure);
        Assert.Contains("تتجاوز المتبقي", overReturn.Errors.Single());
        Assert.Equal(90m, await GetStockAsync(product.Id));

        var remainingReturn = await new CreateSalesReturnUseCase(_unitOfWork).ExecuteAsync(
            sale.Value.Id,
            new[] { new SalesReturnLineInputDto(product.Id, box.Id, 1m) },
            "استكمال المرتجع",
            cashier.Id);
        Assert.True(remainingReturn.IsSuccess);
        Assert.Equal(100m, await GetStockAsync(product.Id));

        var exhausted = await new CreateSalesReturnUseCase(_unitOfWork).ExecuteAsync(
            sale.Value.Id,
            new[] { new SalesReturnLineInputDto(product.Id, box.Id, 1m) },
            "لا متبقٍ",
            cashier.Id);
        Assert.True(exhausted.IsFailure);

        var card = await GetCardAsync(product.Id);
        Assert.True(card.IsReconciled);
        Assert.Equal(100m, card.StoredQuantity);
    }

    // T-12: أول بيع يفتح وردية تلقائيًا، والبيع الثاني يستخدم نفس الوردية.
    [Fact]
    public async Task T12_FirstSale_OpensShift_AndSecondSaleReusesIt()
    {
        await SeedStoreAsync();
        var cashier = await SeedUserAsync();
        var product = await CreateProductAsync(stock: 100m);
        var baseUnit = product.BaseUnit!;

        var first = await SellAsync(product.Id, baseUnit.Id, 1m, 100m, cashier.Id);
        var second = await SellAsync(product.Id, baseUnit.Id, 1m, 100m, cashier.Id);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        _db.ChangeTracker.Clear();
        var shift = await _db.CashierShifts.AsNoTracking().SingleAsync();
        Assert.True(shift.IsOpen);
        Assert.Equal(cashier.Id, shift.CashierId);
        Assert.Equal(cashier.Id, shift.OpenedByUserId);

        var shiftIds = await _db.Sales.AsNoTracking().Select(s => s.ShiftId).ToListAsync();
        Assert.Equal(2, shiftIds.Count);
        Assert.All(shiftIds, id => Assert.Equal(shift.Id, id));
    }

    // T-13: الإلغاء يظهر كخروج أصلي ثم دخول إلغاء، ومجموع الحركة صفر.
    [Fact]
    public async Task T13_StockCard_ShowsCancelAsEntry_WithZeroNet()
    {
        await SeedStoreAsync();
        var cashier = await SeedUserAsync();
        var product = await CreateProductAsync(stock: 100m);
        var baseUnit = product.BaseUnit!;

        var sale = await SellAsync(product.Id, baseUnit.Id, 5m, 500m, cashier.Id);
        Assert.True(sale.IsSuccess);

        var cancelled = await new CancelSaleUseCase(_unitOfWork)
            .ExecuteAsync(sale.Value.Id, "إلغاء تجريبي", cashier.Id);
        Assert.True(cancelled.IsSuccess);

        _db.ChangeTracker.Clear();
        var storedSale = await _db.Sales.AsNoTracking().SingleAsync(s => s.Id == sale.Value.Id);
        Assert.NotNull(storedSale.CancelledAt);
        Assert.Equal(cashier.Id, storedSale.CancelledByUserId);
        Assert.Equal("إلغاء تجريبي", storedSale.CancelReason);

        var card = await GetCardAsync(product.Id);
        Assert.True(card.IsReconciled);
        Assert.Equal(100m, card.StoredQuantity);

        var saleEntries = card.Entries
            .Where(e => e.Number.StartsWith("SALE-", StringComparison.Ordinal)
                        || e.Number.StartsWith("VOID-", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, saleEntries.Count);
        Assert.Contains(saleEntries, e => e.Kind == "بيع" && e.QuantityDelta == -5m);
        Assert.Contains(saleEntries, e => e.Kind == "إلغاء بيع" && e.QuantityDelta == 5m);
        Assert.Equal(0m, saleEntries.Sum(e => e.QuantityDelta));
    }

    private async Task<Wizz.Application.Common.Result<SaleDto>> SellAsync(
        Guid productId,
        Guid unitId,
        decimal quantity,
        decimal paid,
        Guid cashierId,
        bool isCredit = false,
        DateTime? dueDate = null)
    {
        var input = new SaleInputDto(
            new[] { new SaleLineInputDto(productId, unitId, quantity, false) },
            Discount: 0m,
            Paid: paid,
            isCredit ? PaymentMethodDto.Credit : PaymentMethodDto.Cash,
            isCredit,
            dueDate);

        return await new CreateSaleUseCase(_unitOfWork).ExecuteAsync(input, cashierId);
    }

    private async Task<ProductDto> CreateProductAsync(
        decimal stock = 0m,
        string barcode = "SHIFT-001",
        IReadOnlyList<UnitInputDto>? units = null)
    {
        _db.ChangeTracker.Clear();
        var input = new ProductInputDto(
            "منتج اختبار", 100m, 80m, false, barcode, null,
            units ?? new[] { new UnitInputDto("كيلوغرام", "كجم", 1m) },
            StockQuantity: stock,
            ReorderLevel: 0m);

        var created = await new CreateProductUseCase(_unitOfWork).ExecuteAsync(input);
        Assert.True(created.IsSuccess);
        return created.Value;
    }

    private async Task<decimal> GetStockAsync(Guid productId)
    {
        _db.ChangeTracker.Clear();
        return await _db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => p.StockQuantity)
            .SingleAsync();
    }

    private async Task<StockCardDto> GetCardAsync(Guid productId)
    {
        _db.ChangeTracker.Clear();
        var result = await new GetStockCardUseCase(_unitOfWork).ExecuteAsync(productId);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task<Store> SeedStoreAsync()
    {
        var store = new Store(
            "متجر الوردية", "الجزائر العاصمة", "0550000000", "000000000000000", "00/00-0000000",
            allowCreditSales: true, inventoryEnabled: true);

        await _unitOfWork.Store.AddAsync(store);
        await _unitOfWork.SaveChangesAsync();
        return store;
    }

    private async Task<User> SeedUserAsync()
    {
        var user = new User("cashier", "hash", "كاشير اختبار", UserRole.Cashier);
        await _unitOfWork.Users.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();
        return user;
    }
}

/// <summary>T-11: محاولتا فتح وردية متوازيتان لنفس الكاشير تنتجان وردية واحدة.</summary>
public sealed class ConcurrentShiftOpenTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"wizz_shift_{Guid.NewGuid():N}.db");
    private DbContextOptions<WizzDbContext> _options = null!;
    private Guid _cashierId;

    public async Task InitializeAsync()
    {
        _options = new DbContextOptionsBuilder<WizzDbContext>()
            .UseSqlite($"Data Source={_databasePath};Default Timeout=60")
            .Options;

        await using var db = new WizzDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var store = new Store("متجر التزامن", null, null, null, null, false, inventoryEnabled: true);
        var user = new User("cashier", "hash", "كاشير التزامن", UserRole.Cashier);

        db.Stores.Add(store);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        _cashierId = user.Id;
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task T11_ParallelOpen_ProducesSingleOpenShift()
    {
        var attempts = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await using var db = new WizzDbContext(_options);
            var unitOfWork = new UnitOfWork(db);
            return await new OpenShiftUseCase(unitOfWork).ExecuteAsync(_cashierId, _cashierId);
        }));

        var results = await Task.WhenAll(attempts);

        Assert.Equal(1, results.Count(r => r.IsSuccess));
        Assert.Equal(1, results.Count(r => r.IsFailure));

        await using var verify = new WizzDbContext(_options);
        Assert.Equal(1, await verify.CashierShifts.CountAsync(s => s.ClosedAt == null));
    }
}

/// <summary>T-14: تطبيق مهاجرة المرحلة 2 على قاعدة بيانات من المرحلة 1 يحفظ البيانات.</summary>
public sealed class ShiftMigrationUpgradeTests : IAsyncLifetime
{
    private const string PhaseOneMigration = "20261114101819_AddStockDocuments";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"wizz_upgrade_{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task T14_UpgradeFromPhaseOne_PreservesData_AndLeavesShiftNull()
    {
        var options = new DbContextOptionsBuilder<WizzDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        var storeId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var saleId = Guid.NewGuid();
        var saleDate = new DateTime(2026, 11, 10, 9, 30, 0, DateTimeKind.Utc);

        await using (var db = new WizzDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync(PhaseOneMigration);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Stores" ("Id", "Name", "AllowCreditSales", "CreatedAt")
                VALUES ({storeId}, {"متجر ما قبل الترقية"}, {0}, {saleDate})
                """);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Products" ("Id", "Name", "RetailPrice", "WholesalePrice", "IsService", "IsActive", "CreatedAt", "StockQuantity", "ReorderLevel")
                VALUES ({productId}, {"منتج قديم"}, {100m}, {80m}, {0}, {1}, {saleDate}, {50m}, {0m})
                """);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Sales" ("Id", "StoreId", "CashierId", "SaleDate", "Status", "Subtotal", "Discount", "Total", "Paid", "Remaining", "PaymentMethod", "IsCreditSale", "DueDate", "InventoryApplied")
                VALUES ({saleId}, {storeId}, NULL, {saleDate}, {1}, {100m}, {0m}, {100m}, {100m}, {0m}, {0}, {0}, NULL, {1})
                """);
        }

        await using (var upgraded = new WizzDbContext(options))
        {
            await upgraded.Database.MigrateAsync();

            Assert.Equal(1, await upgraded.Stores.CountAsync(s => s.Id == storeId));
            Assert.Equal(1, await upgraded.Products.CountAsync(p => p.Id == productId));
            Assert.Equal(0, await upgraded.CashierShifts.CountAsync());

            var sale = await upgraded.Sales.AsNoTracking().SingleAsync(s => s.Id == saleId);
            Assert.Null(sale.ShiftId);
            Assert.Null(sale.CancelledAt);
            Assert.Null(sale.ModifiedAt);
            Assert.Equal(100m, sale.Total);

            var applied = await upgraded.Database
                .SqlQuery<string>($"SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\"")
                .ToListAsync();
            Assert.Contains("20261114123401_AddCashierShiftsAndSaleAudit", applied);
        }
    }
}
