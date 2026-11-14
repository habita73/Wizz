namespace Wizz.Application.UseCases.Sales;

using Wizz.Application.Abstractions;
using Wizz.Application.UseCases.StockDocuments;
using Wizz.Domain;

/// <summary>
/// بوابة نافذة التعديل (D-24): يُسمح بتعديل/إلغاء البيع فقط داخل ورديته المفتوحة
/// وفي يوم العمل الحالي للمتجر. ما عدا ذلك يُصحَّح بمستند مرتجع جديد.
/// </summary>
internal static class ShiftGate
{
    public static async Task<string?> RejectReasonAsync(
        IUnitOfWork unitOfWork,
        Sale sale,
        Store store,
        CancellationToken ct)
    {
        if (sale.ShiftId is not Guid shiftId)
            return "البيع غير مرتبط بوردية مفتوحة (بيانات سابقة) — استخدم المرتجع MRB للتصحيح";

        var shift = await unitOfWork.CashierShifts.GetByIdAsync(shiftId, ct);
        if (shift is null)
            return "وردية البيع غير موجودة — لا يسمح بتعديل أو إلغاء البيع";

        if (!shift.IsOpen)
            return "وردية البيع مغلقة — لا يسمح بتعديل أو إلغاء مبيعاتها؛ استخدم المرتجع MRB";

        var businessDate = StockWorkflow.BusinessDate(store, sale.SaleDate);
        if (businessDate != StockWorkflow.Today(store))
            return "لا يسمح بتعديل أو إلغاء مبيعات يوم عمل سابق — استخدم المرتجع MRB";

        return null;
    }

    /// <summary>هل البيع داخل نافذة التعديل المفتوحة؟ يُستخدم لمنع المرتجع داخل النافذة.</summary>
    public static async Task<bool> IsWithinEditWindowAsync(
        IUnitOfWork unitOfWork,
        Sale sale,
        Store store,
        CancellationToken ct)
        => await RejectReasonAsync(unitOfWork, sale, store, ct) is null;
}

/// <summary>فتح وردية عند الحاجة — مشترك بين الفتح اليدوي والفتح التلقائي مع أول بيع.</summary>
internal static class ShiftWorkflow
{
    public static async Task<CashierShift> GetOrOpenAsync(
        IUnitOfWork unitOfWork,
        Store store,
        Guid cashierId,
        Guid openedByUserId,
        CancellationToken ct)
    {
        var existing = await unitOfWork.CashierShifts.GetOpenAsync(store.Id, cashierId, ct);
        if (existing is not null)
            return existing;

        var shift = new CashierShift(store.Id, cashierId, openedByUserId);
        await unitOfWork.CashierShifts.AddAsync(shift, ct);
        return shift;
    }
}
