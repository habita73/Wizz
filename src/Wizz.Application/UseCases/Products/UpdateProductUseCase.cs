namespace Wizz.Application.UseCases.Products;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.StockDocuments;
using Wizz.Domain;

/// <summary>تحديث منتج، مع تسوية موثقة (TSW) عند تحويل منتج له رصيد إلى خدمة.</summary>
public sealed class UpdateProductUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public UpdateProductUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<ProductDto>> ExecuteAsync(Guid productId, ProductInputDto input, CancellationToken ct = default)
    {
        var product = await _unitOfWork.Products.GetByIdAsync(productId, ct);
        if (product == null)
            return Result<ProductDto>.Failure("منتج غير موجود");

        // التحقق من الباركود
        if (!string.IsNullOrWhiteSpace(input.Barcode) && input.Barcode != product.Barcode)
        {
            var exists = await _unitOfWork.Products.ExistsBarcodeAsync(input.Barcode, productId, ct);
            if (exists)
                return Result<ProductDto>.Failure("باركود مستخدم بالفعل");
        }

        var stockBefore = StockQuantity.Normalize(product.StockQuantity);
        var conversionToService = input.IsService && stockBefore != 0;
        var baseUnit = product.GetBaseUnit();

        Store? store = null;
        if (conversionToService)
        {
            if (baseUnit == null)
                return Result<ProductDto>.Failure("المنتج بلا وحدة أساسية");

            store = await _unitOfWork.Store.GetAsync(ct);
            if (store == null)
                return Result<ProductDto>.Failure("لم يتم إعداد المتجر بعد — لا يمكن تسوية الرصيد");
        }

        var ownsTransaction = await StockWorkflow.BeginIfNeededAsync(_unitOfWork, ct);

        try
        {
            product.UpdateDetails(input.Name, input.RetailPrice, input.WholesalePrice, product.IsActive, input.IsService,
                input.Barcode, input.Description, input.ReorderLevel);

            // تحويل منتج إلى خدمة يُصفّر الرصيد — نسجّل مستند تسوية حتى لا يختفي الرصيد بلا أثر
            // عند تفعيل تتبع المخزون فقط؛ مع تعطيله لا تُنشأ مستندات.
            if (conversionToService && store != null && baseUnit != null && store.InventoryEnabled)
            {
                var businessDate = StockWorkflow.Today(store);
                var number = await DocumentNumbering.AllocateAsync(
                    _unitOfWork.Db, store.Id, StockDocumentType.Adjustment, businessDate,
                    store.DocumentYearDigits, ct);

                var reason = "تحويل المنتج إلى خدمة (إلغاء تتبع المخزون)";
                var line = new StockDocumentLine(
                    product.Id, baseUnit.Id, Math.Abs(stockBefore), -stockBefore, stockBefore, 0m, reason);
                var document = new StockDocument(
                    store.Id, StockDocumentType.Adjustment, number, businessDate, null, reason, new[] { line });

                await _unitOfWork.StockDocuments.AddAsync(document, ct);
            }

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
