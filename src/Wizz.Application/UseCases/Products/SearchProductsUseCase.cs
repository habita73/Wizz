namespace Wizz.Application.UseCases.Products;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>البحث عن منتجات.</summary>
public sealed class SearchProductsUseCase
{
    private readonly IProductRepository _products;

    public SearchProductsUseCase(IProductRepository products) => _products = products;

    public async Task<Result<ProductSearchResultDto>> ExecuteAsync(string? query = null, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 200) pageSize = 50;

        var products = await _products.SearchAsync(query, pageSize, ct);
        var totalCount = await _products.CountAsync(ct);

        return Result<ProductSearchResultDto>.Success(new ProductSearchResultDto(
            products.Select(MapToDto).ToList(), totalCount, page, pageSize));
    }

    private static ProductDto MapToDto(Product p) => new(
        p.Id, p.Name, p.Barcode, p.Description, p.RetailPrice, p.WholesalePrice,
        p.IsService, p.IsActive,
        p.Units.Select(u => new UnitOfMeasureDto(u.Id, u.Name, u.Symbol, u.BaseQuantity, u.IsBaseUnit)).ToList(),
        p.GetBaseUnit() is { } bu ? new UnitOfMeasureDto(bu.Id, bu.Name, bu.Symbol, bu.BaseQuantity, true) : null);
}