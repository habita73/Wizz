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

        product.UpdateDetails(input.Name, input.RetailPrice, input.WholesalePrice, input.IsService, input.Barcode, input.Description);
        _unitOfWork.Products.Update(product);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ProductDto>.Success(MapToDto(product));
    }

    private static ProductDto MapToDto(Product p) => new(
        p.Id, p.Name, p.Barcode, p.Description, p.RetailPrice, p.WholesalePrice,
        p.IsService, p.IsActive,
        p.Units.Select(u => new UnitOfMeasureDto(u.Id, u.Name, u.Symbol, u.BaseQuantity, u.IsBaseUnit)).ToList(),
        p.GetBaseUnit() is { } bu ? new UnitOfMeasureDto(bu.Id, bu.Name, bu.Symbol, bu.BaseQuantity, true) : null);
}