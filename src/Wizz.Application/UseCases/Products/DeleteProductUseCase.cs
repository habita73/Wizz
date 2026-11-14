namespace Wizz.Application.UseCases.Products;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;

/// <summary>حذف منتج — ممنوع إن كان له أي مستند مخزون؛ التعطيل هو المسار الوحيد (R-10).</summary>
public sealed class DeleteProductUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public DeleteProductUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result> ExecuteAsync(Guid productId, CancellationToken ct = default)
    {
        var product = await _unitOfWork.Products.GetByIdAsync(productId, ct);
        if (product == null)
            return Result.Failure("منتج غير موجود");

        if (await _unitOfWork.StockDocuments.HasDocumentsForProductAsync(productId, ct))
            return Result.Failure("لا يمكن حذف منتج له مستندات مخزون — عطّله بدل حذفه للحفاظ على السجل");

        _unitOfWork.Products.Remove(product);
        await _unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
