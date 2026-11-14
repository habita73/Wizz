namespace Wizz.Application.UseCases.StockDocuments;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Domain;

/// <summary>نتيجة ترحيل منتج واحد في تقرير الترحيل.</summary>
public sealed record StockMigrationProductDto(
    Guid ProductId,
    string ProductName,
    decimal StoredQuantity,
    decimal ComputedQuantity,
    decimal DocumentsDelta,
    decimal ActiveSalesBase,
    decimal SyntheticDelta,
    string Path
);

/// <summary>تقرير ترحيل حركات المخزون القديمة إلى مستندات.</summary>
public sealed record StockMigrationReport(
    int ProductsProcessed,
    int DocumentsCreated,
    int ReconstructedOpenings,
    int SyntheticOpenings,
    int SkippedMovements,
    decimal MaxDifference,
    IReadOnlyList<StockMigrationProductDto> Products
);

/// <summary>
/// ترحيل `StockMovements` القديمة إلى مستندات، ثم مطابقة كل رصيد مخزَّن مع المعادلة.
/// إعادة التشغيل آمنة: الحركات المُرحّلة تُتعرَّف بمعرّفها، والافتتاحي الاصطناعي يُحتسب من الفارق
/// فيصبح صفرًا عند التشغيل الثاني.
/// </summary>
public sealed class MigrateStockMovementsUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public MigrateStockMovementsUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<StockMigrationReport>> ExecuteAsync(CancellationToken ct = default)
    {
        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<StockMigrationReport>.Failure("لم يتم إعداد المتجر بعد");

        var ownsTransaction = await StockWorkflow.BeginIfNeededAsync(_unitOfWork, ct);

        try
        {
            var db = _unitOfWork.Db;

            var products = await db.Set<Product>()
                .Include(p => p.Units)
                .ToListAsync(ct);
            var productsById = products.ToDictionary(p => p.Id);

            var migratedMovementIds = (await db.Set<StockDocument>()
                    .AsNoTracking()
                    .Where(d => d.LegacyStockMovementId != null)
                    .Select(d => d.LegacyStockMovementId!.Value)
                    .ToListAsync(ct))
                .ToHashSet();

            var movements = await db.Set<StockMovement>()
                .AsNoTracking()
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .ToListAsync(ct);

            var documentsCreated = 0;
            var reconstructedOpenings = 0;
            var skippedMovements = 0;

            foreach (var movement in movements)
            {
                if (migratedMovementIds.Contains(movement.Id))
                {
                    skippedMovements++;
                    continue;
                }

                if (!productsById.TryGetValue(movement.ProductId, out var product) || product.IsService)
                {
                    skippedMovements++;
                    continue;
                }

                // المبيعات القديمة ليست مصدرًا ثانيًا: تُقرأ من Sale/SaleItems مباشرة.
                StockDocumentType? type = movement.Type switch
                {
                    StockMovementType.OpeningBalance => StockDocumentType.OpeningBalance,
                    StockMovementType.ManualAdjustment => StockDocumentType.Adjustment,
                    _ => null
                };

                var delta = StockQuantity.Normalize(movement.QuantityDelta);
                var baseUnit = product.GetBaseUnit();
                if (type == null || delta == 0 || baseUnit == null)
                {
                    skippedMovements++;
                    continue;
                }

                var businessDate = StockWorkflow.BusinessDate(store, movement.CreatedAt);
                var number = await DocumentNumbering.AllocateAsync(
                    db, store.Id, type.Value, businessDate, store.DocumentYearDigits, ct);

                var reason = string.IsNullOrWhiteSpace(movement.Reason)
                    ? "ترحيل حركة مخزون سابقة"
                    : movement.Reason.Trim();

                var line = new StockDocumentLine(
                    product.Id,
                    baseUnit.Id,
                    Math.Abs(delta),
                    delta,
                    StockQuantity.Normalize(movement.QuantityBefore),
                    StockQuantity.Normalize(movement.QuantityAfter),
                    movement.Reason);

                var document = new StockDocument(
                    store.Id,
                    type.Value,
                    number,
                    businessDate,
                    movement.CreatedByUserId,
                    reason,
                    new[] { line },
                    source: StockDocumentSource.Reconstructed,
                    createdAt: movement.CreatedAt,
                    legacyStockMovementId: movement.Id);

                await _unitOfWork.StockDocuments.AddAsync(document, ct);
                migratedMovementIds.Add(movement.Id);
                documentsCreated++;
                if (type.Value == StockDocumentType.OpeningBalance)
                    reconstructedOpenings++;
            }

            await _unitOfWork.SaveChangesAsync(ct);

            var documentLines = await db.Set<StockDocumentLine>()
                .AsNoTracking()
                .Include(l => l.StockDocument)
                .Where(l => l.StockDocument!.StoreId == store.Id && !l.StockDocument.IsVoided)
                .ToListAsync(ct);

            var documentsDeltaByProduct = documentLines
                .GroupBy(l => l.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.BaseQuantityDelta));

            var activeSales = await db.Set<Sale>()
                .AsNoTracking()
                .Include(s => s.Items)
                    .ThenInclude(i => i.Unit)
                .Where(s => s.StoreId == store.Id && s.Status != SaleStatus.Cancelled && s.InventoryApplied)
                .ToListAsync(ct);

            var activeSalesByProduct = activeSales
                .SelectMany(s => s.Items)
                .Where(i => i.Unit != null)
                .GroupBy(i => i.ProductId)
                .ToDictionary(g => g.Key, g => g.Sum(i => StockQuantity.ToBase(i.Unit!, i.Quantity)));

            var syntheticOpenings = 0;
            var today = StockWorkflow.Today(store);
            var results = new List<StockMigrationProductDto>();

            foreach (var product in products.Where(p => !p.IsService))
            {
                var documentsDelta = StockQuantity.Normalize(documentsDeltaByProduct.GetValueOrDefault(product.Id));
                var activeSalesBase = StockQuantity.Normalize(activeSalesByProduct.GetValueOrDefault(product.Id));
                var stored = StockQuantity.Normalize(product.StockQuantity);

                // معادلة الرصيد: المستندات + الاصطناعي − المبيعات النشطة = المخزَّن.
                var syntheticDelta = StockQuantity.Normalize(stored - documentsDelta + activeSalesBase);

                if (syntheticDelta != 0)
                {
                    var baseUnit = product.GetBaseUnit()
                        ?? throw new InvalidOperationException($"المنتج {product.Name} بلا وحدة أساسية");

                    var number = await DocumentNumbering.AllocateAsync(
                        db, store.Id, StockDocumentType.OpeningBalance, today, store.DocumentYearDigits, ct);

                    var reason = "افتتاحي اصطناعي بفارق الوصول إلى رصيد الترحيل";
                    var line = new StockDocumentLine(
                        product.Id, baseUnit.Id, Math.Abs(syntheticDelta), syntheticDelta, 0m, syntheticDelta, reason);
                    var document = new StockDocument(
                        store.Id, StockDocumentType.OpeningBalance, number, today, null, reason,
                        new[] { line }, source: StockDocumentSource.Synthetic);

                    await _unitOfWork.StockDocuments.AddAsync(document, ct);
                    syntheticOpenings++;
                    documentsCreated++;
                    documentsDelta = StockQuantity.Normalize(documentsDelta + syntheticDelta);
                }

                var computed = StockQuantity.Normalize(documentsDelta - activeSalesBase);
                var path = syntheticDelta != 0
                    ? "synthetic"
                    : (documentsDelta != 0 || activeSalesBase != 0 ? "reconstructed" : "unchanged");

                results.Add(new StockMigrationProductDto(
                    product.Id, product.Name, stored, computed, documentsDelta, activeSalesBase, syntheticDelta, path));
            }

            await _unitOfWork.SaveChangesAsync(ct);
            await StockWorkflow.CommitIfOwnedAsync(_unitOfWork, ownsTransaction, ct);

            var maxDifference = results.Count == 0
                ? 0m
                : results.Max(r => Math.Abs(r.ComputedQuantity - r.StoredQuantity));

            return Result<StockMigrationReport>.Success(new StockMigrationReport(
                results.Count,
                documentsCreated,
                reconstructedOpenings,
                syntheticOpenings,
                skippedMovements,
                maxDifference,
                results));
        }
        catch (Exception ex)
        {
            await StockWorkflow.RollbackIfOwnedAsync(_unitOfWork, ownsTransaction, ct);
            return Result<StockMigrationReport>.Failure($"فشل الترحيل: {ex.Message}");
        }
    }
}
