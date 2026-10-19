namespace Wizz.Application.Abstractions;

/// <summary>واجهة الطابعة — تتيح المحاكاة (HTML/نص/PDF) والطرازات الحرارية الحقيقية.</summary>
public interface IPrinter
{
    /// <summary>طباعة إيصال/مستند — يعيد true إذا نجح.</summary>
    Task<bool> PrintAsync(string content, CancellationToken ct = default);

    /// <summary>طباعة خام (للطابعات الحرارية ESC/POS).</summary>
    Task<bool> PrintRawAsync(byte[] data, CancellationToken ct = default);

    /// <summary>هل الطابعة متصلة وجاهزة؟</summary>
    bool IsConnected { get; }

    /// <summary>اسم الطابعة/الطراز للعرض.</summary>
    string DeviceName { get; }

    /// <summary>عرض ورقة الطابعة بالملم (للطابعات الحرارية).</summary>
    int PaperWidthMm { get; }
}