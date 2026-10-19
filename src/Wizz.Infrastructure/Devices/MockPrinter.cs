namespace Wizz.Infrastructure.Devices;

using Wizz.Application.Abstractions;

/// <summary>محاكي طابعة — يعيد المحتوى كـ HTML/نص للمراجعة، ويدعم PDF لاحقاً.</summary>
public sealed class MockPrinter : IPrinter
{
    private bool _connected = true;
    private readonly List<PrintJob> _jobs = new();

    public bool IsConnected => _connected;
    public string DeviceName => "Mock Printer (محاكي طابعة حرارية)";
    public int PaperWidthMm => 80;

    public Task<bool> PrintAsync(string content, CancellationToken ct = default)
    {
        if (!_connected) return Task.FromResult(false);
        var job = new PrintJob(DateTime.UtcNow, content, false);
        _jobs.Add(job);
        System.Diagnostics.Debug.WriteLine($"[MOCK PRINTER] {content}");
        return Task.FromResult(true);
    }

    public Task<bool> PrintRawAsync(byte[] data, CancellationToken ct = default)
    {
        if (!_connected) return Task.FromResult(false);
        var content = System.Text.Encoding.UTF8.GetString(data);
        var job = new PrintJob(DateTime.UtcNow, content, true);
        _jobs.Add(job);
        System.Diagnostics.Debug.WriteLine($"[MOCK PRINTER RAW] {content}");
        return Task.FromResult(true);
    }

    public IReadOnlyList<PrintJob> GetJobs() => _jobs.AsReadOnly();
    public void ClearJobs() => _jobs.Clear();
    public void SetConnected(bool connected) => _connected = connected;

    public sealed record PrintJob(DateTime Timestamp, string Content, bool IsRaw);
}