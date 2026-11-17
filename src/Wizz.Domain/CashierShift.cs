namespace Wizz.Domain;

/// <summary>
/// وردية كاشير — النافذة الزمنية التي تجمّد المبيعات السابقة. الوردية المفتوحة هي التي
/// تسمح بتعديل/إلغاء مبيعاتها داخل يوم العمل الحالي فقط؛ وبعد الإغلاق تُجمَّد الإجماليات.
/// الحالة مشتقة من <see cref="ClosedAt"/> ولا تُخزَّن حقلًا مستقلًا لتفادي مصدرَي حقيقة.
/// </summary>
public sealed class CashierShift
{
    public Guid Id { get; private set; }
    public Guid StoreId { get; private set; }
    public Guid CashierId { get; private set; }
    public DateTime OpenedAt { get; private set; }
    public Guid OpenedByUserId { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public Guid? ClosedByUserId { get; private set; }

    /// <summary>مجموع إجمالي المبيعات غير الملغاة في الوردية — يُجمَّد عند الإغلاق.</summary>
    public decimal SalesTotal { get; private set; }

    /// <summary>مجموع المدفوع في المبيعات غير الآجلة — يُجمَّد عند الإغلاق.</summary>
    public decimal CashTotal { get; private set; }

    /// <summary>مجموع المتبقي في المبيعات الآجلة — يُجمَّد عند الإغلاق.</summary>
    public decimal CreditTotal { get; private set; }

    /// <summary>الفكة الافتتاحية التي أُدخلت عند فتح الوردية (صفر في الفتح التلقائي والبيانات السابقة).</summary>
    public decimal OpeningFloat { get; private set; }

    /// <summary>مجموع الإيداعات النقدية — يُجمَّد عند الإغلاق.</summary>
    public decimal? DepositsTotal { get; private set; }

    /// <summary>مجموع السحوبات النقدية — يُجمَّد عند الإغلاق.</summary>
    public decimal? WithdrawalsTotal { get; private set; }

    /// <summary>النقد المتوقع عند الإغلاق — يُجمَّد ولا يُعاد حسابه.</summary>
    public decimal? ExpectedCash { get; private set; }

    /// <summary>النقد المعدود فعليًا — يُدخل عند الإغلاق؛ عدم وجوده يعني بياناتًا سابقة بلا عدّ.</summary>
    public decimal? CountedCash { get; private set; }

    /// <summary>فرق الإغلاق = المعدود − المتوقع — يُجمَّد ولا يُعاد حسابه.</summary>
    public decimal? CashDifference { get; private set; }

    /// <summary>ملاحظة الإغلاق أو سبب الفرق.</summary>
    public string? CloseNote { get; private set; }

    public bool IsOpen => ClosedAt is null;

    /// <summary>هل أُجري عدّ نقدي عند الإغلاق؟ الورديات السابقة للمرحلة 3 تُوسم بلا عدّ.</summary>
    public bool HasCashCount => CountedCash is not null;

    private CashierShift() { } // EF Core

    public CashierShift(
        Guid storeId,
        Guid cashierId,
        Guid openedByUserId,
        DateTime? openedAt = null,
        decimal openingFloat = 0)
    {
        if (storeId == Guid.Empty)
            throw new ArgumentException("معرّف المتجر مطلوب", nameof(storeId));
        if (cashierId == Guid.Empty)
            throw new ArgumentException("معرّف الكاشير مطلوب", nameof(cashierId));
        if (openedByUserId == Guid.Empty)
            throw new ArgumentException("معرّف المستخدم الذي يفتح الوردية مطلوب", nameof(openedByUserId));
        if (openingFloat < 0)
            throw new ArgumentOutOfRangeException(nameof(openingFloat), "الفكة الافتتاحية لا يمكن أن تكون سالبة");
        Money.RequireAtMostTwoDecimalPlaces(openingFloat, nameof(openingFloat));

        Id = Guid.NewGuid();
        StoreId = storeId;
        CashierId = cashierId;
        OpenedByUserId = openedByUserId;
        OpenedAt = openedAt ?? DateTime.UtcNow;
        OpeningFloat = Money.Round(openingFloat);
    }

    /// <summary>إغلاق الوردية وتجميد إجمالياتها. لا يقبل الإغلاق مرتين ولا تاريخًا قبل الفتح.</summary>
    public void Close(
        Guid closedByUserId,
        decimal salesTotal,
        decimal cashTotal,
        decimal creditTotal,
        DateTime? closedAt = null)
    {
        if (!IsOpen) throw new InvalidOperationException("الوردية مغلقة بالفعل");
        if (closedByUserId == Guid.Empty)
            throw new ArgumentException("معرّف المستخدم الذي يغلق الوردية مطلوب", nameof(closedByUserId));
        if (salesTotal < 0 || cashTotal < 0 || creditTotal < 0)
            throw new ArgumentOutOfRangeException(nameof(salesTotal), "إجماليات الوردية لا يمكن أن تكون سالبة");

        var at = closedAt ?? DateTime.UtcNow;
        if (at < OpenedAt)
            throw new ArgumentOutOfRangeException(nameof(closedAt), "وقت الإغلاق لا يمكن أن يسبق وقت الفتح");

        ClosedAt = at;
        ClosedByUserId = closedByUserId;
        SalesTotal = salesTotal;
        CashTotal = cashTotal;
        CreditTotal = creditTotal;
    }

    /// <summary>
    /// إغلاق مع عدّ نقدي: يجمّد الإجماليات والإيداعات والسحوبات والمتوقع والمعدود والفرق.
    /// الفرق لا يمنع الإغلاق؛ من تجاوز حدّ التسامح يتحقق من الملاحظة قبل استدعاء هذه الدالة.
    /// </summary>
    public void CloseWithCash(
        Guid closedByUserId,
        decimal salesTotal,
        decimal cashTotal,
        decimal creditTotal,
        decimal depositsTotal,
        decimal withdrawalsTotal,
        decimal expectedCash,
        decimal countedCash,
        string? closeNote,
        DateTime? closedAt = null)
    {
        if (!IsOpen) throw new InvalidOperationException("الوردية مغلقة بالفعل");
        if (closedByUserId == Guid.Empty)
            throw new ArgumentException("معرّف المستخدم الذي يغلق الوردية مطلوب", nameof(closedByUserId));
        if (salesTotal < 0 || cashTotal < 0 || creditTotal < 0 ||
            depositsTotal < 0 || withdrawalsTotal < 0 || expectedCash < 0 || countedCash < 0)
            throw new ArgumentOutOfRangeException(nameof(salesTotal), "إجماليات الوردية والمبالغ النقدية لا يمكن أن تكون سالبة");

        var at = closedAt ?? DateTime.UtcNow;
        if (at < OpenedAt)
            throw new ArgumentOutOfRangeException(nameof(closedAt), "وقت الإغلاق لا يمكن أن يسبق وقت الفتح");

        ClosedAt = at;
        ClosedByUserId = closedByUserId;
        SalesTotal = salesTotal;
        CashTotal = cashTotal;
        CreditTotal = creditTotal;
        DepositsTotal = Money.Round(depositsTotal);
        WithdrawalsTotal = Money.Round(withdrawalsTotal);
        ExpectedCash = Money.Round(expectedCash);
        CountedCash = Money.Round(countedCash);
        CashDifference = Money.Round(CountedCash.Value - ExpectedCash.Value);
        CloseNote = string.IsNullOrWhiteSpace(closeNote) ? null : closeNote.Trim();
    }
}
