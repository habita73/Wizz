namespace Wizz.Application.UseCases.Sales;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>إنشاء عملية بيع جديدة.</summary>
public sealed class CreateSaleUseCase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductRepository _products;

    public CreateSaleUseCase(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
        _products = unitOfWork.Products;
    }

    public async Task<Result<SaleDto>> ExecuteAsync(SaleInputDto input, Guid cashierId, CancellationToken ct = default)
    {
        var errors = new List<string>();

        if (input.Lines == null || input.Lines.Count == 0)
            return Result<SaleDto>.Failure("يجب أن يتضمن البيع سطراً واحداً على الأقل");

        if (input.Discount < 0)
            return Result<SaleDto>.Failure("الخصم لا يمكن أن يكون سالباً");

        if (input.Paid < 0)
            return Result<SaleDto>.Failure("المبلغ المدفوع لا يمكن أن يكون سالباً");

        // التحقق من المنتجات وبناء الأسطر
        var items = new List<SaleItem>();
        foreach (var lineInput in input.Lines)
        {
            var product = await _products.GetByIdAsync(lineInput.ProductId, ct);
            if (product == null)
            {
                errors.Add($"منتج غير موجود: {lineInput.ProductId}");
                continue;
            }

            if (!product.IsActive)
            {
                errors.Add($"منتج غير نشط: {product.Name}");
                continue;
            }

            var unit = product.GetUnitById(lineInput.UnitId);
            if (unit == null)
            {
                errors.Add($"وحدة غير موجودة للمنتج: {product.Name}");
                continue;
            }

            var price = product.GetPrice(lineInput.UseWholesalePrice);
            var item = new SaleItem(lineInput.ProductId, lineInput.UnitId, lineInput.Quantity, price, lineInput.UseWholesalePrice);
            item.SetCatalog(product, unit);
            items.Add(item);
        }

        if (errors.Count > 0)
            return Result<SaleDto>.Failure(errors);

        // الحصول على المتجر (MVP: متجر واحد)
        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<SaleDto>.Failure("لم يتم إعداد المتجر بعد");

        // التحقق من البيع الآجل
        if (input.IsCreditSale && !store.AllowCreditSales)
            return Result<SaleDto>.Failure("البيع الآجل غير مفعل في إعدادات المتجر");

        if (input.IsCreditSale && input.DueDate == null)
            return Result<SaleDto>.Failure("تاريخ الاستحقاق مطلوب للبيع الآجل");

        // إنشاء البيع
        var sale = new Sale(store.Id, cashierId, items, input.Discount, input.Paid, 
            (PaymentMethod)input.PaymentMethod, input.IsCreditSale, input.DueDate);

        await _unitOfWork.Sales.AddAsync(sale, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return Result<SaleDto>.Success(MapToDto(sale));
    }

    private static SaleDto MapToDto(Sale sale) => new(
        sale.Id,
        sale.SaleDate,
        (SaleStatusDto)sale.Status,
        sale.Items.Select(i => new SaleLineDto(
            i.Id, i.ProductId, i.Product?.Name ?? "", i.Product?.Barcode,
            i.UnitId, i.Unit?.Name ?? "", i.Unit?.Symbol ?? "",
            i.Quantity, i.UnitPrice, i.LineTotal, i.IsWholesalePrice)).ToList(),
        sale.Subtotal, sale.Discount, sale.Total, sale.Paid, sale.Remaining,
        (PaymentMethodDto)sale.PaymentMethod, sale.IsCreditSale, sale.DueDate,
        sale.CashierId, null, sale.IsCorrected, sale.OriginalSaleId, sale.CorrectionReason);
}