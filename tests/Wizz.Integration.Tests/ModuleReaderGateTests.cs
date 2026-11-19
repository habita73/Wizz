using Wizz.Application.Abstractions;
using Wizz.Application.AI.Perception;
using Wizz.Application.Modules;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية لبوابة قارئ الوثائق: الوحدة المطفأة لا تحمّل المحرك،
/// والتبديل بين القرّاء يتم عند غياب القدرة لا عند فشل القراءة. لا صور حقيقية ولا نصوص وصول.
/// </summary>
public sealed class ModuleReaderGateTests
{
    [Fact]
    public void Catalog_ContainsReaderModule_AndGateDefaultsToDisabled()
    {
        var store = new MemoryModuleStateStore();
        var gate = new ModuleGate(new ModuleCatalog(new[] { DocumentReaderModule.Descriptor }), store);

        var status = Assert.Single(gate.GetAllStatuses());

        Assert.Equal(DocumentReaderModule.Id, status.Descriptor.Id);
        Assert.Equal(ModuleGroup.Documents, status.Descriptor.Group);
        Assert.Equal(ModuleAvailability.Available, status.Availability);
        Assert.False(status.Enabled);
        Assert.False(gate.IsEnabled(DocumentReaderModule.Id));
    }

    [Fact]
    public async Task GatedReader_WhenDisabled_DoesNotCreateInnerReader()
    {
        var creations = 0;
        var reader = new GatedOcrTextReader(
            new FixedGate(false),
            DocumentReaderModule.Id,
            () =>
            {
                creations++;
                return new FakeReader(true, OcrReadResult.Success("نص اصطناعي", "ar"));
            });

        Assert.False(reader.IsSupported);
        var result = await reader.ReadAsync(new byte[] { 1, 2, 3 });

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Text);
        Assert.False(result.UnavailableReason!.Contains("نص اصطناعي", StringComparison.Ordinal));
        Assert.Equal(0, creations);
    }

    [Fact]
    public async Task GatedReader_WhenEnabled_DelegatesOnceInnerIsCreated()
    {
        var creations = 0;
        var inner = new FakeReader(true, OcrReadResult.Success("نص اصطناعي", "ar"));
        var reader = new GatedOcrTextReader(
            new FixedGate(true),
            DocumentReaderModule.Id,
            () =>
            {
                creations++;
                return inner;
            });

        Assert.True(reader.IsSupported);
        var first = await reader.ReadAsync(new byte[] { 1 });
        var second = await reader.ReadAsync(new byte[] { 2 });

        Assert.True(first.IsAvailable);
        Assert.Equal("نص اصطناعي", second.Text);
        Assert.Equal(1, creations);
        Assert.Equal(2, inner.Calls);
    }

    [Fact]
    public async Task GatedReader_ReflectsGateChangeWithoutRecreation()
    {
        var gate = new MutableGate(false);
        var reader = new GatedOcrTextReader(gate, DocumentReaderModule.Id, () => new FakeReader(true, OcrReadResult.Success("نص", "ar")));

        Assert.False((await reader.ReadAsync(new byte[] { 1 })).IsAvailable);

        gate.Enabled = true;
        Assert.True((await reader.ReadAsync(new byte[] { 1 })).IsAvailable);
    }

    [Fact]
    public async Task Composite_UsesPrimaryWhenItCanRead_AndNeverFallsBackOnFailure()
    {
        var primary = new FakeReader(true, OcrReadResult.Unavailable("فشل قراءة اصطناعي"));
        var fallback = new FakeReader(true, OcrReadResult.Success("من البديل", "ar"));
        var composite = new CompositeOcrTextReader(primary, fallback);

        var result = await composite.ReadAsync(new byte[] { 1 });

        Assert.False(result.IsAvailable);
        Assert.Equal(0, fallback.Calls);
        Assert.Equal(1, primary.Calls);
    }

    [Fact]
    public async Task Composite_UsesFallbackOnlyWhenPrimaryIsNotSupported()
    {
        var primary = new FakeReader(false, OcrReadResult.Unavailable("غير متاح"));
        var fallback = new FakeReader(true, OcrReadResult.Success("من البديل", "ar"));
        var composite = new CompositeOcrTextReader(primary, fallback);

        Assert.True(composite.IsSupported);
        var result = await composite.ReadAsync(new byte[] { 1 });

        Assert.True(result.IsAvailable);
        Assert.Equal("من البديل", result.Text);
        Assert.Equal(0, primary.Calls);
        Assert.Equal(1, fallback.Calls);
    }

    [Fact]
    public async Task Composite_ReportsUnavailable_WhenNoReaderIsSupported()
    {
        var composite = new CompositeOcrTextReader(
            new FakeReader(false, OcrReadResult.Unavailable("غير متاح")),
            new FakeReader(false, OcrReadResult.Unavailable("غير متاح")));

        Assert.False(composite.IsSupported);
        var result = await composite.ReadAsync(new byte[] { 1 });

        Assert.False(result.IsAvailable);
        Assert.Empty(result.Text);
        Assert.False(string.IsNullOrWhiteSpace(result.UnavailableReason));
    }

    private sealed class FakeReader : IOcrTextReader
    {
        private readonly OcrReadResult _result;

        public FakeReader(bool supported, OcrReadResult result)
        {
            IsSupported = supported;
            _result = result;
        }

        public bool IsSupported { get; }
        public int Calls { get; private set; }

        public Task<OcrReadResult> ReadAsync(byte[] imageBytes, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(_result);
        }
    }

    private sealed class FixedGate : IModuleGate
    {
        private readonly bool _enabled;
        public FixedGate(bool enabled) => _enabled = enabled;

        public bool IsEnabled(string moduleId) => _enabled;
        public ModuleStatus? GetStatus(string moduleId) => null;
        public IReadOnlyList<ModuleStatus> GetAllStatuses() => Array.Empty<ModuleStatus>();
    }

    private sealed class MutableGate : IModuleGate
    {
        public MutableGate(bool enabled) => Enabled = enabled;

        public bool Enabled { get; set; }

        public bool IsEnabled(string moduleId) => Enabled;
        public ModuleStatus? GetStatus(string moduleId) => null;
        public IReadOnlyList<ModuleStatus> GetAllStatuses() => Array.Empty<ModuleStatus>();
    }

    private sealed class MemoryModuleStateStore : IModuleStateStore
    {
        private readonly Dictionary<string, bool> _states = new(StringComparer.Ordinal);

        public bool IsEnabled(string moduleId) => _states.TryGetValue(moduleId, out var value) && value;
        public IReadOnlyDictionary<string, bool> GetAll() => new Dictionary<string, bool>(_states);
        public void SetEnabled(string moduleId, bool enabled) => _states[moduleId] = enabled;
    }
}
