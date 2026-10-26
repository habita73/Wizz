namespace Wizz.Application.UseCases.Products;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>إنشاء منتج جديد.</summary>
public sealed class CreateProductUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public CreateProductUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<ProductDto>> ExecuteAsync(ProductInputDto input, CancellationToken ct = default)
    {
        // التحقق من الباركود
        if (!string.IsNullOrWhiteSpace(input.Barcode))
        {
            var exists = await _unitOfWork.Products.ExistsBarcodeAsync(input.Barcode, null, ct);
            if (exists)
                return Result<ProductDto>.Failure("باركود مستخدم بالفعل");
        }

        var product = new Product(input.Name, input.RetailPrice, input.WholesalePrice, input.IsService, input.Barcode, input.Description,
            input.StockQuantity, input.ReorderLevel);

        // إضافة الوحدات
        if (input.Units != null && input.Units.Count > 0)
        {
            foreach (var unitInput in input.Units)
            {
                product.AddUnit(unitInput.Name, unitInput.Symbol, unitInput.BaseQuantity);
            }
        }
        else
        {
            // وحدة افتراضية
            product.AddUnit("قطعة", "قطعة", 1m);
        }

        await _unitOfWork.Products.AddAsync(product, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<ProductDto>.Success(MapToDto(product));
    }

    private static ProductDto MapToDto(Product p) => new(
        p.Id, p.Name, p.Barcode, p.Description, p.RetailPrice, p.WholesalePrice,
        p.IsService, p.IsActive,
        p.Units.Select(u => new UnitOfMeasureDto(u.Id, u.Name, u.Symbol, u.BaseQuantity, u.IsBaseUnit)).ToList(),
        p.GetBaseUnit() is { } bu ? new UnitOfMeasureDto(bu.Id, bu.Name, bu.Symbol, bu.BaseQuantity, true) : null,
        p.StockQuantity, p.ReorderLevel);
}