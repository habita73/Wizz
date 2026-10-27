namespace Wizz.Application.UseCases.Products;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>جرد/تعديل يدوي لرصيد المخزون مع تسجيل حركة وسبب.</summary>
public sealed class AdjustStockUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public AdjustStockUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<ProductDto>> ExecuteAsync(
        Guid productId,
        decimal newQuantity,
        string reason,
        Guid? changedByUserId,
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

        var before = product.StockQuantity;
        var delta = newQuantity - before;
        if (delta == 0)
            return Result<ProductDto>.Failure("الرصيد لم يتغيّر — لا حاجة لتسجيل حركة");

        product.SetStock(newQuantity, product.ReorderLevel);

        var movement = new StockMovement(
            product.Id,
            StockMovementType.ManualAdjustment,
            delta,
            before,
            reason,
            saleId: null,
            createdByUserId: changedByUserId);

        await _unitOfWork.StockMovements.AddAsync(movement, ct);
        _unitOfWork.Products.Update(product);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ProductDto>.Success(ProductDto.FromDomain(product));
    }
}
