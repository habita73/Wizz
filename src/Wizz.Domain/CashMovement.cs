namespace Wizz.Domain;

/// <summary>نوع الحركة النقدية على الوردية.</summary>
public enum CashMovementType
{
    /// <summary>إيداع نقدي في الصندوق.</summary>
    Deposit = 0,

    /// <summary>سحب نقدي من الصندوق.</summary>
    Withdrawal = 1,
}

/// <summary>
/// حركة نقدية على وردية مفتوحة. لا تُعدَّل ولا تُحذف بعد إنشائها؛ التصحيح بحركة معاكسة
/// بسبب إلزامي. الوقت يُسجَّل من النظام ولا يُقبل تاريخ رجعي.
/// </summary>
public sealed class CashMovement
{
    public Guid Id { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid ShiftId { get; private set; }
    public CashMovementType Type { get; private set; }
    public decimal Amount { get; private set; }
    public string Reason { get; private set; } = default!;
    public DateTime OccurredAt { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? ReferencePurchaseId { get; private set; }

    private CashMovement() { } // EF Core

    public CashMovement(
        Guid storeId,
        Guid shiftId,
        CashMovementType type,
        decimal amount,
        string reason,
        Guid createdByUserId,
        Guid? referencePurchaseId = null,
        DateTime? occurredAt = null)
    {
        if (storeId == Guid.Empty)
            throw new ArgumentException("معرّف المتجر مطلوب", nameof(storeId));
        if (shiftId == Guid.Empty)
            throw new ArgumentException("معرّف الوردية مطلوب", nameof(shiftId));
        if (type is not (CashMovementType.Deposit or CashMovementType.Withdrawal))
            throw new ArgumentOutOfRangeException(nameof(type), "نوع الحركة النقدية غير معروف");
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "مبلغ الحركة النقدية يجب أن يكون أكبر من صفر");
        Money.RequireAtMostTwoDecimalPlaces(amount, nameof(amount));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("سبب الحركة النقدية مطلوب", nameof(reason));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("معرّف الفاعل مطلوب", nameof(createdByUserId));

        Id = Guid.NewGuid();
        StoreId = storeId;
        ShiftId = shiftId;
        Type = type;
        Amount = Money.Round(amount);
        Reason = reason.Trim();
        CreatedByUserId = createdByUserId;
        ReferencePurchaseId = referencePurchaseId;
        OccurredAt = occurredAt ?? DateTime.UtcNow;
    }
}
