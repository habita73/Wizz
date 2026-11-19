namespace Wizz.Application.AI.Perception;

using Wizz.Application.Abstractions;

/// <summary>
/// يلفّ قارئ النص ببوابة الوحدة. لا يُنشأ القارئ الداخلي إلا عند أول استخدام والوحدة مفعّلة،
/// فلا يُحمَّل محرك التعرّف على الجهاز حين تكون الوحدة مطفأة. تغيير الحالة يسري من غير إعادة تشغيل.
/// </summary>
public sealed class GatedOcrTextReader : IOcrTextReader
{
    private readonly IModuleGate _gate;
    private readonly string _moduleId;
    private readonly Lazy<IOcrTextReader> _inner;
    private readonly string _disabledReason;

    public GatedOcrTextReader(
        IModuleGate gate,
        string moduleId,
        Func<IOcrTextReader> innerFactory,
        string? disabledReason = null)
    {
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(innerFactory);
        if (string.IsNullOrWhiteSpace(moduleId))
            throw new ArgumentException("معرّف الوحدة مطلوب", nameof(moduleId));

        _gate = gate;
        _moduleId = moduleId.Trim();
        _inner = new Lazy<IOcrTextReader>(innerFactory, LazyThreadSafetyMode.ExecutionAndPublication);
        _disabledReason = string.IsNullOrWhiteSpace(disabledReason)
            ? "وحدة قراءة الوثائق معطّلة. فعّلها من الإعدادات ← الوحدات الاختيارية."
            : disabledReason;
    }

    private bool Enabled => _gate.IsEnabled(_moduleId);

    public bool IsSupported => Enabled && _inner.Value.IsSupported;

    public Task<OcrReadResult> ReadAsync(byte[] imageBytes, CancellationToken ct = default)
        => Enabled
            ? _inner.Value.ReadAsync(imageBytes, ct)
            : Task.FromResult(OcrReadResult.Unavailable(_disabledReason));
}
