namespace Wizz.Application.AI;

using System.Diagnostics;
using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;

/// <summary>
/// استفسار AI مع سياق المتجر.
/// التوجيه يتم الآن عبر IModelRouter المستقل:
/// - الأسئلة المباشرة → المساعد المدمج (قواعد برمجية، فوري، بدون نموذج)
/// - الأسئلة التحليلية + نموذج محلي متاح → النموذج المحلي أولاً
/// - الأسئلة الحرة + اتصال سحابي + موافقة المستخدم → مزود سحابي مجاني
/// - أي فشل → رجوع تلقائي للمدمج أو إرشاد واضح
/// </summary>
public sealed class QueryAiUseCase
{
    private readonly IModelRouter _router;
    private readonly BuiltInAssistant _assistant;

    public QueryAiUseCase(IModelRouter router, BuiltInAssistant assistant)
    {
        _router = router;
        _assistant = assistant;
    }

    public async Task<Result<AiQueryResultDto>> ExecuteAsync(AiQueryInputDto input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Question))
            return Result<AiQueryResultDto>.Failure("اكتب سؤالاً أولاً.");

        var sw = Stopwatch.StartNew();

        try
        {
            var routeResult = await _router.RouteAsync(input, ct);
            sw.Stop();

            if (routeResult.RouteType == RouteType.None)
            {
                // لا مسار مؤهل — إرشاد واضح ولكن لا فشل
                return Result<AiQueryResultDto>.Success(new AiQueryResultDto(
                    routeResult.Answer,
                    true,
                    Array.Empty<string>(),
                    sw.Elapsed,
                    routeResult.ProviderName));
            }

            return Result<AiQueryResultDto>.Success(new AiQueryResultDto(
                routeResult.Answer,
                routeResult.UsedStoreData,
                routeResult.ReferencedData,
                sw.Elapsed,
                routeResult.ProviderName));
        }
        catch (Exception ex)
        {
            sw.Stop();
            Debug.WriteLine($"QueryAiUseCase failed: {ex.Message}");

            // fallback الأخير: محاولة المدمج مباشرة
            try
            {
                var builtIn = await _assistant.TryAnswerAsync(input.Question, ct);
                if (builtIn is not null)
                {
                    return Result<AiQueryResultDto>.Success(new AiQueryResultDto(
                        builtIn,
                        true,
                        new[] { "بيانات محلية" },
                        sw.Elapsed,
                        _assistant.ProviderName));
                }
            }
            catch
            {
                // تجاهل — نreturn إرشاد أدناه
            }

            return Result<AiQueryResultDto>.Success(new AiQueryResultDto(
                "لم أفهم السؤال بما يكفي. جرّب صيغة أوضح مثل: «قائمة الديون»، «مجموع الديون»، «أكبر دين»، «مبيعات أمس»، «سعر تفاح».",
                true,
                Array.Empty<string>(),
                sw.Elapsed,
                _assistant.ProviderName));
        }
    }
}
