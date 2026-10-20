namespace Wizz.Application.AI;

using System.Diagnostics;
using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;

/// <summary>
/// استفسار AI مع سياق المتجر.
/// الاستراتيجية: المساعد المدمج (فوري، بلا نموذج) هو الأساس؛ ويُستخدم النموذج المحلي
/// فقط إذا كان متاحاً، مع رجوع تلقائي للمدمج عند أي فشل أو مهلة.
/// </summary>
public sealed class QueryAiUseCase
{
    private readonly ILlmProvider _llm;
    private readonly IAiToolRegistry _tools;
    private readonly BuiltInAssistant _assistant;

    public QueryAiUseCase(ILlmProvider llm, IAiToolRegistry tools, BuiltInAssistant assistant)
    {
        _llm = llm;
        _tools = tools;
        _assistant = assistant;
    }

    public async Task<Result<AiQueryResultDto>> ExecuteAsync(AiQueryInputDto input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Question))
            return Result<AiQueryResultDto>.Failure("اكتب سؤالاً أولاً.");

        var sw = Stopwatch.StartNew();

        // 1) المساعد المدمج أولاً: إجابة فورية من البيانات المحلية بلا نموذج ولا انتظار.
        try
        {
            var builtIn = await _assistant.TryAnswerAsync(input.Question, ct);
            if (builtIn is not null)
            {
                sw.Stop();
                return Result<AiQueryResultDto>.Success(new AiQueryResultDto(
                    builtIn,
                    true,
                    new[] { "بيانات محلية" },
                    sw.Elapsed,
                    _assistant.ProviderName));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Built-in assistant failed: {ex.Message}");
        }

        // 2) سؤال حر → النموذج المحلي إن كان متاحاً، مع سياق حقيقي من البيانات.
        if (await SafeIsAvailableAsync(ct))
        {
            try
            {
                var snapshot = await _assistant.GetSnapshotAsync(ct);
                var systemPrompt = BuildSystemPrompt(input, snapshot);
                var answer = await _llm.CompleteWithContextAsync(systemPrompt, input.Question, ct);

                if (!string.IsNullOrWhiteSpace(answer))
                {
                    sw.Stop();
                    return Result<AiQueryResultDto>.Success(new AiQueryResultDto(
                        answer.Trim(),
                        true,
                        new[] { "مبيعات اليوم", "مبيعات الأسبوع", "الديون", "المنتجات" },
                        sw.Elapsed,
                        _llm.ProviderName));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return Result<AiQueryResultDto>.Failure("أُلغي الطلب.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AI fallback: {ex.Message}");
            }
        }

        // 3) لا تطابق ولا نموذج → إرشاد واضح بدل رسالة خطأ صامتة.
        sw.Stop();
        var hint = "لم أجد إجابة مباشرة لهذا السؤال.\n\n" +
                   "المساعد المدمج يجيب فوراً عن: مبيعات اليوم، ملخص الأسبوع، أفضل المنتجات، الديون المستحقة، عدد المنتجات، طريقة إضافة منتج، والنسخ الاحتياطي.\n\n";

        hint += await SafeIsAvailableAsync(ct)
            ? "أعد صياغة السؤال، أو جرّب نموذجاً أخف من إعدادات النموذج المحلي."
            : "لتشغيل الأسئلة الحرة: شغّل Ollama ثم ثبّت نموذجاً خفيفاً مثل llama3.2:1b.";

        return Result<AiQueryResultDto>.Success(new AiQueryResultDto(
            hint,
            true,
            Array.Empty<string>(),
            sw.Elapsed,
            _assistant.ProviderName));
    }

    private async Task<bool> SafeIsAvailableAsync(CancellationToken ct)
    {
        try
        {
            return await _llm.IsAvailableAsync(ct);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildSystemPrompt(AiQueryInputDto input, StoreSnapshot snapshot)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("أنت مساعد ذكي لنظام نقاط بيع جزائري اسمه Wizz، يعمل محلياً.");
        sb.AppendLine("قواعد صارمة:");
        sb.AppendLine("- استخدم فقط الأرقام الواردة في «بيانات المتجر» أدناه، ولا تخترع أي رقم.");
        sb.AppendLine("- إن لم يكن الرقم موجوداً في البيانات، قل بوضوح إنه غير متوفر ووجّه المستخدم للصفحة المناسبة.");
        sb.AppendLine("- لا تقترح عمليات حساسة (حذف، إلغاء بيع، تعديل أسعار) دون تنبيه المستخدم.");
        sb.AppendLine("- رد بالعربية الفصحى المبسطة، في 3 أسطر كحد أقصى، دون مقدمات طويلة.");
        sb.AppendLine();
        sb.AppendLine(BuiltInAssistant.BuildContextText(snapshot));
        return sb.ToString();
    }
}
