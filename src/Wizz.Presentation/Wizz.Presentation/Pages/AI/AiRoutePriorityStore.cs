namespace Wizz.Presentation.Pages.AI;

using Microsoft.JSInterop;
using Wizz.Application.Abstractions;
using Wizz.Application.AI.Routing;

/// <summary>
/// جسر بين أولوية المسارات (منطق نقي في Wizz.Application) و<c>localStorage</c> في المتصفح.
/// يحمّل الترتيب عند بدء الجلسة ويحفظه فور كل تعديل.
/// </summary>
public sealed class AiRoutePriorityStore
{
    private readonly IJSRuntime _js;
    private readonly AutoRoutePriority _priority;
    private bool _loaded;

    public AiRoutePriorityStore(IJSRuntime js, AutoRoutePriority priority)
    {
        _js = js;
        _priority = priority;
    }

    /// <summary>كائن الأولوية المشترك مع الموجّه في نفس جلسة Blazor.</summary>
    public AutoRoutePriority Priority => _priority;

    /// <summary>هل حُمّل الترتيب المحفوظ؟</summary>
    public bool IsLoaded => _loaded;

    /// <summary>يحمّل الترتيب من localStorage (مرة واحدة لكل جلسة).</summary>
    public async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        try
        {
            var order = await _js.InvokeAsync<string[]>("wizzAiRoutePriority.load");
            if (order is { Length: > 0 })
            {
                _priority.SetOrder(order.Select(ParseRoute).Where(r => r is not null).Select(r => r!.Value));
            }
        }
        catch
        {
            // SSR أو غياب JS — نكمل بالترتيب الافتراضي.
        }

        _loaded = true;
    }

    /// <summary>يحفظ الترتيب الحالي في localStorage.</summary>
    public async Task SaveAsync()
    {
        try
        {
            // ملاحظة: يجب لفّ المصفوفة في object[] وإلا فكّكها params إلى وسائط منفصلة.
            await _js.InvokeVoidAsync(
                "wizzAiRoutePriority.save",
                new object?[] { _priority.Order.Select(r => r.ToString()).ToArray() });
        }
        catch
        {
            // فشل الحفظ لا يُعطّل الإعدادات.
        }
    }

    /// <summary>يعيّن ترتيباً جديداً ويحفظه فوراً.</summary>
    public async Task SetOrderAsync(IEnumerable<RouteType> order)
    {
        _priority.SetOrder(order);
        await SaveAsync();
    }

    /// <summary>يعيد الترتيب الافتراضي ويمسحه من localStorage.</summary>
    public async Task ResetAsync()
    {
        _priority.SetOrder(AutoRoutePriority.AllRoutes);

        try
        {
            await _js.InvokeVoidAsync("wizzAiRoutePriority.clear");
        }
        catch
        {
            // تجاهل.
        }
    }

    private static RouteType? ParseRoute(string? value)
        => Enum.TryParse<RouteType>(value, ignoreCase: true, out var route) ? route : null;
}
