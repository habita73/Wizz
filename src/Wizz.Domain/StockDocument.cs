namespace Wizz.Domain;

/// <summary>نوع مستند حركة المخزون. كل نوع له رمز ثابت لا يُعاد استخدامه.</summary>
public enum StockDocumentType
{
    /// <summary>رصيد افتتاحي — الرمز RSL.</summary>
    OpeningBalance = 0,

    /// <summary>جرد — الرمز JRD.</summary>
    StockCount = 1,

    /// <summary>إتلاف/تالف — الرمز ITL.</summary>
    Waste = 2,

    /// <summary>مرتجع بيع — الرمز MRB.</summary>
    SalesReturn = 3,

    /// <summary>شراء — الرمز SHR (محجوز للمرحلة 3).</summary>
    Purchase = 4,

    /// <summary>تسوية — الرمز TSW.</summary>
    Adjustment = 5
}

/// <summary>رموز أنواع المستندات المعتمدة وقواعدها المساعدة.</summary>
public static class StockDocumentTypeCodes
{
    public static string GetCode(StockDocumentType type) => type switch
    {
        StockDocumentType.OpeningBalance => "RSL",
        StockDocumentType.StockCount => "JRD",
        StockDocumentType.Waste => "ITL",
        StockDocumentType.SalesReturn => "MRB",
        StockDocumentType.Purchase => "SHR",
        StockDocumentType.Adjustment => "TSW",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "نوع مستند غير معروف")
    };

    /// <summary>السبب إلزامي في الجرد والإتلاف والتسوية والمرتجع، اختياري في الافتتاحي والشراء.</summary>
    public static bool RequiresReason(StockDocumentType type) => type is
        StockDocumentType.StockCount or
        StockDocumentType.Waste or
        StockDocumentType.Adjustment or
        StockDocumentType.SalesReturn;
}

/// <summary>وسوم مصدر المستند عند الترحيل — تفصل المُعاد بناؤه عن الاصطناعي.</summary>
public static class StockDocumentSource
{
    public const string Reconstructed = "reconstructed";
    public const string Synthetic = "synthetic";
}

/// <summary>
/// مستند حركة مخزون — مصدر الحقيقة الوحيد. الرصيد المخزَّن ذاكرة مؤقتة تُشتق منه،
/// ولا يُكتسب رقم المستند إلا بعد تخصيصه داخل معاملة المستند.
/// </summary>
public sealed class StockDocument
{
    public Guid Id { get; private set; }
    public Guid StoreId { get; private set; }
    public StockDocumentType Type { get; private set; }
    public string Number { get; private set; } = default!;
    public DateOnly DocumentDate { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public string? Reason { get; private set; }
    public Guid? ReferenceSaleId { get; private set; }
    public string? Source { get; private set; }
    public bool IsVoided { get; private set; }
    public DateTime? VoidedAt { get; private set; }
    public Guid? VoidedByUserId { get; private set; }
    public string? VoidReason { get; private set; }

    /// <summary>معرّف الحركة القديمة المُرحّلة — قيد تقني فريد يضمن إعادة تشغيل آمنة.</summary>
    public Guid? LegacyStockMovementId { get; private set; }

    private readonly List<StockDocumentLine> _lines = new();
    public IReadOnlyList<StockDocumentLine> Lines => _lines.AsReadOnly();

    private StockDocument() { } // EF Core

    public StockDocument(
        Guid storeId,
        StockDocumentType type,
        string number,
        DateOnly documentDate,
        Guid? createdByUserId,
        string? reason,
        IEnumerable<StockDocumentLine> lines,
        Guid? referenceSaleId = null,
        string? source = null,
        DateTime? createdAt = null,
        Guid? legacyStockMovementId = null)
    {
        if (storeId == Guid.Empty)
            throw new ArgumentException("معرّف المتجر مطلوب", nameof(storeId));
        if (string.IsNullOrWhiteSpace(number))
            throw new ArgumentException("رقم المستند مطلوب", nameof(number));
        if (StockDocumentTypeCodes.RequiresReason(type) && string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("سبب المستند مطلوب لهذا النوع", nameof(reason));

        var lineList = lines?.ToList() ?? throw new ArgumentNullException(nameof(lines));
        if (lineList.Count == 0)
            throw new ArgumentException("المستند يجب أن يحتوي سطرًا واحدًا على الأقل", nameof(lines));

        Id = Guid.NewGuid();
        StoreId = storeId;
        Type = type;
        Number = number.Trim();
        DocumentDate = documentDate;
        CreatedAt = createdAt ?? DateTime.UtcNow;
        CreatedByUserId = createdByUserId;
        Reason = Normalize(reason);
        ReferenceSaleId = referenceSaleId;
        Source = Normalize(source);
        LegacyStockMovementId = legacyStockMovementId;

        foreach (var line in lineList)
        {
            line.AttachToDocument(Id);
            _lines.Add(line);
        }
    }

    /// <summary>إبطال المستند بمستند عكسي — لا حذف ولا تعديل للرقم.</summary>
    public void Void(string reason, Guid? voidedByUserId, DateTime? voidedAt = null)
    {
        if (IsVoided) throw new InvalidOperationException("المستند ملغى بالفعل");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("سبب الإبطال مطلوب", nameof(reason));

        IsVoided = true;
        VoidReason = reason.Trim();
        VoidedByUserId = voidedByUserId;
        VoidedAt = voidedAt ?? DateTime.UtcNow;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>سطر مستند مخزون — النتيجة الموقّعة بالوحدة الأساسية هي ما يدخل المعادلة.</summary>
public sealed class StockDocumentLine
{
    public Guid Id { get; private set; }
    public Guid StockDocumentId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid UnitId { get; private set; }

    /// <summary>الكمية كما أدخلها المستخدم بالوحدة المختارة (موجبة دائمًا).</summary>
    public decimal Quantity { get; private set; }

    /// <summary>التغير الموقّع بالوحدة الأساسية للمنتج — هو ما يدخل معادلة الرصيد.</summary>
    public decimal BaseQuantityDelta { get; private set; }

    public decimal QuantityBefore { get; private set; }
    public decimal QuantityAfter { get; private set; }
    public string? Reason { get; private set; }

    public StockDocument? StockDocument { get; private set; }

    private StockDocumentLine() { } // EF Core

    public StockDocumentLine(
        Guid productId,
        Guid unitId,
        decimal quantity,
        decimal baseQuantityDelta,
        decimal quantityBefore,
        decimal quantityAfter,
        string? reason = null)
    {
        if (productId == Guid.Empty)
            throw new ArgumentException("معرّف المنتج مطلوب", nameof(productId));
        if (unitId == Guid.Empty)
            throw new ArgumentException("معرّف الوحدة مطلوب", nameof(unitId));
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "كمية السطر يجب أن تكون أكبر من صفر");
        if (baseQuantityDelta == 0)
            throw new ArgumentOutOfRangeException(nameof(baseQuantityDelta), "تغير الكمية لا يمكن أن يكون صفرًا");

        Id = Guid.NewGuid();
        ProductId = productId;
        UnitId = unitId;
        Quantity = quantity;
        BaseQuantityDelta = baseQuantityDelta;
        QuantityBefore = quantityBefore;
        QuantityAfter = quantityAfter;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    internal void AttachToDocument(Guid stockDocumentId) => StockDocumentId = stockDocumentId;
}
