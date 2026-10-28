namespace Wizz.Application.UseCases.Sales;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>تعديل بيع مباشر على العملية نفسها مع تعديل المخزون حسب الفرق وإضافة ملاحظة التعديل.</summary>
public sealed class ModifySaleUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public ModifySaleUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<SaleDto>> ExecuteAsync(
        Guid saleId,
        IReadOnlyList<SaleLineInputDto> newLines,
        string modificationNote,
        Guid modifiedByUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(modificationNote))
        {
            return Result<SaleDto>.Failure("ملاحظة التعديل مطلوبة");
        }

        if (newLines == null || newLines.Count == 0)
        {
            return Result<SaleDto>.Failure("يجب أن يتضمن البيع سطراً واحداً على الأقل");
        }

        // تحميل البيع بدون تتبع الكيانات (لتجنب تعارض Change Tracker)
        var sale = await _unitOfWork.Sales.GetByIdAsyncNoTracking(saleId, ct);
        if (sale is null)
        {
            return Result<SaleDto>.Failure("لم يتم العثور على البيع");
        }

        if (sale.Status == SaleStatus.Cancelled)
        {
            return Result<SaleDto>.Failure("لا يمكن تعديل بيع ملغي");
        }

        // حساب الكميات القديمة بالوحدة الأساسية لكل منتج
        var oldQuantities = sale.Items
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Unit!.ToBaseQuantity(i.Quantity)));

        // بناء الأصناف الجديدة مع تحميل المنتجات (مع التتبع لرصد تغييرات المخزون)
        var newItems = new List<SaleItem>();
        var unitBaseQuantities = new Dictionary<(Guid ProductId, Guid UnitId), decimal>();

        foreach (var lineInput in newLines)
        {
            var product = await _unitOfWork.Products.GetByIdAsync(lineInput.ProductId, ct);
            if (product == null)
            {
                return Result<SaleDto>.Failure($"منتج غير موجود: {lineInput.ProductId}");
            }

            if (!product.IsActive)
            {
                return Result<SaleDto>.Failure($"منتج غير نشط: {product.Name}");
            }

            var unit = product.GetUnitById(lineInput.UnitId);
            if (unit == null)
            {
                return Result<SaleDto>.Failure($"وحدة غير موجودة للمنتج: {product.Name}");
            }

            unitBaseQuantities[(lineInput.ProductId, lineInput.UnitId)] = unit.BaseQuantity;

            var price = product.GetPrice(lineInput.UseWholesalePrice);
            var item = new SaleItem(lineInput.ProductId, lineInput.UnitId, lineInput.Quantity, price, lineInput.UseWholesalePrice);
            newItems.Add(item);
        }

        // حساب الكميات الجديدة بالوحدة الأساسية لكل منتج
        var newQuantities = newItems
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.Sum(i =>
            {
                var baseQty = unitBaseQuantities[(i.ProductId, i.UnitId)];
                return baseQty * i.Quantity;
            }));

        // تطبيق فرق المخزون لكل منتج (جديد - قديم)
        foreach (var productId in oldQuantities.Keys.Union(newQuantities.Keys))
        {
            var oldQty = oldQuantities.TryGetValue(productId, out var o) ? o : 0m;
            var newQty = newQuantities.TryGetValue(productId, out var n) ? n : 0m;
            var delta = newQty - oldQty;

            if (delta != 0)
            {
                var product = await _unitOfWork.Products.GetByIdAsync(productId, ct);
                if (product != null && !product.IsService)
                {
                    product.ApplyStockChange(-delta);
                }
            }
        }

        // استبدال الأصناف في البيع (سيُعيد حساب الإجماليات)
        sale.ReplaceItems(newItems);

        // إذا تجاوز المبلغ المدفوع الإجمالي الجديد، نضبطه على الإجمالي الجديد
        if (sale.Paid > sale.Total)
        {
            sale.AdjustPaid(sale.Total);
        }

        // تحديد علامات التعديل
        sale.MarkModified(modificationNote.Trim(), modifiedByUserId);

        // ربط البيع كمعدل مع EF Core
        _unitOfWork.Sales.Update(sale);

        await _unitOfWork.SaveChangesAsync(ct);

        // إعادة تحميل البيع مع الأصناف لـ DTO (لأن الأصناف الجديدة لم تُحفظ بعد مع منتجاتها)
        var savedSale = await _unitOfWork.Sales.GetByIdAsync(saleId, ct);

        return Result<SaleDto>.Success(MapToDto(savedSale));
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
        sale.CashierId, null, sale.IsModified, sale.ModifiedAt, sale.ModifiedByUserId, sale.ModificationNote);
}
