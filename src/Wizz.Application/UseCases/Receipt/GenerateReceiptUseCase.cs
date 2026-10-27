namespace Wizz.Application.UseCases.Receipt;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>إنشاء إيصال للطباعة.</summary>
public sealed class GenerateReceiptUseCase
{
    private readonly ISaleRepository _sales;
    private readonly IStoreRepository _store;

    public GenerateReceiptUseCase(ISaleRepository sales, IStoreRepository store)
    {
        _sales = sales;
        _store = store;
    }

    public async Task<Result<ReceiptDto>> ExecuteAsync(Guid saleId, CancellationToken ct = default)
    {
        var sale = await _sales.GetByIdAsync(saleId, ct);
        if (sale == null)
            return Result<ReceiptDto>.Failure("عملية البيع غير موجودة");

        var store = await _store.GetAsync(ct);
        if (store == null)
            return Result<ReceiptDto>.Failure("لم يتم إعداد المتجر");

        var receipt = new ReceiptDto(
            sale.Id,
            sale.Id.ToString("N")[..8].ToUpper(), // رقم بيع مختصر
            sale.SaleDate,
            new StoreDto(store.Id, store.Name, store.Address, store.Phone, store.TaxNumber, store.CommercialRegister,
                store.AllowCreditSales, store.Currency, store.TimeZone, store.InventoryEnabled),
            sale.Items.Select(i => new ReceiptLineDto(
                i.Product?.Name ?? "منتج غير معروف",
                i.Quantity,
                i.Unit?.Symbol ?? "وحدة",
                i.UnitPrice,
                i.LineTotal)).ToList(),
            sale.Subtotal,
            sale.Discount,
            sale.Total,
            sale.Paid,
            sale.Remaining,
            (PaymentMethodDto)sale.PaymentMethod,
            null, // اسم الكاشير — يحتاج UserRepository
            sale.IsCreditSale,
            sale.DueDate,
            sale.CorrectionReason
        );

        return Result<ReceiptDto>.Success(receipt);
    }
}