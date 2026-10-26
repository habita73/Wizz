namespace Wizz.Application.UseCases.Products;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>الحصول على منتج بالمعرف أو الباركود.</summary>
public sealed class GetProductUseCase
{
    private readonly IProductRepository _products;

    public GetProductUseCase(IProductRepository products) => _products = products;

    public async Task<Result<ProductDto>> ExecuteAsync(Guid? id = null, string? barcode = null, CancellationToken ct = default)
    {
        Product? product = null;
        if (id.HasValue)
            product = await _products.GetByIdAsync(id.Value, ct);
        else if (!string.IsNullOrWhiteSpace(barcode))
            product = await _products.GetByBarcodeAsync(barcode, ct);

        if (product == null)
            return Result<ProductDto>.Failure("منتج غير موجود");

        return Result<ProductDto>.Success(MapToDto(product));
    }

    private static ProductDto MapToDto(Product p) => new(
        p.Id, p.Name, p.Barcode, p.Description, p.RetailPrice, p.WholesalePrice,
        p.IsService, p.IsActive,
        p.Units.Select(u => new UnitOfMeasureDto(u.Id, u.Name, u.Symbol, u.BaseQuantity, u.IsBaseUnit)).ToList(),
        p.GetBaseUnit() is { } bu ? new UnitOfMeasureDto(bu.Id, bu.Name, bu.Symbol, bu.BaseQuantity, true) : null,
        p.StockQuantity, p.ReorderLevel);
}