namespace Wizz.Infrastructure.Devices;

using Wizz.Application.Abstractions;

/// <summary>محاكي قارئ باركود — يدخل الكود نصياً للاختبار والتطوير.</summary>
public sealed class MockScanner : IBarcodeReader
{
    private readonly Queue<string> _preloadedCodes = new();
    private bool _connected = true;

    public bool IsConnected => _connected;
    public string DeviceName => "Mock Barcode Scanner (محاكي)";

    public void PreloadCode(string code) => _preloadedCodes.Enqueue(code);
    public void PreloadCodes(IEnumerable<string> codes)
    {
        foreach (var c in codes) _preloadedCodes.Enqueue(c);
    }

    public Task<string?> ReadAsync(CancellationToken ct = default)
    {
        if (!_connected) return Task.FromResult<string?>(null);
        if (_preloadedCodes.Count > 0)
            return Task.FromResult<string?>(_preloadedCodes.Dequeue());

        // في بيئة حقيقية: انتظار إدخال من المستخدم
        // هنا نعيد null لمحاكاة "لا يوجد كود محمل"
        return Task.FromResult<string?>(null);
    }

    public void SetConnected(bool connected) => _connected = connected;
}