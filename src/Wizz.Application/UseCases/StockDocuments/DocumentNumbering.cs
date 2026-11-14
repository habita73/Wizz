namespace Wizz.Application.UseCases.StockDocuments;

using Microsoft.EntityFrameworkCore;
using Wizz.Domain;

/// <summary>
/// تخصيص أرقام المستندات: {CODE}-{YYYY}-{SEQ} بتسلسل مستقل لكل (متجر، نوع، سنة).
/// يجب أن يُستدعى داخل معاملة المستند؛ التخصيص ذرّي والقراءة بعد التحديث محمية بقفل الكتابة.
/// </summary>
public static class DocumentNumbering
{
    public const int MaxAttempts = 3;

    public static string Format(StockDocumentType type, DateOnly documentDate, int sequence, int yearDigits)
    {
        var code = StockDocumentTypeCodes.GetCode(type);
        var year = yearDigits == 2
            ? (documentDate.Year % 100).ToString("00")
            : documentDate.Year.ToString("0000");
        return $"{code}-{year}-{sequence:D4}";
    }

    public static async Task<string> AllocateAsync(
        DbContext db,
        Guid storeId,
        StockDocumentType type,
        DateOnly documentDate,
        int yearDigits,
        CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("لا يجوز تخصيص رقم مستند خارج معاملة المستند");

        var digits = yearDigits is 2 or 4 ? yearDigits : 4;
        var year = documentDate.Year;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // إنشاء الصف أول مرة بشكل ذرّي ودون المساس بمتتبع التغييرات.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT OR IGNORE INTO "DocumentSequences" ("StoreId", "DocumentType", "Year", "LastNumber")
                VALUES ({storeId}, {(int)type}, {year}, 0)
                """, ct);

            var updated = await db.Set<DocumentSequence>()
                .Where(s => s.StoreId == storeId && s.DocumentType == type && s.Year == year)
                .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.LastNumber, s => s.LastNumber + 1), ct);

            if (updated == 1)
            {
                var lastNumber = await db.Set<DocumentSequence>()
                    .AsNoTracking()
                    .Where(s => s.StoreId == storeId && s.DocumentType == type && s.Year == year)
                    .Select(s => s.LastNumber)
                    .SingleAsync(ct);

                return Format(type, documentDate, lastNumber, digits);
            }
        }

        throw new InvalidOperationException("تعذّر تخصيص رقم مستند بعد عدة محاولات؛ لم يُصدر رقم مؤقت");
    }
}
