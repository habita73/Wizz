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

        // إذا كان البحث فارغاً، نستخدم GetAllAsync مع الترقيم اليدوي
        IReadOnlyList<Product> products;
        int totalCount;

        if (string.IsNullOrWhiteSpace(query))
        {
            var all = await _products.GetAllAsync(ct);
            totalCount = all.Count;
            products = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }
        else
        {
            products = await _products.SearchAsync(query, pageSize, ct);
            totalCount = products.Count; // SearchAsync يعيد محدوداً بالحد المحدد
        }

        return Result<ProductSearchResultDto>.Success(new ProductSearchResultDto(
            products.Select(MapToDto).ToList(), totalCount, page, pageSize));
    }

    private static ProductDto MapToDto(Product p) => new(
        p.Id, p.Name, p.Barcode, p.Description, p.RetailPrice, p.WholesalePrice,
        p.IsService, p.IsActive,
        p.Units.Select(u => new UnitOfMeasureDto(u.Id, u.Name, u.Symbol, u.BaseQuantity, u.IsBaseUnit)).ToList(),
        p.GetBaseUnit() is { } bu ? new UnitOfMeasureDto(bu.Id, bu.Name, bu.Symbol, bu.BaseQuantity, true) : null);
}