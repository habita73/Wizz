namespace Wizz.Application.UseCases.Sales;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Application.UseCases.StockDocuments;
using Wizz.Domain;

/// <summary>إلغاء عملية بيع — يستعيد المخزون على العملية نفسها.</summary>
public sealed class CancelSaleUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public CancelSaleUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<SaleDto>> ExecuteAsync(Guid saleId, string reason, Guid cancelledByUserId, CancellationToken ct = default)
    {
        var sale = await _unitOfWork.Sales.GetByIdAsync(saleId, ct);
        if (sale == null)
        {
            return Result<SaleDto>.Failure("عملية البيع غير موجودة");
        }

        if (sale.Status == SaleStatus.Cancelled)
        {
            return Result<SaleDto>.Failure("البيع ملغي بالفعل");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result<SaleDto>.Failure("سبب الإلغاء مطلوب");
        }

        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
        {
            return Result<SaleDto>.Failure("لم يتم إعداد المتجر بعد");
        }

        var gateReason = await ShiftGate.RejectReasonAsync(_unitOfWork, sale, store, ct);
        if (gateReason != null)
        {
            return Result<SaleDto>.Failure(gateReason);
        }

        var ownsTransaction = await StockWorkflow.BeginIfNeededAsync(_unitOfWork, ct);

        try
        {
            sale.Cancel(reason.Trim(), cancelledByUserId);
            _unitOfWork.Sales.Update(sale);

            await _unitOfWork.SaveChangesAsync(ct);
            await StockWorkflow.CommitIfOwnedAsync(_unitOfWork, ownsTransaction, ct);
        }
        catch
        {
            await StockWorkflow.RollbackIfOwnedAsync(_unitOfWork, ownsTransaction, ct);
            throw;
        }

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
        sale.CashierId, null);
}
