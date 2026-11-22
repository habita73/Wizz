namespace Wizz.Presentation.Services;

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Configuration;
using Wizz.Application.DTOs;
using Wizz.Application.Receipts;

/// <summary>
/// يرسل إيصال بيع نصيًا بترميز UTF-8 إلى طابعة تجريبية مثبتة على Windows بصيغة RAW،
/// ويحفظ نسخة مؤرشفة لكل طباعة. لا يغيّر بيانات البيع ولا يتصل بأي جهاز آخر أو سحابة.
/// </summary>
public sealed class DirectReceiptPrinter
{
    /// <summary>اسم الطابعة الافتراضية المستخدمة في بيئة التطوير.</summary>
    public const string DefaultPrinterName = "XP-480B (Virtual)";

    private readonly ThermalReceiptService _receiptText;
    private readonly string _printerName;
    private readonly string _archiveDirectory;

    public DirectReceiptPrinter(IConfiguration configuration, ThermalReceiptService receiptText)
    {
        _receiptText = receiptText;

        var configuredName = configuration["ReceiptPrinter:PrinterName"];
        _printerName = string.IsNullOrWhiteSpace(configuredName) ? DefaultPrinterName : configuredName.Trim();

        var configuredDirectory = configuration["ReceiptPrinter:ArchiveDirectory"];
        _archiveDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wizz", "PrintedReceipts")
            : configuredDirectory.Trim();
    }

    /// <summary>اسم الطابعة التي تُرسل إليها الطباعة المباشرة.</summary>
    public string PrinterName => _printerName;

    /// <summary>مجلد النسخ المؤرشفة لكل إيصال مطبوع مباشرةً.</summary>
    public string ArchiveDirectory => _archiveDirectory;

    /// <summary>يطبع الإيصال مباشرةً ويعيد نتيجة العملية ومسار النسخة المحفوظة.</summary>
    public async Task<DirectPrintResult> PrintReceiptAsync(ReceiptDto receipt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var bytes = Encoding.UTF8.GetBytes(_receiptText.BuildText(receipt));
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var fileName = $"receipt-{stamp}-{receipt.SaleNumber}.prn";
        var archivePath = Path.Combine(_archiveDirectory, fileName);

        try
        {
            Directory.CreateDirectory(_archiveDirectory);
            await File.WriteAllBytesAsync(archivePath, bytes, ct);
        }
        catch (Exception ex)
        {
            return new DirectPrintResult(false, $"تعذّر حفظ نسخة الإيصال: {ex.Message}", null);
        }

        if (!OperatingSystem.IsWindows())
        {
            return new DirectPrintResult(false, "الطباعة المباشرة متاحة على Windows فقط.", archivePath);
        }

        var sent = TrySendRaw(_printerName, bytes, out var error);
        return sent
            ? new DirectPrintResult(true, $"تمت الطباعة على {_printerName}. النسخة المحفوظة: {archivePath}", archivePath)
            : new DirectPrintResult(false, $"تعذّر الإرسال إلى {_printerName}: {error}. النسخة المحفوظة: {archivePath}", archivePath);
    }

    [SupportedOSPlatform("windows")]
    private static bool TrySendRaw(string printerName, byte[] data, out string error)
    {
        error = string.Empty;
        var handle = IntPtr.Zero;
        try
        {
            if (!OpenPrinter(printerName, out handle, IntPtr.Zero))
            {
                error = $"تعذّر فتح الطابعة (خطأ {Marshal.GetLastWin32Error()}).";
                return false;
            }

            var info = new DocInfo
            {
                DocName = "Wizz - إيصال بيع",
                OutputFile = null,
                DataType = "RAW"
            };
            if (!StartDocPrinter(handle, 1, ref info))
            {
                error = $"تعذّر بدء مهمة الطباعة (خطأ {Marshal.GetLastWin32Error()}).";
                return false;
            }

            var pageStarted = StartPagePrinter(handle);
            var offset = 0;
            while (offset < data.Length)
            {
                var count = Math.Min(4096, data.Length - offset);
                var chunk = new byte[count];
                Array.Copy(data, offset, chunk, 0, count);
                if (!WritePrinter(handle, chunk, count, out var written) || written <= 0)
                {
                    error = $"تعذّر إرسال بيانات الإيصال (خطأ {Marshal.GetLastWin32Error()}).";
                    return false;
                }

                offset += written;
            }

            if (pageStarted)
                EndPagePrinter(handle);
            EndDocPrinter(handle);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
        finally
        {
            if (handle != IntPtr.Zero)
                ClosePrinter(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocInfo
    {
        [MarshalAs(UnmanagedType.LPWStr)] public string? DocName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? OutputFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? DataType;
    }

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinter(string printerName, out IntPtr handle, IntPtr defaults);

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool StartDocPrinter(IntPtr handle, int level, ref DocInfo info);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool StartPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool WritePrinter(IntPtr handle, byte[] buffer, int count, out int written);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndPagePrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool EndDocPrinter(IntPtr handle);

    [DllImport("winspool.drv", SetLastError = true)]
    private static extern bool ClosePrinter(IntPtr handle);
}

/// <summary>نتيجة طباعة إيصال مباشرةً.</summary>
public sealed record DirectPrintResult(bool Success, string Message, string? ArchivePath);
