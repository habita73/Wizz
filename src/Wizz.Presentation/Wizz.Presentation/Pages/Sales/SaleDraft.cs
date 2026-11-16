namespace Wizz.Presentation.Pages.Sales;

/// <summary>
/// مسودة سلة بيع غير مكتملة، تُحفظ في متصفح الكاشير (localStorage) بعد كل تعديل
/// وتُسترجع عند العودة إلى شاشة البيع. الغرض: عدم فقدان الفاتورة الجارية عند
/// الانتقال إلى صفحة أخرى أو إعادة تحميل الصفحة أو إغلاق المتصفح.
/// لا تُحفظ هنا أي أسعار أو مبالغ محسوبة نهائياً: تُعاد قراءة أسعار المنتجات
/// والمخزون من قاعدة البيانات عند الاسترجاع، فلا تُبنى فاتورة على سعر قديم.
/// </summary>
public sealed class SaleDraft
{
    /// <summary>معرّف الكاشير صاحب المسودة — لا تُسترجع مسودة كاشير آخر.</summary>
    public string CashierId { get; set; } = "";

    /// <summary>وقت الحفظ بتوقيت UTC — تُهمل المسودات الأقدم من المدة المسموحة.</summary>
    public DateTime SavedAtUtc { get; set; }

    public List<SaleDraftLine> Lines { get; set; } = new();

    public decimal Discount { get; set; }

    public decimal PaidAmount { get; set; }

    public int PaymentMethod { get; set; }

    public DateTime DueDate { get; set; }
}

/// <summary>سطر في المسودة: المنتج والكمية ونوع السعر فقط.</summary>
public sealed class SaleDraftLine
{
    public Guid ProductId { get; set; }

    public decimal Quantity { get; set; }

    public bool IsWholesale { get; set; }
}
