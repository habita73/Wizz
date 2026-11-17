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
using Wizz.Infrastructure.Services;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات المرحلة 3: الصندوق وعدّ النقدية، المشتريات SHR، والتقارير (T-15…T-21 وT-25).
/// تستخدم قاعدة SQLite في الذاكرة بنفس نمط اختبارات المرحلة 2.
/// </summary>
public sealed class Phase3CashAndPurchasesTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private WizzDbContext _db = null!;
    private UnitOfWork _unitOfWork = null!;
    private User _cashier = null!;

    private static DateOnly DocumentDate => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
    private static DateOnly ReportFrom => DocumentDate.AddDays(-2);
    private static DateOnly ReportTo => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

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

        _db.Stores.Add(new Store(
            "متجر المرحلة 3", "الجزائر العاصمة", "0550000000", null, null,
            allowCreditSales: true, inventoryEnabled: true));
        _cashier = new User("cashier", "hash", "كاشير المرحلة 3", UserRole.Cashier);
        _db.Users.Add(_cashier);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // T-15: المتوقع = الفكة + مبيعات Cash النشطة + الإيداعات − السحوبات، والفرق صفر عند المطابقة.
    [Fact]
    public async Task T15_ExpectedCash_UsesDocumentedFormula_AndZeroDifference()
    {
        var opened = await new OpenShiftUseCase(_unitOfWork)
            .ExecuteAsync(_cashier.Id, _cashier.Id, 50m);
        Assert.True(opened.IsSuccess, Err(opened));
        Assert.Equal(50m, opened.Value.OpeningFloat);

        var product = await CreateProductAsync("T15", 100m);
        var sale = await SellAsync(product.Id, product.BaseUnit!.Id, 1m, 100m);
        Assert.True(sale.IsSuccess, Err(sale));

        var deposit = await new RecordCashMovementUseCase(_unitOfWork).ExecuteAsync(
            opened.Value.Id, CashMovementType.Deposit, 20m, "إيداع تحصيل", _cashier.Id);
        Assert.True(deposit.IsSuccess, Err(deposit));

        var withdrawal = await new RecordCashMovementUseCase(_unitOfWork).ExecuteAsync(
            opened.Value.Id, CashMovementType.Withdrawal, 10m, "سحب مصروف", _cashier.Id);
        Assert.True(withdrawal.IsSuccess, Err(withdrawal));

        var summary = await new GetShiftCashSummaryUseCase(_unitOfWork).ExecuteAsync(opened.Value.Id);
        Assert.True(summary.IsSuccess);
        Assert.Equal(50m, summary.Value.OpeningFloat);
        Assert.Equal(100m, summary.Value.CashSalesTotal);
        Assert.Equal(20m, summary.Value.DepositsTotal);
        Assert.Equal(10m, summary.Value.WithdrawalsTotal);
        Assert.Equal(160m, summary.Value.ExpectedCash);
        Assert.Equal(2, summary.Value.Movements.Count);

        var closed = await new CloseShiftUseCase(_unitOfWork)
            .ExecuteAsync(opened.Value.Id, _cashier.Id, 160m, null);
        Assert.True(closed.IsSuccess, Err(closed));
        Assert.Equal(160m, closed.Value.ExpectedCash);
        Assert.Equal(160m, closed.Value.CountedCash);
        Assert.Equal(0m, closed.Value.CashDifference);
    }

    // T-16: الفرق يُحسب ويُجمَّد ويظهر في التقرير؛ فرق ≠ 0 يتطلب ملاحظة والإغلاق لا يُرفض.
    [Fact]
    public async Task T16_CloseDifference_Frozen_AndRequiresNote_ButDoesNotBlockClose()
    {
        var opened = await new OpenShiftUseCase(_unitOfWork).ExecuteAsync(_cashier.Id, _cashier.Id);
        Assert.True(opened.IsSuccess);

        var product = await CreateProductAsync("T16", 100m);
        Assert.True((await SellAsync(product.Id, product.BaseUnit!.Id, 1m, 100m)).IsSuccess);

        var withoutNote = await new CloseShiftUseCase(_unitOfWork)
            .ExecuteAsync(opened.Value.Id, _cashier.Id, 95m, null);
        Assert.True(withoutNote.IsFailure);
        Assert.Contains("ملاحظة", withoutNote.Errors.Single());

        var withNote = await new CloseShiftUseCase(_unitOfWork)
            .ExecuteAsync(opened.Value.Id, _cashier.Id, 95m, "عجز ناتج عن فكة غير مسجلة");
        Assert.True(withNote.IsSuccess, Err(withNote));
        Assert.Equal(100m, withNote.Value.ExpectedCash);
        Assert.Equal(95m, withNote.Value.CountedCash);
        Assert.Equal(-5m, withNote.Value.CashDifference);

        var report = await new GetCashReportUseCase(_unitOfWork).ExecuteAsync(ReportFrom, ReportTo);
        Assert.True(report.IsSuccess);
        var row = Assert.Single(report.Value.Rows, r => r.ShiftId == opened.Value.Id);
        Assert.False(row.IsOpen);
        Assert.True(row.HasCashCount);
        Assert.Equal(-5m, row.CashDifference);
        Assert.Equal("عجز ناتج عن فكة غير مسجلة", row.CloseNote);
    }

    // T-17: لا إيداع/سحب/إغلاق بعد الإغلاق، ولا إغلاق مرتين، ولا حركة على وردية مغلقة.
    [Fact]
    public async Task T17_ClosedShift_RejectsMovementsAndSecondClose()
    {
        var opened = await new OpenShiftUseCase(_unitOfWork).ExecuteAsync(_cashier.Id, _cashier.Id);
        Assert.True(opened.IsSuccess);

        var closed = await new CloseShiftUseCase(_unitOfWork)
            .ExecuteAsync(opened.Value.Id, _cashier.Id, 0m, null);
        Assert.True(closed.IsSuccess);

        var deposit = await new RecordCashMovementUseCase(_unitOfWork).ExecuteAsync(
            opened.Value.Id, CashMovementType.Deposit, 10m, "بعد الإغلاق", _cashier.Id);
        Assert.True(deposit.IsFailure);
        Assert.Contains("مغلقة", deposit.Errors.Single());

        var withdrawal = await new RecordCashMovementUseCase(_unitOfWork).ExecuteAsync(
            opened.Value.Id, CashMovementType.Withdrawal, 10m, "بعد الإغلاق", _cashier.Id);
        Assert.True(withdrawal.IsFailure);

        var secondClose = await new CloseShiftUseCase(_unitOfWork)
            .ExecuteAsync(opened.Value.Id, _cashier.Id, 0m, null);
        Assert.True(secondClose.IsFailure);
        Assert.Contains("مغلقة بالفعل", secondClose.Errors.Single());
    }

    // T-18: إلغاء بيع نقدي داخل النافذة يخفّض المتوقع، والبيع الملغي لا يدخل الحساب.
    [Fact]
    public async Task T18_CancelCashSale_ReducesExpectedCash()
    {
        var opened = await new OpenShiftUseCase(_unitOfWork).ExecuteAsync(_cashier.Id, _cashier.Id);
        Assert.True(opened.IsSuccess);

        var product = await CreateProductAsync("T18", 100m);
        var sale = await SellAsync(product.Id, product.BaseUnit!.Id, 1m, 100m);
        Assert.True(sale.IsSuccess);

        var before = await new GetShiftCashSummaryUseCase(_unitOfWork).ExecuteAsync(opened.Value.Id);
        Assert.Equal(100m, before.Value.ExpectedCash);

        var cancelled = await new CancelSaleUseCase(_unitOfWork)
            .ExecuteAsync(sale.Value.Id, "تصحيح داخل الوردية", _cashier.Id);
        Assert.True(cancelled.IsSuccess, Err(cancelled));

        var after = await new GetShiftCashSummaryUseCase(_unitOfWork).ExecuteAsync(opened.Value.Id);
        Assert.True(after.IsSuccess);
        Assert.Equal(0m, after.Value.CashSalesTotal);
        Assert.Equal(0m, after.Value.ExpectedCash);
    }

    // T-19: إنشاء SHR يزيد المخزون بالوحدة الأساسية ويُرقَّم في تسلسل مستقل.
    [Fact]
    public async Task T19_CreatePurchase_IncreasesStock_AndUsesIndependentSHRSequence()
    {
        var product = await CreateProductAsync("T19", 0m, new[]
        {
            new UnitInputDto("كيلوغرام", "كجم", 1m),
            new UnitInputDto("صندوق", "صندوق", 10m),
        });
        var box = product.Units.Single(u => u.Symbol == "صندوق");

        var first = await new CreatePurchaseUseCase(_unitOfWork).ExecuteAsync(
            DocumentDate, "مورد اختبار", "INV-1",
            new[] { new PurchaseLineInputDto(product.Id, box.Id, 2m, 12.50m) },
            _cashier.Id);
        Assert.True(first.IsSuccess, Err(first));
        Assert.StartsWith("SHR-", first.Value.Number);
        Assert.Equal(25m, first.Value.TotalCost);
        Assert.Equal(20m, await GetStockAsync(product.Id));

        var second = await new CreatePurchaseUseCase(_unitOfWork).ExecuteAsync(
            DocumentDate, "مورد اختبار", "INV-2",
            new[] { new PurchaseLineInputDto(product.Id, box.Id, 1m, 12.50m) },
            _cashier.Id);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value.Number, second.Value.Number);
        Assert.Equal(30m, await GetStockAsync(product.Id));

        _db.ChangeTracker.Clear();
        var line = await _db.StockDocumentLines.AsNoTracking()
            .SingleAsync(l => l.StockDocumentId == first.Value.Id);
        Assert.Equal(2m, line.Quantity);
        Assert.Equal(20m, line.BaseQuantityDelta);
        Assert.Equal(0m, line.QuantityBefore);
        Assert.Equal(20m, line.QuantityAfter);
        Assert.Equal(12.50m, line.UnitCost);
        Assert.Equal(25m, line.LineCost);

        var card = await GetCardAsync(product.Id);
        Assert.True(card.IsReconciled);
        Assert.Contains(card.Entries, e => e.Kind == "شراء" && e.QuantityDelta == 20m && e.Number == first.Value.Number);
        var purchaseEntries = card.Entries.Where(e => e.Kind == "شراء").ToList();
        Assert.Equal(2, purchaseEntries.Count);
        Assert.True(purchaseEntries[0].DocumentDate <= purchaseEntries[1].DocumentDate);
    }

    // T-20: إلغاء SHR يعكس الكميات ويُبقي الرقم ويستبعده من الإجماليات ويمنع الإلغاء المكرر.
    [Fact]
    public async Task T20_VoidPurchase_ReversesStock_AndExcludesItFromTotals()
    {
        var product = await CreateProductAsync("T20", 0m);
        var purchase = await new CreatePurchaseUseCase(_unitOfWork).ExecuteAsync(
            DocumentDate, "مورد", "INV-20",
            new[] { new PurchaseLineInputDto(product.Id, product.BaseUnit!.Id, 7m, 3m) },
            _cashier.Id);
        Assert.True(purchase.IsSuccess);
        Assert.Equal(7m, await GetStockAsync(product.Id));

        var voided = await new VoidPurchaseUseCase(_unitOfWork)
            .ExecuteAsync(purchase.Value.Id, "فاتورة خاطئة", _cashier.Id);
        Assert.True(voided.IsSuccess, Err(voided));
        Assert.Equal(purchase.Value.Number, voided.Value.Number);
        Assert.True(voided.Value.IsVoided);
        Assert.Equal(0m, await GetStockAsync(product.Id));

        var secondVoid = await new VoidPurchaseUseCase(_unitOfWork)
            .ExecuteAsync(purchase.Value.Id, "محاولة مكررة", _cashier.Id);
        Assert.True(secondVoid.IsFailure);
        Assert.Contains("ملغى بالفعل", secondVoid.Errors.Single());

        var report = await new GetPurchaseReportUseCase(_unitOfWork).ExecuteAsync(ReportFrom, ReportTo);
        Assert.True(report.IsSuccess);
        Assert.Equal(0, report.Value.DocumentCount);
        Assert.Equal(1, report.Value.VoidedDocumentCount);
        Assert.Equal(0m, report.Value.TotalPurchase);

        var list = await new GetPurchasesUseCase(_unitOfWork).ExecuteAsync(ReportFrom, ReportTo, includeVoided: true);
        Assert.True(list.IsSuccess);
        var stored = Assert.Single(list.Value);
        Assert.True(stored.IsVoided);
        Assert.Equal(purchase.Value.Number, stored.Number);
    }

    // T-21: تكلفة الشراء لا تؤثر في إجماليات البيع، ولا يوجد رقم بعنوان «ربح» في التقارير.
    [Fact]
    public async Task T21_PurchaseCost_DoesNotAffectSales_AndNoProfitIsReported()
    {
        var product = await CreateProductAsync("T21", 0m);
        var purchase = await new CreatePurchaseUseCase(_unitOfWork).ExecuteAsync(
            DocumentDate, "مورد", "INV-21",
            new[] { new PurchaseLineInputDto(product.Id, product.BaseUnit!.Id, 10m, 4m) },
            _cashier.Id);
        Assert.True(purchase.IsSuccess);
        Assert.Equal(40m, purchase.Value.TotalCost);

        // سعر البيع من سعر التجزئة (100 × 2 = 200) لا من تكلفة الشراء (4/وحدة).
        var sale = await SellAsync(product.Id, product.BaseUnit!.Id, 2m, 200m);
        Assert.True(sale.IsSuccess);
        Assert.Equal(200m, sale.Value.Total);
        Assert.Equal(200m, sale.Value.Paid);

        var sales = await SaleRepoStub.GetSalesAsync(_db);
        Assert.Equal(200m, sales.Sum(s => s.Total));

        var report = await new GetPurchaseReportUseCase(_unitOfWork).ExecuteAsync(ReportFrom, ReportTo);
        Assert.True(report.IsSuccess);
        Assert.Equal(40m, report.Value.TotalPurchase);

        var forbiddenNames = new[] { "profit", "ربح", "costofsales", "تكلفةالمبيعات", "تكلفة المبيعات" };
        foreach (var property in typeof(PurchaseReportDto).GetProperties())
            Assert.DoesNotContain(property.Name, forbiddenNames, StringComparer.OrdinalIgnoreCase);
        foreach (var property in typeof(CashReportDto).GetProperties())
            Assert.DoesNotContain(property.Name, forbiddenNames, StringComparer.OrdinalIgnoreCase);
    }

    // T-25 (أ): تقرير الصندوق المغلق يساوي اللقطة المجمّدة حرفيًا بعد تغيير لاحق.
    [Fact]
    public async Task T25_CashReport_MatchesFrozenSnapshot_AfterLaterCancellation()
    {
        var opened = await new OpenShiftUseCase(_unitOfWork).ExecuteAsync(_cashier.Id, _cashier.Id, 5m);
        Assert.True(opened.IsSuccess);

        var product = await CreateProductAsync("T25", 100m);
        var sale = await SellAsync(product.Id, product.BaseUnit!.Id, 1m, 100m);
        Assert.True(sale.IsSuccess);

        var closed = await new CloseShiftUseCase(_unitOfWork)
            .ExecuteAsync(opened.Value.Id, _cashier.Id, 104m, "فائض بسيط");
        Assert.True(closed.IsSuccess, Err(closed));

        var frozen = await _db.CashierShifts.AsNoTracking()
            .SingleAsync(s => s.Id == opened.Value.Id);

        // محاولة إلغاء بعد الإغلاق مرفوضة، فلا تتغير اللقطة.
        var lateCancel = await new CancelSaleUseCase(_unitOfWork)
            .ExecuteAsync(sale.Value.Id, "بعد الإغلاق", _cashier.Id);
        Assert.True(lateCancel.IsFailure);

        var report = await new GetCashReportUseCase(_unitOfWork).ExecuteAsync(ReportFrom, ReportTo);
        Assert.True(report.IsSuccess);
        var row = Assert.Single(report.Value.Rows, r => r.ShiftId == opened.Value.Id);
        // الوردية المغلقة تعرض اللقطة المجمّدة؛ مبيعاتها النقدية مشتقة من المتوقع المجمد لا من مبيعات حية.
        Assert.Equal(
            decimal.Round(frozen.ExpectedCash!.Value - frozen.OpeningFloat
                - (frozen.DepositsTotal ?? 0m) + (frozen.WithdrawalsTotal ?? 0m),
                2, MidpointRounding.AwayFromZero),
            row.CashSalesTotal);
        Assert.Equal(frozen.OpeningFloat, row.OpeningFloat);
        Assert.Equal(frozen.DepositsTotal ?? 0m, row.DepositsTotal);
        Assert.Equal(frozen.WithdrawalsTotal ?? 0m, row.WithdrawalsTotal);
        Assert.Equal(frozen.ExpectedCash, row.ExpectedCash);
        Assert.Equal(frozen.CountedCash, row.CountedCash);
        Assert.Equal(frozen.CashDifference, row.CashDifference);
        Assert.Equal(frozen.CloseNote, row.CloseNote);
        Assert.False(row.IsOpen);
        Assert.True(row.HasCashCount);
        Assert.False(row.IsLive);
    }

    // T-25 (ب): تقرير المشتريات يستبعد الملغى ويساوي مجموع أسطر المستندات النشطة.
    [Fact]
    public async Task T25_PurchaseReport_ExcludesVoided_AndMatchesSumOfLines()
    {
        var product = await CreateProductAsync("T25B", 0m);
        var active = await new CreatePurchaseUseCase(_unitOfWork).ExecuteAsync(
            DocumentDate, "مورد أ", "INV-A",
            new[] { new PurchaseLineInputDto(product.Id, product.BaseUnit!.Id, 3m, 5m) },
            _cashier.Id);
        Assert.True(active.IsSuccess);

        var toVoid = await new CreatePurchaseUseCase(_unitOfWork).ExecuteAsync(
            DocumentDate, "مورد ب", "INV-B",
            new[] { new PurchaseLineInputDto(product.Id, product.BaseUnit!.Id, 9m, 9m) },
            _cashier.Id);
        Assert.True(toVoid.IsSuccess);
        Assert.True((await new VoidPurchaseUseCase(_unitOfWork)
            .ExecuteAsync(toVoid.Value.Id, "خطأ", _cashier.Id)).IsSuccess);

        var report = await new GetPurchaseReportUseCase(_unitOfWork).ExecuteAsync(ReportFrom, ReportTo);
        Assert.True(report.IsSuccess);
        Assert.Equal(1, report.Value.DocumentCount);
        Assert.Equal(1, report.Value.VoidedDocumentCount);
        Assert.Equal(15m, report.Value.TotalPurchase);

        var activeIds = new[] { active.Value.Id };
        var lineSum = await _db.StockDocumentLines.AsNoTracking()
            .Where(l => activeIds.Contains(l.StockDocumentId))
            .SumAsync(l => l.LineCost ?? 0m);
        Assert.Equal(lineSum, report.Value.TotalPurchase);
        Assert.Equal(15m, Assert.Single(report.Value.BySupplier).TotalPurchase);
        Assert.Equal(15m, Assert.Single(report.Value.ByProduct).TotalPurchase);
    }

    private async Task<ProductDto> CreateProductAsync(string barcode, decimal stock, IReadOnlyList<UnitInputDto>? units = null)
    {
        _db.ChangeTracker.Clear();
        var created = await new CreateProductUseCase(_unitOfWork).ExecuteAsync(new ProductInputDto(
            "منتج اختبار " + barcode, 100m, 80m, false, barcode, null,
            units ?? new[] { new UnitInputDto("كيلوغرام", "كجم", 1m) },
            StockQuantity: stock, ReorderLevel: 0m));
        Assert.True(created.IsSuccess, Err(created));
        return created.Value;
    }

    private async Task<Wizz.Application.Common.Result<SaleDto>> SellAsync(
        Guid productId, Guid unitId, decimal quantity, decimal paid)
    {
        var input = new SaleInputDto(
            new[] { new SaleLineInputDto(productId, unitId, quantity, false) },
            Discount: 0m,
            Paid: paid,
            PaymentMethodDto.Cash,
            IsCreditSale: false,
            DueDate: null);
        return await new CreateSaleUseCase(_unitOfWork).ExecuteAsync(input, _cashier.Id);
    }

    /// <summary>رسالة الخطأ بأمان: Result.Errors يرمي عند النجاح، فلا يُقرأ إلا عند الفشل.</summary>
    private static string Err<T>(Wizz.Application.Common.Result<T> result)
        => result.IsFailure ? string.Join(" | ", result.Errors) : "(عملية ناجحة)";

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
        var card = await new GetStockCardUseCase(_unitOfWork).ExecuteAsync(productId);
        Assert.True(card.IsSuccess);
        return card.Value;
    }

    /// <summary>قراءة مبيعات الفترة للتحقق من أن التكلفة لم تغيّر الإجماليات.</summary>
    private static class SaleRepoStub
    {
        public static async Task<List<Sale>> GetSalesAsync(WizzDbContext db)
            => await db.Sales.AsNoTracking().ToListAsync();
    }
}

