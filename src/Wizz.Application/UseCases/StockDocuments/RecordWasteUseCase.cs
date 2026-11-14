namespace Wizz.Application.UseCases.StockDocuments;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>تسجيل إتلاف/تالف بمستند موثّق (ITL) وسبب إلزامي.</summary>
public sealed class RecordWasteUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public RecordWasteUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<StockDocumentDto>> ExecuteAsync(
        Guid productId,
        Guid unitId,
        decimal quantity,
        string reason,
        Guid? createdByUserId,
        DateOnly? documentDate = null,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            return Result<StockDocumentDto>.Failure("كمية الإتلاف يجب أن تكون أكبر من صفر");

        if (string.IsNullOrWhiteSpace(reason))
            return Result<StockDocumentDto>.Failure("سبب الإتلاف مطلوب");

        var product = await _unitOfWork.Products.GetByIdAsync(productId, ct);
        if (product == null)
            return Result<StockDocumentDto>.Failure("منتج غير موجود");

        if (product.IsService)
            return Result<StockDocumentDto>.Failure("الخدمات لا تملك مخزوناً");

        var unit = product.GetUnitById(unitId);
        if (unit == null)
            return Result<StockDocumentDto>.Failure("وحدة القياس غير موجودة لهذا المنتج");

        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<StockDocumentDto>.Failure("لم يتم إعداد المتجر بعد");

        if (!store.InventoryEnabled)
            return Result<StockDocumentDto>.Failure("تتبع المخزون معطّل في المتجر — لا تُسجَّل مستندات مخزون");

        var baseQuantity = StockQuantity.ToBase(unit, quantity);
        if (baseQuantity <= 0)
            return Result<StockDocumentDto>.Failure("كمية الإتلاف بالوحدة الأساسية يجب أن تكون أكبر من صفر");

        var before = StockQuantity.Normalize(product.StockQuantity);
        var after = StockQuantity.Normalize(before - baseQuantity);
        var businessDate = documentDate ?? StockWorkflow.Today(store);
        var ownsTransaction = await StockWorkflow.BeginIfNeededAsync(_unitOfWork, ct);

        try
        {
            var number = await DocumentNumbering.AllocateAsync(
                _unitOfWork.Db, store.Id, StockDocumentType.Waste, businessDate, store.DocumentYearDigits, ct);

            var line = new StockDocumentLine(
                product.Id, unit.Id, quantity, -baseQuantity, before, after, reason);
            var document = new StockDocument(
                store.Id, StockDocumentType.Waste, number, businessDate, createdByUserId, reason,
                new[] { line });

            product.ApplyStockChange(-baseQuantity);

            await _unitOfWork.StockDocuments.AddAsync(document, ct);
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
