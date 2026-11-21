namespace Wizz.Presentation.Services;

using Microsoft.Extensions.Hosting;
using Wizz.Application.Abstractions;
using Wizz.Application.AI;

/// <summary>
/// يحدّث كتالوج النماذج العامة في الخلفية على فترات متباعدة، وبعد تفعيل صريح من المستخدم فقط.
/// لا يُرسل أي سؤال أو صورة أو مفتاح API — يقرأ GET عامًا لنقطة <c>/models</c> ويحفظ النتيجة محليًا.
/// أي فشل يُسجَّل في اللقطة نفسها ولا يوقف التطبيق أو يغيّر مسار الرد.
/// </summary>
public sealed class FreeModelCatalogBackgroundService : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

    private readonly IFreeModelCatalogService _catalog;
    private readonly IAiSettingsProvider _settings;
    private readonly ILogger<FreeModelCatalogBackgroundService> _logger;

    public FreeModelCatalogBackgroundService(
        IFreeModelCatalogService catalog,
        IAiSettingsProvider settings,
        ILogger<FreeModelCatalogBackgroundService> logger)
    {
        _catalog = catalog;
        _settings = settings;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(RefreshInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            // المفتاح مغلق افتراضيًا؛ ألا تُجرى أي قراءة شبكية قبل تفعيله صراحةً.
            if (_settings.AutoModelCatalogEnabled)
            {
                try
                {
                    await _catalog.RefreshAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "فشلت قراءة الكتالوج العام في الخلفية؛ تُبقى اللقطة السابقة.");
                }
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
