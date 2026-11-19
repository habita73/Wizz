namespace Wizz.Application.AI.Perception;

/// <summary>
/// يختار قارئًا محليًا حسب التوفر: القارئ المضمّن أساسًا، وقرة Windows بديلًا حين لا تتوفر
/// بيانات اللغة المضمّنة. التبديل يتم عند غياب القدرة فقط؛ لا يوجد تبديل تلقائي عند فشل القراءة،
/// ولا أي مسار سحابي.
/// </summary>
public sealed class CompositeOcrTextReader : IOcrTextReader
{
    private readonly IOcrTextReader _primary;
    private readonly IOcrTextReader _fallback;

    public CompositeOcrTextReader(IOcrTextReader primary, IOcrTextReader fallback)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    public bool IsSupported => _primary.IsSupported || _fallback.IsSupported;

    public async Task<OcrReadResult> ReadAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        if (_primary.IsSupported)
            return await _primary.ReadAsync(imageBytes, ct);

        if (_fallback.IsSupported)
            return await _fallback.ReadAsync(imageBytes, ct);

        return OcrReadResult.Unavailable(
            "لا يوجد قارئ نصوص محلي متاح على هذا الجهاز؛ استخدم الإدخال اليدوي.");
    }
}
