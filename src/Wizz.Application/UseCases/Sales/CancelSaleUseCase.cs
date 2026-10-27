namespace Wizz.Application.UseCases.Sales;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>إلغاء عملية بيع.</summary>
public sealed class CancelSaleUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public CancelSaleUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<SaleDto>> ExecuteAsync(Guid saleId, string reason, Guid cancelledByUserId, CancellationToken ct = default)
    {
        var sale = await _unitOfWork.Sales.GetByIdAsync(saleId, ct);
        if (sale == null)
            return Result<SaleDto>.Failure("عملية البيع غير موجودة");

        if (sale.Status == SaleStatus.Cancelled)
            return Result<SaleDto>.Failure("البيع ملغي بالفعل");

        if (sale.IsCorrected)
            return Result<SaleDto>.Failure("لا يمكن إلغاء بيع مصحح — ألغِ التصحيح أولاً");

        sale.Cancel(reason);
        _unitOfWork.Sales.Update(sale);

        // إرجاع الكميات إلى المخزون عند إلغاء البيع (إن كان تتبع المخزون مفعلاً).
        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store?.InventoryEnabled == true)
        {
            foreach (var item in sale.Items)
            {
                var product = item.Product;
                var unit = item.Unit;
                if (product == null || unit == null || product.IsService) continue;

                var baseQuantity = unit.ToBaseQuantity(item.Quantity);
                if (baseQuantity == 0) continue;

                var before = product.StockQuantity;
                product.ApplyStockChange(baseQuantity);

                await _unitOfWork.StockMovements.AddAsync(new StockMovement(
                    product.Id,
                    StockMovementType.SaleCancellation,
                    baseQuantity,
                    before,
                    reason,
                    sale.Id,
                    cancelledByUserId), ct);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);

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
        sale.CashierId, null, sale.IsCorrected, sale.OriginalSaleId, sale.CorrectionReason);
}