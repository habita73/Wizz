namespace Wizz.Application.AI.Perception;

/// <summary>
/// تحسين اختياري للصورة داخل الجهاز، ونسخة مصغّرة للمعاينة. لا نماذج، لا اتصال،
/// ولا تغيير في القارئ الافتراضي؛ يُستدعى فقط حين يفعّل المستخدم التحسين.
/// </summary>
public interface IImageEnhancer
{
    /// <summary>يعيد صورة محسّنة بصيغة PNG، أو <c>null</c> إذا تعذّر فكّ الصورة.</summary>
    byte[]? Enhance(byte[] imageBytes);

    /// <summary>يعيد نسخة مصغّرة بصيغة JPEG للمعاينة فقط، أو <c>null</c> إذا تعذّر فكّ الصورة.</summary>
    byte[]? CreateThumbnail(byte[] imageBytes, int maxDimension);
}
