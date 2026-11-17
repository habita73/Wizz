namespace Wizz.Application.UseCases.StockDocuments;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>
/// إلغاء مستند شراء SHR: يعكس الكميات، ويحتفظ بالرقم، ولا يحذف أي سطر.
/// لا يُسمح بإلغاء مستند ملغى، والسبب إلزامي.
/// </summary>
public sealed class VoidPurchaseUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public VoidPurchaseUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<StockDocumentDto>> ExecuteAsync(
        Guid documentId,
        string reason,
        Guid? voidedByUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result<StockDocumentDto>.Failure("سبب إلغاء الشراء مطلوب");

        var document = await _unitOfWork.StockDocuments.GetByIdAsync(documentId, ct);
        if (document == null)
            return Result<StockDocumentDto>.Failure("مستند الشراء غير موجود");
        if (document.Type != StockDocumentType.Purchase)
            return Result<StockDocumentDto>.Failure("المستند ليس شراءً");
        if (document.IsVoided)
            return Result<StockDocumentDto>.Failure("مستند الشراء ملغى بالفعل");

        var products = new Dictionary<Guid, Product>();
        foreach (var line in document.Lines)
        {
            if (products.ContainsKey(line.ProductId))
                continue;
            var product = await _unitOfWork.Products.GetByIdAsync(line.ProductId, ct);
            if (product == null)
                return Result<StockDocumentDto>.Failure($"منتج السطر غير موجود: {line.ProductId}");
            products[line.ProductId] = product;
        }

        var ownsTransaction = await StockWorkflow.BeginIfNeededAsync(_unitOfWork, ct);

        try
        {
            foreach (var line in document.Lines)
            {
                var product = products[line.ProductId];
                product.ApplyStockChange(-line.BaseQuantityDelta);
            }

            document.Void(reason, voidedByUserId);

            foreach (var product in products.Values)
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
