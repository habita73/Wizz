namespace Wizz.Application.UseCases.StockDocuments;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.Sales;
using Wizz.Domain;

/// <summary>
/// مرتجع بيع (MRB) — مستند تصحيح بعد انتهاء نافذة تعديل الوردية.
/// مرتبط إلزاميًا بالبيع الأصلي، يزيد المخزون بالوحدة الأساسية، ولا يتجاوز المتبقي
/// بعد خصم المرتجعات السابقة. مخزوني فقط في هذه المرحلة: لا ردّ نقدي.
/// </summary>
public sealed class CreateSalesReturnUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateSalesReturnUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<StockDocumentDto>> ExecuteAsync(
        Guid saleId,
        IReadOnlyList<SalesReturnLineInputDto> lines,
        string reason,
        Guid? createdByUserId,
        CancellationToken ct = default)
    {
        if (lines == null || lines.Count == 0)
            return Result<StockDocumentDto>.Failure("يجب أن يتضمن المرتجع سطرًا واحدًا على الأقل");

        if (string.IsNullOrWhiteSpace(reason))
            return Result<StockDocumentDto>.Failure("سبب المرتجع مطلوب");

        if (lines.Any(l => l.Quantity <= 0))
            return Result<StockDocumentDto>.Failure("كمية المرتجع يجب أن تكون أكبر من صفر");

        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<StockDocumentDto>.Failure("لم يتم إعداد المتجر بعد");

        if (!store.InventoryEnabled)
            return Result<StockDocumentDto>.Failure("تتبع المخزون معطّل — لا يُسجَّل مرتجع مخزوني");

        var sale = await _unitOfWork.Db.Set<Sale>()
            .AsNoTracking()
            .Include(s => s.Items)
                .ThenInclude(i => i.Unit)
            .FirstOrDefaultAsync(s => s.Id == saleId, ct);

        if (sale == null)
            return Result<StockDocumentDto>.Failure("عملية البيع غير موجودة");

        if (sale.Status == SaleStatus.Cancelled)
            return Result<StockDocumentDto>.Failure("لا يمكن إنشاء مرتجع لبيع ملغي");

        if (!sale.InventoryApplied)
            return Result<StockDocumentDto>.Failure("لم تُخصم كمية هذا البيع من المخزون — لا مرتجع مخزوني");

        if (await ShiftGate.IsWithinEditWindowAsync(_unitOfWork, sale, store, ct))
            return Result<StockDocumentDto>.Failure(
                "البيع داخل نافذة التعديل — استخدم تعديل البيع أو إلغاءه بدل المرتجع");

        var soldByProduct = sale.Items
            .Where(i => i.Unit != null)
            .GroupBy(i => i.ProductId)
            .ToDictionary(
                g => g.Key,
                g => StockQuantity.Normalize(g.Sum(i => StockQuantity.ToBase(i.Unit!, i.Quantity))));

        var priorReturns = await (
                from line in _unitOfWork.Db.Set<StockDocumentLine>()
                join document in _unitOfWork.Db.Set<StockDocument>() on line.StockDocumentId equals document.Id
                where document.ReferenceSaleId == saleId
                      && document.Type == StockDocumentType.SalesReturn
                      && !document.IsVoided
                group line by line.ProductId into g
                select new { ProductId = g.Key, Quantity = g.Sum(x => x.BaseQuantityDelta) })
            .ToListAsync(ct);

        var remaining = new Dictionary<Guid, decimal>(soldByProduct);
        foreach (var prior in priorReturns)
        {
            remaining.TryGetValue(prior.ProductId, out var current);
            remaining[prior.ProductId] = StockQuantity.Normalize(current - prior.Quantity);
        }

        var prepared = new List<(Product Product, UnitOfMeasure Unit, decimal Quantity, decimal BaseQuantity)>();

        foreach (var line in lines)
        {
            if (!soldByProduct.ContainsKey(line.ProductId))
                return Result<StockDocumentDto>.Failure("المنتج ليس ضمن البيع الأصلي");

            var product = await _unitOfWork.Products.GetByIdAsync(line.ProductId, ct);
            if (product == null)
                return Result<StockDocumentDto>.Failure($"منتج غير موجود: {line.ProductId}");

            if (product.IsService)
                return Result<StockDocumentDto>.Failure("الخدمات لا تملك مخزونًا — لا مرتجع لها");

            var unit = product.GetUnitById(line.UnitId);
            if (unit == null)
                return Result<StockDocumentDto>.Failure($"وحدة غير موجودة للمنتج: {product.Name}");

            var baseQuantity = StockQuantity.ToBase(unit, line.Quantity);
            if (baseQuantity <= 0)
                return Result<StockDocumentDto>.Failure("كمية المرتجع بالوحدة الأساسية يجب أن تكون أكبر من صفر");

            remaining.TryGetValue(line.ProductId, out var available);
            if (baseQuantity > available)
                return Result<StockDocumentDto>.Failure(
                    $"كمية المرتجع تتجاوز المتبقي للبيع من المنتج: {product.Name}");

            remaining[line.ProductId] = StockQuantity.Normalize(available - baseQuantity);
            prepared.Add((product, unit, line.Quantity, baseQuantity));
        }

        var ownsTransaction = await StockWorkflow.BeginIfNeededAsync(_unitOfWork, ct);

        try
        {
            var businessDate = StockWorkflow.Today(store);
            var number = await DocumentNumbering.AllocateAsync(
                _unitOfWork.Db, store.Id, StockDocumentType.SalesReturn, businessDate,
                store.DocumentYearDigits, ct);

            var documentLines = new List<StockDocumentLine>();
            foreach (var entry in prepared)
            {
                var before = StockQuantity.Normalize(entry.Product.StockQuantity);
                var after = StockQuantity.Normalize(before + entry.BaseQuantity);

                documentLines.Add(new StockDocumentLine(
                    entry.Product.Id, entry.Unit.Id, entry.Quantity, entry.BaseQuantity,
                    before, after, reason));

                entry.Product.ApplyStockChange(entry.BaseQuantity);
            }

            var document = new StockDocument(
                store.Id, StockDocumentType.SalesReturn, number, businessDate, createdByUserId, reason,
                documentLines, referenceSaleId: saleId);

            await _unitOfWork.StockDocuments.AddAsync(document, ct);

            foreach (var product in prepared.Select(p => p.Product).DistinctBy(p => p.Id))
                _unitOfWork.Products.Update(product);

            await _unitOfWork.SaveChangesAsync(ct);
            await StockWorkflow.CommitIfOwnedAsync(_unitOfWork, ownsTransaction, ct);

            return Result<StockDocumentDto>.Success(StockDocumentDto.FromDomain(document));
        }
        catch
        {
            await StockWorkflow.RollbackIfOwnedAsync(_unitOfWork, ownsTransaction, ct);
            throw;
        }
    }
}
