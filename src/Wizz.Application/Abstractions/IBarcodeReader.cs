namespace Wizz.Application.Abstractions;

/// <summary>واجهة قارئ الباركود — تتيح المحاكاة والطرازات الحقيقية.</summary>
public interface IBarcodeReader
{
    /// <summary>قراءة باركود واحد — يعيد الكود أو null إذا ألغي/انتهت المهلة.</summary>
    Task<string?> ReadAsync(CancellationToken ct = default);

    /// <summary>هل القارئ متصل وجاهز؟</summary>
    bool IsConnected { get; }

    /// <summary>اسم القارئ/الطراز للعرض.</summary>
    string DeviceName { get; }
}