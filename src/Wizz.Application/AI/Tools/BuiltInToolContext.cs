namespace Wizz.Application.AI.Tools;

/// <summary>
/// السياق المُمرَّر لكل أداة مدمجة: السؤال بعد التطبيع + لقطة بيانات المتجر.
/// الأدوات التي تحتاج بيانات المتجر تقرأها من هنا، والتي لا تحتاج تتجاهلها.
/// </summary>
public sealed class BuiltInToolContext
{
    /// <summary>السؤال بعد التطبيع (توحيد الهمزات/الأرقام/اللاحقات).</summary>
    public required string Question { get; init; }

    /// <summary>السؤال الأصلي كما كتبه المستخدم (للأدوات التي تحتاج النص الخام).</summary>
    public required string RawQuestion { get; init; }

    /// <summary>مُهيّئ كسول للقطة بيانات المتجر — لا يُبنى إلا إذا احاجته أداة.</summary>
    public required Func<CancellationToken, Task<StoreSnapshot>> SnapshotFactory { get; init; }

    private StoreSnapshot? _snapshot;

    /// <summary>يعيد لقطة بيانات المتجر (تُبنى مرة واحدة وتُخزَّن مؤقتاً).</summary>
    public async Task<StoreSnapshot> GetSnapshotAsync(CancellationToken ct = default)
        => _snapshot ??= await SnapshotFactory(ct);
}