/// <summary>T-22: مصفوفة الصلاحيات الجديدة كما حُسمت بالتفويض (الخيار أ).</summary>
public sealed class Phase3PermissionTests
{
    [Fact]
    public void T22_NewPermissions_AreGrantedToTheDocumentedRolesOnly()
    {
        var newPermissions = new[]
        {
            Permission.OperateCashDrawer,
            Permission.ManageCashDrawer,
            Permission.ManagePurchases,
            Permission.ViewPurchaseCosts,
        };

        Assert.All(newPermissions, p => Assert.True(UserRole.Cashier.HasPermission(p) == (p == Permission.OperateCashDrawer)));
        Assert.All(newPermissions, p => Assert.True(UserRole.Manager.HasPermission(p)));
        Assert.All(newPermissions, p => Assert.True(UserRole.Admin.HasPermission(p)));

        Assert.Equal(11, (int)Permission.OperateCashDrawer);
        Assert.Equal(14, (int)Permission.ViewPurchaseCosts);
        Assert.Equal(23, (int)AuditAction.ShiftOpened);
        Assert.Equal(28, (int)AuditAction.CashDifferenceRecorded);
    }
}

/// <summary>T-23: ترقية قاعدة v0.6.3 إلى المرحلة 3 بلا فقدان وبلا تغيير اللقطات المجمّدة.</summary>
public sealed class Phase3UpgradeTests : IAsyncLifetime
{
    private const string BaselineMigration = "20261114123401_AddCashierShiftsAndSaleAudit";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"wizz_phase3_upgrade_{Guid.NewGuid():N}.db");

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath)) File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task T23_UpgradeFromBaseline_PreservesData_AndMarksLegacyShiftsWithoutCount()
    {
        var options = new DbContextOptionsBuilder<WizzDbContext>()
            .UseSqlite($"Data Source={_databasePath}")
            .Options;

        var storeId = Guid.NewGuid();
        var cashierId = Guid.NewGuid();
        var shiftId = Guid.NewGuid();
        var openedAt = new DateTime(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);
        var closedAt = new DateTime(2026, 11, 10, 16, 0, 0, DateTimeKind.Utc);

        await using (var db = new WizzDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync(BaselineMigration);

            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Stores" ("Id", "Name", "AllowCreditSales", "CreatedAt")
                VALUES ({storeId}, {"متجر ما قبل الترقية"}, {0}, {openedAt})
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "CashierShifts" ("Id", "StoreId", "CashierId", "OpenedAt", "OpenedByUserId", "ClosedAt", "ClosedByUserId", "SalesTotal", "CashTotal", "CreditTotal")
                VALUES ({shiftId}, {storeId}, {cashierId}, {openedAt}, {cashierId}, {closedAt}, {cashierId}, {300m}, {200m}, {100m})
                """);
        }

        await using (var upgraded = new WizzDbContext(options))
        {
            await upgraded.Database.MigrateAsync();

            Assert.Equal(1, await upgraded.Stores.CountAsync(s => s.Id == storeId));
            var shift = await upgraded.CashierShifts.AsNoTracking().SingleAsync(s => s.Id == shiftId);
            Assert.Equal(300m, shift.SalesTotal);
            Assert.Equal(200m, shift.CashTotal);
            Assert.Equal(100m, shift.CreditTotal);
            Assert.Equal(closedAt, shift.ClosedAt);
            Assert.Equal(0m, shift.OpeningFloat);
            Assert.Null(shift.CountedCash);
            Assert.Null(shift.ExpectedCash);
            Assert.Null(shift.DepositsTotal);
            Assert.Null(shift.WithdrawalsTotal);
            Assert.Null(shift.CashDifference);
            Assert.False(shift.HasCashCount);

            var applied = await upgraded.Database
                .SqlQuery<string>($"SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\"")
                .ToListAsync();
            Assert.Contains("20261117175437_AddCashDrawerAndPurchases", applied);
            Assert.Equal(1, await upgraded.Database.SqlQuery<int>(
                $"SELECT COUNT(*) AS \"Value\" FROM sqlite_master WHERE type = 'table' AND name = 'CashMovements'").SingleAsync());
        }
    }
}

/// <summary>T-24: CashMovements ضمن الجداول المطلوبة؛ نسخة جديدة تُستعاد والقديمة تُرفض.</summary>
public sealed class Phase3BackupTests : IAsyncLifetime
{
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), $"wizz_phase3_backup_{Guid.NewGuid():N}");
    private readonly string _currentDb = Path.Combine(
        Path.GetTempPath(), $"wizz_phase3_current_{Guid.NewGuid():N}.db");
    private readonly string _legacyDb = Path.Combine(
        Path.GetTempPath(), $"wizz_phase3_legacy_{Guid.NewGuid():N}.db");

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_folder);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
            foreach (var path in new[] { _currentDb, _legacyDb })
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
                if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
            }
        }
        catch { }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task T24_CashMovementsIsRequired_NewBackupRestores_OldBackupRejected()
    {
        var currentOptions = new DbContextOptionsBuilder<WizzDbContext>()
            .UseSqlite($"Data Source={_currentDb}")
            .Options;
        await using (var db = new WizzDbContext(currentOptions))
        {
            await db.Database.EnsureCreatedAsync();
            db.Stores.Add(new Store("متجر النسخة", allowCreditSales: false, inventoryEnabled: true));
            await db.SaveChangesAsync();
        }

        var service = new BackupService(new WizzDbContext(currentOptions), _folder);
        var created = await service.CreateBackupAsync();
        Assert.True(created.Success, created.ErrorMessage);
        var restored = await service.RestoreAsync(created.BackupPath!);
        Assert.True(restored.Success, restored.ErrorMessage);

        var legacyOptions = new DbContextOptionsBuilder<WizzDbContext>()
            .UseSqlite($"Data Source={_legacyDb}")
            .Options;
        await using (var legacy = new WizzDbContext(legacyOptions))
            await legacy.GetService<IMigrator>().MigrateAsync("20261114123401_AddCashierShiftsAndSaleAudit");

        var legacyCopy = Path.Combine(_folder, "wizz_legacy_backup.db");
        File.Copy(_legacyDb, legacyCopy, true);

        var rejected = await service.RestoreAsync(legacyCopy);
        Assert.False(rejected.Success);
        Assert.Contains("جداول أساسية", rejected.ErrorMessage);
    }
}
