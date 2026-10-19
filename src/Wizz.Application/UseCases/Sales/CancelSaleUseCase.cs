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