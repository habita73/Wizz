namespace Wizz.Application.UseCases.Sales;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>الحصول على عملية بيع بالمعرف.</summary>
public sealed class GetSaleUseCase
{
    private readonly ISaleRepository _sales;

    public GetSaleUseCase(ISaleRepository sales) => _sales = sales;

    public async Task<Result<SaleDto>> ExecuteAsync(Guid saleId, CancellationToken ct = default)
    {
        var sale = await _sales.GetByIdAsync(saleId, ct);
        if (sale == null)
            return Result<SaleDto>.Failure("عملية البيع غير موجودة");

        return Result<SaleDto>.Success(MapToDto(sale));
    }

    private static SaleDto MapToDto(Sale sale) => new(
        sale.Id, sale.SaleDate, (SaleStatusDto)sale.Status,
        sale.Items.Select(i => new SaleLineDto(
            i.Id, i.ProductId, i.Product?.Name ?? "", i.Product?.Barcode,
            i.UnitId, i.Unit?.Name ?? "", i.Unit?.Symbol ?? "",
            i.Quantity, i.UnitPrice, i.LineTotal, i.IsWholesalePrice)).ToList(),
        sale.Subtotal, sale.Discount, sale.Total, sale.Paid, sale.Remaining,
        (PaymentMethodDto)sale.PaymentMethod, sale.IsCreditSale, sale.DueDate,
        sale.CashierId, null, sale.IsModified, sale.ModifiedAt, sale.ModifiedByUserId, sale.ModificationNote);
}