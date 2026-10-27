namespace Wizz.Application.UseCases.Products;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>تحديث منتج.</summary>
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

        var stockBefore = product.StockQuantity;

        product.UpdateDetails(input.Name, input.RetailPrice, input.WholesalePrice, product.IsActive, input.IsService, input.Barcode, input.Description,
            input.ReorderLevel);

        // تحويل منتج إلى خدمة يُصفّر الرصيد — نسجّل الحركة حتى لا يختفي الرصيد بلا أثر.
        if (input.IsService && stockBefore != 0)
        {
            await _unitOfWork.StockMovements.AddAsync(new StockMovement(
                product.Id,
                StockMovementType.ManualAdjustment,
                -stockBefore,
                stockBefore,
                "تحويل المنتج إلى خدمة (إلغاء تتبع المخزون)",
                saleId: null,
                createdByUserId: null), ct);
        }

        _unitOfWork.Products.Update(product);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ProductDto>.Success(ProductDto.FromDomain(product));
    }
}