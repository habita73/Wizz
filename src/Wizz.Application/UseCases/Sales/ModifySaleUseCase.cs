namespace Wizz.Application.UseCases.Sales;

using Microsoft.EntityFrameworkCore;
using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>تحرير كامل لعملية بيع موجودة: استبدال الأصناف وتحديث طرق الدفع والخصم والمبلغ المدفوع وتاريخ الاستحقاق وتعديل المخزون حسب الفرق.</summary>
public sealed class ModifySaleUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public ModifySaleUseCase(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<Result<SaleDto>> ExecuteAsync(
        Guid saleId,
        SaleInputDto input,
        CancellationToken ct = default)
    {
        if (input.Lines == null || input.Lines.Count == 0)
        {
            return Result<SaleDto>.Failure("يجب أن يتضمن البيع سطراً واحداً على الأقل");
        }

        var db = (Microsoft.EntityFrameworkCore.DbContext)_unitOfWork.Db;

        // تحميل البيع مع الأصناف والوحدات والمنتجات
        var sale = await db.Set<Sale>()
            .Include(s => s.Items)
                .ThenInclude(i => i.Product)
            .Include(s => s.Items)
                .ThenInclude(i => i.Unit)
            .FirstOrDefaultAsync(s => s.Id == saleId, ct);

        if (sale is null)
        {
            return Result<SaleDto>.Failure("لم يتم العثور على البيع");
        }

        if (sale.Status == SaleStatus.Cancelled)
        {
            return Result<SaleDto>.Failure("لا يمكن تعديل بيع ملغي");
        }

        // لقطة الأصناف القديمة قبل أي تعديل — تُحذف عبر EF داخل المعاملة نفسها.
        var oldItems = sale.Items.ToList();

        // بناء الأصناف الجديدة مع تحميل المنتجات والوحدات
        var newItems = new List<SaleItem>();

        foreach (var lineInput in input.Lines)
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

            var price = lineInput.UnitPrice ?? product.GetPrice(lineInput.UseWholesalePrice);
            var item = new SaleItem(lineInput.ProductId, lineInput.UnitId, lineInput.Quantity, price, lineInput.UseWholesalePrice);
            item.SetCatalog(product, unit);
            newItems.Add(item);
        }

        // تعديل البيع والأصناف والمخزون داخل معاملة EF واحدة — لا SQL خام ولا Commit منفصل.
        await _unitOfWork.BeginTransactionAsync(ct);

        try
        {
            sale.Edit(newItems, input.Discount, input.Paid, (PaymentMethod)input.PaymentMethod, input.IsCreditSale, input.DueDate);

            // حذف صريح للأصناف القديمة عبر EF لتفادي الاعتماد على فرق مجموعات التتبع.
            db.Set<SaleItem>().RemoveRange(oldItems);

            // إضافة الأصناف الجديدة صراحةً بحالة Added — المفتاح Guid معرّف مسبقًا،
            // وبدون ذلك يعتبرها EF موجودة ويولّد UPDATE يفشل بـ0 صفوف.
            db.Set<SaleItem>().AddRange(newItems);

            await _unitOfWork.SaveChangesAsync(ct);
            await _unitOfWork.CommitTransactionAsync(ct);
        }
        catch
        {
            await _unitOfWork.RollbackTransactionAsync(ct);
            throw;
        }

        return Result<SaleDto>.Success(MapToDto(sale));
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
        sale.CashierId, null);
}
