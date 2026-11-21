namespace Wizz.Domain;

/// <summary>
/// تسديدة واحدة لدين بيع آجل. تُخزَّن كسجل مستقل حتى يبقى لكل مبلغ محصَّل
/// تاريخه وفاعله، ولا يقتصر الأثر على تغيير المتبقي في الفاتورة.
/// </summary>
public sealed class DebtPayment
{
    public Guid Id { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid StoreId { get; private set; }
    public decimal Amount { get; private set; }

    /// <summary>وسيلة التحصيل. لا تُستخدم لتغيير طريقة الدفع الأصلية للبيع.</summary>
    public PaymentMethod Method { get; private set; }

    public DateTime PaidAt { get; private set; }
    public Guid? RecordedByUserId { get; private set; }
    public string? Note { get; private set; }

    private DebtPayment() { } // EF Core

    public DebtPayment(
        Guid saleId,
        Guid storeId,
        decimal amount,
        PaymentMethod method,
        Guid? recordedByUserId = null,
        string? note = null,
        DateTime? paidAt = null)
    {
        if (saleId == Guid.Empty) throw new ArgumentException("معرّف البيع مطلوب", nameof(saleId));
        if (storeId == Guid.Empty) throw new ArgumentException("معرّف المتجر مطلوب", nameof(storeId));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "مبلغ التسديد يجب أن يكون أكبر من صفر");
        Money.RequireAtMostTwoDecimalPlaces(amount, nameof(amount));

        Id = Guid.NewGuid();
        SaleId = saleId;
        StoreId = storeId;
        Amount = Money.Round(amount);
        Method = method;
        RecordedByUserId = recordedByUserId;
        Note = NormalizeNote(note);
        PaidAt = paidAt ?? DateTime.UtcNow;
    }

    private static string? NormalizeNote(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[..200];
    }
}
