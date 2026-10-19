namespace Wizz.Application.AI;

using Wizz.Application.Abstractions;
using Wizz.Application.Common;
using Wizz.Application.DTOs;

/// <summary>استفسار AI مع سياق المتجر.</summary>
public sealed class QueryAiUseCase
{
    private readonly ILlmProvider _llm;
    private readonly IAiToolRegistry _tools;

    public QueryAiUseCase(ILlmProvider llm, IAiToolRegistry tools)
    {
        _llm = llm;
        _tools = tools;
    }

    public async Task<Result<AiQueryResultDto>> ExecuteAsync(AiQueryInputDto input, CancellationToken ct = default)
    {
        var available = await _llm.IsAvailableAsync(ct);
        if (!available)
            return Result<AiQueryResultDto>.Failure("موفر AI غير متاح حالياً");

        var start = DateTime.UtcNow;
        var systemPrompt = BuildSystemPrompt(input);
        var answer = await _llm.CompleteWithContextAsync(systemPrompt, input.Question, ct);
        var elapsed = DateTime.UtcNow - start;

        return Result<AiQueryResultDto>.Success(new AiQueryResultDto(
            answer,
            input.IncludeStoreContext || input.IncludeRecentSales,
            Array.Empty<string>(), // يمكن إضافة بيانات مستخدمة لاحقاً
            elapsed,
            _llm.ProviderName
        ));
    }

    private static string BuildSystemPrompt(AiQueryInputDto input)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("أنت مساعد ذكي لنظام نقاط بيع (Wizz).");
        sb.AppendLine("قواعد:");
        sb.AppendLine("- لا تخترع أرقاماً أو مبيعات أو أرصدة.");
        sb.AppendLine("- إذا كان السؤال غامضاً، اطلب توضيحاً.");
        sb.AppendLine("- استخدم الأدوات المتاحة للحصول على بيانات حقيقية.");
        sb.AppendLine("- رد بالعربية، مختصراً ومفيداً.");
        return sb.ToString();
    }
}