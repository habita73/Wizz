namespace Wizz.Application.UseCases.Sales;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>تصحيح بيع: يُنشئ بيعًا مصححًا ويحفظ الأصل مع سبب التصحيح والفاعل.</summary>
public sealed class CorrectSaleUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public CorrectSaleUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<SaleDto>> ExecuteAsync(Guid saleId, string reason, Guid correctedByUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result<SaleDto>.Failure("سبب التصحيح مطلوب");
        }

        var sale = await _unitOfWork.Sales.GetByIdAsync(saleId, ct);
        if (sale is null)
        {
            return Result<SaleDto>.Failure("لم يتم العثور على البيع");
        }

        if (sale.IsCorrected)
        {
            return Result<SaleDto>.Failure("تم تصحيح هذا البيع مسبقًا");
        }

        if (sale.Status == SaleStatus.Cancelled)
        {
            return Result<SaleDto>.Failure("لا يمكن تصحيح بيع ملغي");
        }

        var correctedSale = sale.Correct(reason.Trim(), correctedByUserId);

        _unitOfWork.Sales.Update(sale);
        await _unitOfWork.Sales.AddAsync(correctedSale, ct);

        // تصحيح البيع = إرجاع كميات الأصل ثم خصم كميات البيع المصحح (صافي صفر عند تطابق الأسطر)،
        // مع تسجيل الحركتين ليبقى سجل التدقيق واضحاً.
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

                var beforeReturn = product.StockQuantity;
                product.ApplyStockChange(baseQuantity);

                await _unitOfWork.StockMovements.AddAsync(new StockMovement(
                    product.Id,
                    StockMovementType.SaleCorrection,
                    baseQuantity,
                    beforeReturn,
                    reason.Trim(),
                    sale.Id,
                    correctedByUserId), ct);

                var beforeDeduction = product.StockQuantity;
                product.ApplyStockChange(-baseQuantity);

                await _unitOfWork.StockMovements.AddAsync(new StockMovement(
                    product.Id,
                    StockMovementType.SaleCorrection,
                    -baseQuantity,
                    beforeDeduction,
                    reason.Trim(),
                    correctedSale.Id,
                    correctedByUserId), ct);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<SaleDto>.Success(MapToDto(correctedSale));
    }

    private static SaleDto MapToDto(Sale sale) => new(
        sale.Id,
        sale.SaleDate,
        (SaleStatusDto)sale.Status,
        sale.Items.Select(i => new SaleLineDto(
            i.Id, i.ProductId, i.Product?.Name ?? string.Empty, i.Product?.Barcode,
            i.UnitId, i.Unit?.Name ?? string.Empty, i.Unit?.Symbol ?? string.Empty,
            i.Quantity, i.UnitPrice, i.LineTotal, i.IsWholesalePrice)).ToList(),
        sale.Subtotal, sale.Discount, sale.Total, sale.Paid, sale.Remaining,
        (PaymentMethodDto)sale.PaymentMethod, sale.IsCreditSale, sale.DueDate,
        sale.CashierId, null, sale.IsCorrected, sale.OriginalSaleId, sale.CorrectionReason);
}
