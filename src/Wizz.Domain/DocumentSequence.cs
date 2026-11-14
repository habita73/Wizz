namespace Wizz.Domain;

/// <summary>
/// مُعدِّد ترقيم مستندات مستقل لكل (متجر، نوع مستند، سنة).
/// يُحدَّث داخل معاملة المستند نفسها، ويُحمى بقيد فريد مركّب.
/// </summary>
public sealed class DocumentSequence
{
    public Guid StoreId { get; private set; }
    public StockDocumentType DocumentType { get; private set; }
    public int Year { get; private set; }
    public int LastNumber { get; private set; }

    private DocumentSequence() { } // EF Core

    public DocumentSequence(Guid storeId, StockDocumentType documentType, int year, int lastNumber = 0)
    {
        if (storeId == Guid.Empty)
            throw new ArgumentException("معرّف المتجر مطلوب", nameof(storeId));
        if (year < 1)
            throw new ArgumentOutOfRangeException(nameof(year), "السنة غير صالحة");
        if (lastNumber < 0)
            throw new ArgumentOutOfRangeException(nameof(lastNumber), "آخر رقم لا يمكن أن يكون سالبًا");

        StoreId = storeId;
        DocumentType = documentType;
        Year = year;
        LastNumber = lastNumber;
    }
}
