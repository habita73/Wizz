namespace Wizz.Application.UseCases.Sales;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;
using Wizz.Domain;

/// <summary>
/// تحصيل جزء من دين بيع آجل: يُنقص المتبقي، ويُثبّت التسديد في سجل مستقل،
/// ويُسجَّل إيداع نقدي في الوردية المفتوحة إذا كان التحصيل نقدًا.
/// </summary>
public sealed class RecordDebtPaymentUseCase
{
    private readonly IUnitOfWork _unitOfWork;

    public RecordDebtPaymentUseCase(IUnitOfWork unitOfWork)
        => _unitOfWork = unitOfWork;

    public async Task<Result<DebtPaymentDto>> ExecuteAsync(
        DebtPaymentInputDto input,
        Guid? recordedByUserId = null,
        Guid? cashierId = null,
        CancellationToken ct = default)
    {
        if (input.Amount <= 0)
            return Result<DebtPaymentDto>.Failure("مبلغ التسديد يجب أن يكون أكبر من صفر");

        if (decimal.Round(input.Amount, 2, MidpointRounding.AwayFromZero) != input.Amount)
            return Result<DebtPaymentDto>.Failure("المبلغ لا يمكن أن يتجاوز منزلتين عشريتين");

        var sale = await _unitOfWork.Sales.GetByIdAsync(input.SaleId, ct);
        if (sale == null)
            return Result<DebtPaymentDto>.Failure("البيع غير موجود");

        if (sale.Status == SaleStatus.Cancelled)
            return Result<DebtPaymentDto>.Failure("لا يمكن تسديد بيع ملغي");

        if (sale.Remaining <= 0)
            return Result<DebtPaymentDto>.Failure("هذا البيع مسدّد بالكامل");

        if (input.Amount > sale.Remaining)
            return Result<DebtPaymentDto>.Failure($"المبلغ يتجاوز المتبقي ({sale.Remaining:N2} د.ج)");

        var store = await _unitOfWork.Store.GetAsync(ct);
        if (store == null)
            return Result<DebtPaymentDto>.Failure("لم يتم إعداد المتجر بعد");

        // نمرّر Credit عمدًا: ApplyPayment يغيّر طريقة الدفع الأصلية للبيع عند وجود
        // وسيلة أخرى، وهذا قد يرفع قيمته في إجمالي النقد التاريخي لوردية قديمة.
        // المطلوب هنا زيادة المدفوع ونقص المتبقي فقط.
        sale.ApplyPayment(input.Amount, PaymentMethod.Credit);

        var payment = new DebtPayment(
            sale.Id, sale.StoreId, input.Amount, (PaymentMethod)input.Method,
            recordedByUserId, input.Note);

        await _unitOfWork.Sales.AddPaymentAsync(payment, ct);

        // التحصيل النقدي يدخل الصندوق في الوردية المفتوحة الحالية، لا في الوردية
        // التي وقع فيها البيع الأصلي. إن لم تكن هناك وردية مفتوحة يُسجَّل التسديد وحده.
        if (input.Method == PaymentMethodDto.Cash && cashierId.HasValue)
        {
            var shift = await _unitOfWork.CashierShifts.GetOpenAsync(store.Id, cashierId.Value, ct);
            if (shift != null)
            {
                var saleNumber = sale.Id.ToString("N")[..8].ToUpperInvariant();
                var movement = new CashMovement(
                    store.Id, shift.Id, CashMovementType.Deposit, input.Amount,
                    $"تحصيل دين — بيع {saleNumber}",
                    recordedByUserId ?? cashierId.Value,
                    referencePurchaseId: null,
                    occurredAt: payment.PaidAt);
                await _unitOfWork.CashMovements.AddAsync(movement, ct);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);

        return Result<DebtPaymentDto>.Success(new DebtPaymentDto(
            payment.Id, sale.Id, payment.Amount, input.Method,
            payment.PaidAt, payment.Note, sale.Remaining));
    }
}
