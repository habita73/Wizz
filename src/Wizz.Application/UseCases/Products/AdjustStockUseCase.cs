namespace Wizz.Application.UseCases.Products;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.StockDocuments;
using Wizz.Domain;

/// <summary>جرد/تعديل يدوي لرصيد المخزون عبر مستند جرد موثّق (JRD).</summary>
public sealed class AdjustStockUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public AdjustStockUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<ProductDto>> ExecuteAsync(
        Guid productId,
        decimal newQuantity,
        string reason,
        Guid? changedByUserId,
        Guid? unitId = null,
        DateOnly? documentDate = null,
        CancellationToken ct = default)
    {
        if (newQuantity < 0)
            return Result<ProductDto>.Failure("الكمية الجديدة لا يمكن أن تكون سالبة");

        if (string.IsNullOrWhiteSpace(reason))
            return Result<ProductDto>.Failure("سبب تعديل المخزون مطلوب");

        var product = await _unitOfWork.Products.GetByIdAsync(productId, ct);
        if (product == null)
            return Result<ProductDto>.Failure("منتج غير موجود");

        if (product.IsService)
            return Result<ProductDto>.Failure("الخدمات لا تملك مخزوناً");

        var unit = unitId.HasValue ? product.GetUnitById(unitId.Value) : product.GetBaseUnit();
        if (unit == null)
            return Result<ProductDto>.Failure("وحدة القياس غير موجودة لهذا المنتج");

        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<ProductDto>.Failure("لم يتم إعداد المتجر بعد");

        if (!store.InventoryEnabled)
            return Result<ProductDto>.Failure("تتبع المخزون معطّل في المتجر — لا تُسجَّل مستندات مخزون");

        var before = StockQuantity.Normalize(product.StockQuantity);
        var newBaseQuantity = StockQuantity.ToBase(unit, newQuantity);
        var delta = StockQuantity.Normalize(newBaseQuantity - before);
        if (delta == 0)
            return Result<ProductDto>.Failure("الرصيد لم يتغيّر — لا حاجة لتسجيل حركة");

        var businessDate = documentDate ?? StockWorkflow.Today(store);
        var ownsTransaction = await StockWorkflow.BeginIfNeededAsync(_unitOfWork, ct);

        try
        {
            var number = await DocumentNumbering.AllocateAsync(
                _unitOfWork.Db, store.Id, StockDocumentType.StockCount, businessDate, store.DocumentYearDigits, ct);

            product.SetStock(newBaseQuantity, product.ReorderLevel);

            var line = new StockDocumentLine(
                product.Id, unit.Id, newQuantity, delta, before, newBaseQuantity, reason);
            var document = new StockDocument(
                store.Id, StockDocumentType.StockCount, number, businessDate, changedByUserId, reason,
                new[] { line });

            await _unitOfWork.StockDocuments.AddAsync(document, ct);
            _unitOfWork.Products.Update(product);
            await _unitOfWork.SaveChangesAsync(ct);
            await StockWorkflow.CommitIfOwnedAsync(_unitOfWork, ownsTransaction, ct);
        }
        catch
        {
            await StockWorkflow.RollbackIfOwnedAsync(_unitOfWork, ownsTransaction, ct);
            throw;
        }

        return Result<ProductDto>.Success(ProductDto.FromDomain(product));
    }
}
