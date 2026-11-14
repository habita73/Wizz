using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.Products;
using Wizz.Application.UseCases.Sales;
using Wizz.Application.UseCases.StockDocuments;
using Wizz.Domain;
using Wizz.Infrastructure.Data;
using Wizz.Infrastructure.Repositories;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات المرحلة 1 من نموذج المستندات والمخزون:
/// T-01 مطابقة الرصيد، T-05 التاريخ الرجعي، T-06 الترحيل، T-07 منع الحذف، T-08 تعطيل المخزون، T-10 الوحدات.
/// </summary>
public sealed class StockDocumentTests : IAsyncLifetime
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

    // T-01: الرصيد المخزَّن = معادلة المستندات والمبيعات النشطة بعد بيع وإلغاء وتعديل.
    [Fact]
    public async Task T01_StockCard_ReconcilesStock_AfterSaleCancelAndEdit()
    {
        var store = await SeedStoreAsync();
        var cashier = await SeedUserAsync();
        var product = await CreateProductAsync(stock: 100m, units: new[]
        {
            new UnitInputDto("كيلوغرام", "كجم", 1m),
            new UnitInputDto("صندوق", "صندوق", 10m),
        });

        var box = product.Units.Single(u => u.Symbol == "صندوق");

        var sale = await SellAsync(product.Id, box.Id, quantity: 2m, cashier.Id);
        Assert.True(sale.IsSuccess);
        await AssertReconciledAsync(product.Id, expectedStored: 80m);

        var modified = await new ModifySaleUseCase(_unitOfWork).ExecuteAsync(
            sale.Value.Id,
            new SaleInputDto(
                new[] { new SaleLineInputDto(product.Id, box.Id, 3m, false) },
                Discount: 0m,
                Paid: 300m,
                PaymentMethodDto.Cash,
                IsCreditSale: false,
                DueDate: null));
        Assert.True(modified.IsSuccess);
        await AssertReconciledAsync(product.Id, expectedStored: 70m);

        var cancelled = await new CancelSaleUseCase(_unitOfWork)
            .ExecuteAsync(sale.Value.Id, "خطأ في الكمية", cashier.Id);
        Assert.True(cancelled.IsSuccess);

        var card = await GetCardAsync(product.Id);
        Assert.True(card.IsReconciled);
        Assert.Equal(100m, card.StoredQuantity);
        Assert.Equal(100m, card.ComputedQuantity);
        // البيع الملغى لا يُدرج كخروج في بطاقة المخزون.
        Assert.DoesNotContain(card.Entries, e => e.Kind == "بيع");
    }

    // T-05: مستند بتاريخ رجعي يرتّب حسب DocumentDate، ويحتفظ CreatedAt بوقت الإدخال.
    [Fact]
    public async Task T05_BackdatedDocument_OrdersByDocumentDate_AndKeepsCreatedAt()
    {
        var store = await SeedStoreAsync();
        var cashier = await SeedUserAsync();
        var product = await CreateProductAsync(stock: 100m);
        var baseUnit = product.BaseUnit!;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var backdated = today.AddDays(-10);

        var retro = await new AdjustStockUseCase(_unitOfWork)
            .ExecuteAsync(product.Id, 110m, "جرد رجعي", cashier.Id, baseUnit.Id, backdated);
        Assert.True(retro.IsSuccess);

        var current = await new AdjustStockUseCase(_unitOfWork)
            .ExecuteAsync(product.Id, 120m, "جرد اليوم", cashier.Id, baseUnit.Id);
        Assert.True(current.IsSuccess);

        var card = await GetCardAsync(product.Id);
        Assert.True(card.IsReconciled);

        var retroEntry = card.Entries.Single(e => e.Reason == "جرد رجعي");
        Assert.Equal(backdated, retroEntry.DocumentDate);
        Assert.True(retroEntry.CreatedAt > DateTime.UtcNow.AddMinutes(-10));
        Assert.NotEqual(backdated, DateOnly.FromDateTime(retroEntry.CreatedAt));

        // الترتيب تصاعدي حسب التاريخ التجاري، فالمستند الرجعي أولًا.
        Assert.Equal(backdated, card.Entries[0].DocumentDate);
        Assert.Equal(card.Entries.Select(e => e.DocumentDate).OrderBy(d => d), card.Entries.Select(e => e.DocumentDate));
    }

    // T-06: ترحيل الحركات القديمة، وسم المصدر، وبناء افتتاحي اصطناعي عند الحاجة، وإعادة تشغيل دون تكرار.
    [Fact]
    public async Task T06_Migration_ReconstructsDocuments_AndIsIdempotent()
    {
        var store = await SeedStoreAsync();
        var withMovements = await SeedProductAsync(name: "منتج له حركات", barcode: "LEG-001", stock: 40m);
        var withoutMovements = await SeedProductAsync(name: "منتج بلا حركات", barcode: "LEG-002", stock: 25m);

        await _unitOfWork.StockMovements.AddAsync(new StockMovement(
            withMovements.Id, StockMovementType.OpeningBalance, 30m, 0m, "افتتاحي قديم"));
        await _unitOfWork.StockMovements.AddAsync(new StockMovement(
            withMovements.Id, StockMovementType.ManualAdjustment, 10m, 30m, "تعديل قديم"));
        await _unitOfWork.SaveChangesAsync();

        var first = await new MigrateStockMovementsUseCase(_unitOfWork).ExecuteAsync();

        Assert.True(first.IsSuccess);
        Assert.Equal(2, first.Value.ProductsProcessed);
        Assert.Equal(3, first.Value.DocumentsCreated);
        Assert.Equal(1, first.Value.ReconstructedOpenings);
        Assert.Equal(1, first.Value.SyntheticOpenings);
        Assert.Equal(0m, first.Value.MaxDifference);
        Assert.All(first.Value.Products, p => Assert.Equal(p.StoredQuantity, p.ComputedQuantity));

        var documents = await _db.StockDocuments.AsNoTracking().Include(d => d.Lines).ToListAsync();
        Assert.Equal(3, documents.Count);
        Assert.Equal(2, documents.Count(d => d.Source == StockDocumentSource.Reconstructed));
        Assert.Single(documents, d => d.Source == StockDocumentSource.Synthetic);
        Assert.All(
            documents.Where(d => d.Source == StockDocumentSource.Reconstructed),
            d => Assert.NotNull(d.LegacyStockMovementId));

        var synthetic = documents.Single(d => d.Source == StockDocumentSource.Synthetic);
        Assert.Equal(withoutMovements.Id, synthetic.Lines.Single().ProductId);

        var second = await new MigrateStockMovementsUseCase(_unitOfWork).ExecuteAsync();

        Assert.True(second.IsSuccess);
        Assert.Equal(0, second.Value.DocumentsCreated);
        Assert.Equal(0, second.Value.SyntheticOpenings);
        Assert.Equal(2, second.Value.SkippedMovements);
        Assert.Equal(0m, second.Value.MaxDifference);
        Assert.Equal(3, await _db.StockDocuments.CountAsync());
    }

    // T-07: منع حذف منتج له مستندات واقتراح التعطيل؛ منتج بلا مستندات يُحذف.
    [Fact]
    public async Task T07_DeleteProduct_WithDocuments_IsRejected()
    {
        await SeedStoreAsync();
        var withDocuments = await CreateProductAsync(stock: 5m);
        var withoutDocuments = await CreateProductAsync(stock: 0m, barcode: "NO-DOC-001");

        var useCase = new DeleteProductUseCase(_unitOfWork);

        var rejected = await useCase.ExecuteAsync(withDocuments.Id);
        Assert.True(rejected.IsFailure);
        Assert.Contains("عطّله", rejected.Errors.Single());
        Assert.NotNull(await _unitOfWork.Products.GetByIdAsync(withDocuments.Id));

        var deleted = await useCase.ExecuteAsync(withoutDocuments.Id);
        Assert.True(deleted.IsSuccess);
        Assert.Null(await _unitOfWork.Products.GetByIdAsync(withoutDocuments.Id));
    }

    // T-08: تعطيل المخزون لا ينشئ مستندات ولا يخصم، والإلغاء لا يعيد كمية غير مخصومة.
    [Fact]
    public async Task T08_InventoryDisabled_NoDocumentsOrDeduction_AndNoRestoreOnCancel()
    {
        await SeedStoreAsync(inventoryEnabled: false);
        var cashier = await SeedUserAsync();

        var product = await CreateProductAsync(stock: 40m);
        Assert.False(await _db.StockDocumentLines.AnyAsync(l => l.ProductId == product.Id));
        Assert.Equal(40m, await GetStockAsync(product.Id));

        var baseUnit = product.BaseUnit!;
        var sale = await SellAsync(product.Id, baseUnit.Id, quantity: 3m, cashier.Id);
        Assert.True(sale.IsSuccess);
        Assert.Equal(40m, await GetStockAsync(product.Id));
        Assert.False(await _db.Sales.AsNoTracking().Where(s => s.Id == sale.Value.Id).Select(s => s.InventoryApplied).SingleAsync());
        Assert.False(await _db.StockDocumentLines.AnyAsync(l => l.ProductId == product.Id));

        var cancelled = await new CancelSaleUseCase(_unitOfWork)
            .ExecuteAsync(sale.Value.Id, "إلغاء", cashier.Id);
        Assert.True(cancelled.IsSuccess);
        Assert.Equal(40m, await GetStockAsync(product.Id));

        var adjust = await new AdjustStockUseCase(_unitOfWork)
            .ExecuteAsync(product.Id, 50m, "جرد", cashier.Id, baseUnit.Id);
        Assert.True(adjust.IsFailure);
        Assert.False(await _db.StockDocumentLines.AnyAsync(l => l.ProductId == product.Id));
    }

    // T-10: التحويل للوحدة الأساسية دقيق وثابت في البيع والجرد والإتلاف.
    [Fact]
    public async Task T10_UnitConversion_IsPreciseAcrossDocumentTypes()
    {
        await SeedStoreAsync();
        var cashier = await SeedUserAsync();
        var product = await CreateProductAsync(stock: 0m, units: new[]
        {
            new UnitInputDto("كيلوغرام", "كجم", 1m),
            new UnitInputDto("صندوق", "صندوق", 12.5m),
            new UnitInputDto("عبوة", "عبوة", 3.3333m),
        });

        var box = product.Units.Single(u => u.Symbol == "صندوق");
        var pack = product.Units.Single(u => u.Symbol == "عبوة");

        var boxSale = await SellAsync(product.Id, box.Id, quantity: 1.5m, cashier.Id);
        Assert.True(boxSale.IsSuccess);
        var packSale = await SellAsync(product.Id, pack.Id, quantity: 3m, cashier.Id);
        Assert.True(packSale.IsSuccess);

        // 1.5 × 12.5 = 18.75، و3 × 3.3333 = 9.9999 (بأربع منازل، AwayFromZero).
        Assert.Equal(-28.7499m, await GetStockAsync(product.Id));

        var adjust = await new AdjustStockUseCase(_unitOfWork)
            .ExecuteAsync(product.Id, 2.4m, "جرد", cashier.Id, box.Id);
        Assert.True(adjust.IsSuccess);
        Assert.Equal(30m, await GetStockAsync(product.Id));

        var waste = await new RecordWasteUseCase(_unitOfWork)
            .ExecuteAsync(product.Id, box.Id, 0.3m, "تلف", cashier.Id);
        Assert.True(waste.IsSuccess);
        Assert.Equal(26.25m, await GetStockAsync(product.Id));

        var card = await GetCardAsync(product.Id);
        Assert.True(card.IsReconciled);
        Assert.Equal(26.25m, card.ComputedQuantity);

        var lines = await _db.StockDocumentLines.AsNoTracking()
            .Include(l => l.StockDocument)
            .Where(l => l.ProductId == product.Id)
            .ToListAsync();

        var count = Assert.Single(lines, l => l.StockDocument!.Type == StockDocumentType.StockCount);
        Assert.Equal(2.4m, count.Quantity);
        Assert.Equal(58.7499m, count.BaseQuantityDelta);

        var wasteLine = Assert.Single(lines, l => l.StockDocument!.Type == StockDocumentType.Waste);
        Assert.Equal(0.3m, wasteLine.Quantity);
        Assert.Equal(-3.75m, wasteLine.BaseQuantityDelta);

        // تحويل مباشر للوحدة الأساسية يطابق نفس قاعدة التقريب.
        var domainProduct = await _unitOfWork.Products.GetByIdAsync(product.Id);
        var domainPack = domainProduct!.Units.Single(u => u.Symbol == "عبوة");
        Assert.Equal(9.9999m, StockQuantity.ToBase(domainPack, 3m));
    }

    private async Task AssertReconciledAsync(Guid productId, decimal expectedStored)
    {
        var card = await GetCardAsync(productId);
        Assert.True(card.IsReconciled, $"فرق غير مطابق: {card.Difference}");
        Assert.Equal(expectedStored, card.StoredQuantity);
        Assert.Equal(expectedStored, card.ComputedQuantity);
    }

    private async Task<StockCardDto> GetCardAsync(Guid productId)
    {
        _db.ChangeTracker.Clear();
        var result = await new GetStockCardUseCase(_unitOfWork).ExecuteAsync(productId);
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task<Wizz.Application.Common.Result<SaleDto>> SellAsync(
        Guid productId, Guid unitId, decimal quantity, Guid cashierId)
    {
        var input = new SaleInputDto(
            new[] { new SaleLineInputDto(productId, unitId, quantity, false) },
            Discount: 0m,
            Paid: quantity * 100m,
            PaymentMethodDto.Cash,
            IsCreditSale: false,
            DueDate: null);

        return await new CreateSaleUseCase(_unitOfWork).ExecuteAsync(input, cashierId);
    }

    private async Task<ProductDto> CreateProductAsync(
        decimal stock = 0m, string barcode = "OIL-001", IReadOnlyList<UnitInputDto>? units = null)
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

    private async Task<Store> SeedStoreAsync(bool inventoryEnabled = true, bool allowCreditSales = true)
    {
        var store = new Store(
            "متجر الاختبار", "الجزائر العاصمة", "0550000000", "000000000000000", "00/00-0000000",
            allowCreditSales, inventoryEnabled: inventoryEnabled);

        await _unitOfWork.Store.AddAsync(store);
        await _unitOfWork.SaveChangesAsync();
        return store;
    }

    private async Task<Product> SeedProductAsync(decimal stock = 0m, string name = "منتج", string barcode = "LEG-000")
    {
        var product = new Product(name, 100m, 80m, false, barcode, null, stock);
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

/// <summary>T-04: الترقيم تحت التزامن — ملف SQLite واحد و50 معاملة متوازية.</summary>
public sealed class DocumentNumberingConcurrencyTests : IAsyncLifetime
{
    private const int Writers = 50;
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"wizz_numbering_{Guid.NewGuid():N}.db");
    private string _connectionString = null!;
    private DbContextOptions<WizzDbContext> _options = null!;
    private Guid _storeId;
    private Guid _productId;
    private Guid _unitId;

    public async Task InitializeAsync()
    {
        _connectionString = $"Data Source={_databasePath};Default Timeout=60";
        _options = new DbContextOptionsBuilder<WizzDbContext>()
            .UseSqlite(_connectionString)
            .Options;

        await using var db = new WizzDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var store = new Store(
            "متجر الترقيم", null, null, null, null, false, inventoryEnabled: true);
        var product = new Product("منتج الترقيم", 100m, 80m, false, "NUM-001", null, 0m);
        product.AddUnit("كيلوغرام", "كجم", 1m);

        db.Stores.Add(store);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        _storeId = store.Id;
        _productId = product.Id;
        _unitId = product.GetBaseUnit()!.Id;
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task T04_ParallelDocuments_GetUniqueSequentialNumbers()
    {
        var documentDate = new DateOnly(2026, 11, 14);

        var tasks = Enumerable.Range(0, Writers).Select(index => Task.Run(async () =>
        {
            await using var db = new WizzDbContext(_options);
            await using var transaction = await db.Database.BeginTransactionAsync();

            var number = await DocumentNumbering.AllocateAsync(
                db, _storeId, StockDocumentType.StockCount, documentDate, 4);

            var line = new StockDocumentLine(
                _productId, _unitId, 1m, 1m, 0m, 1m, $"تزامن {index}");
            var document = new StockDocument(
                _storeId, StockDocumentType.StockCount, number, documentDate, null,
                $"تزامن {index}", new[] { line });

            db.StockDocuments.Add(document);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return number;
        }));

        var numbers = await Task.WhenAll(tasks);

        Assert.Equal(Writers, numbers.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, Writers), numbers
            .Select(n => int.Parse(n.Split('-')[2], System.Globalization.CultureInfo.InvariantCulture))
            .OrderBy(n => n));

        await using var verify = new WizzDbContext(_options);
        var sequence = await verify.DocumentSequences.AsNoTracking()
            .SingleAsync(s => s.StoreId == _storeId && s.DocumentType == StockDocumentType.StockCount);
        Assert.Equal(Writers, sequence.LastNumber);

        var rangeEnd = DocumentNumbering.Format(
            StockDocumentType.StockCount, documentDate, Writers, 4);
        Assert.Contains(rangeEnd, numbers);
        Assert.Equal(Writers, await verify.StockDocuments.CountAsync());
    }
}
