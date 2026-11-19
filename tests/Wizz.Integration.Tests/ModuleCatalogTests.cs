using System.Text.Json;
using Wizz.Application.Abstractions;
using Wizz.Application.Modules;
using Wizz.Application.UseCases.Modules;
using Wizz.Domain;
using Wizz.Infrastructure.Modules;

namespace Wizz.Integration.Tests;

/// <summary>
/// اختبارات ببيانات اصطناعية لآلية الوحدات الاختيارية: الكتالوج المحلي،
/// بوابة التشغيل، الحفظ المحلي، وسجل التدقيق. لا وحدة حقيقية منقولة بعد.
/// </summary>
public sealed class ModuleCatalogTests
{
    private static ModuleDescriptor Demo(string id = "demo.module", ModuleLicenseCapability license = ModuleLicenseCapability.None)
        => new(
            id: id,
            nameAr: "وحدة تجريبية",
            descriptionAr: "وصف اصطناعي للاختبار",
            group: ModuleGroup.Documents,
            version: "1.0",
            requirementsAr: new[] { "حزمة لغة" },
            licenseCapability: license);

    [Fact]
    public void EmptyCatalog_HasNoModules()
    {
        Assert.Empty(ModuleCatalog.Empty.All);
        Assert.Null(ModuleCatalog.Empty.Find("anything"));

        var gate = new ModuleGate(ModuleCatalog.Empty, new MemoryModuleStateStore());
        Assert.Empty(gate.GetAllStatuses());
        Assert.False(gate.IsEnabled("anything"));
    }

    [Fact]
    public void Catalog_RejectsDuplicateIds()
    {
        Assert.Throws<ArgumentException>(() => new ModuleCatalog(new[] { Demo(), Demo() }));
    }

    [Fact]
    public void Catalog_RejectsUnknownDependency()
    {
        var descriptor = new ModuleDescriptor(
            "child", "تابع", "وصف", ModuleGroup.Data, "1.0", dependsOn: new[] { "missing" });

        Assert.Throws<ArgumentException>(() => new ModuleCatalog(new[] { descriptor }));
    }

    [Fact]
    public void Descriptor_RejectsIdWithWhitespace()
    {
        Assert.Throws<ArgumentException>(() => Demo("not valid"));
    }

    [Fact]
    public void Gate_DefaultsToDisabled()
    {
        var gate = new ModuleGate(new ModuleCatalog(new[] { Demo() }), new MemoryModuleStateStore());

        var status = Assert.Single(gate.GetAllStatuses());

        Assert.False(status.Enabled);
        Assert.Equal(ModuleAvailability.Available, status.Availability);
        Assert.False(gate.IsEnabled("demo.module"));
    }

    [Fact]
    public void Gate_UsesStoredEnabledState()
    {
        var store = new MemoryModuleStateStore();
        store.SetEnabled("demo.module", true);
        var gate = new ModuleGate(new ModuleCatalog(new[] { Demo() }), store);

        Assert.True(gate.IsEnabled("demo.module"));
    }

    [Fact]
    public void Gate_BlocksEntitlementModuleEvenWhenStoreSaysEnabled()
    {
        var store = new MemoryModuleStateStore();
        store.SetEnabled("paid.module", true);
        var gate = new ModuleGate(new ModuleCatalog(new[] { Demo("paid.module", ModuleLicenseCapability.RequiresEntitlement) }), store);

        var status = Assert.Single(gate.GetAllStatuses());

        Assert.Equal(ModuleAvailability.Unavailable, status.Availability);
        Assert.False(status.Enabled);
        Assert.False(gate.IsEnabled("paid.module"));
        Assert.False(string.IsNullOrWhiteSpace(status.ReasonAr));
    }

    [Fact]
    public async Task SetModuleEnabled_RejectsUnknownModuleWithoutAudit()
    {
        var audit = new RecordingAuditLogger();
        var useCase = BuildUseCase(ModuleCatalog.Empty, new MemoryModuleStateStore(), audit);

        var result = await useCase.ExecuteAsync("unknown", true, Guid.NewGuid(), "tester");

        Assert.True(result.IsFailure);
        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task SetModuleEnabled_TogglesAndAuditsOnce()
    {
        var catalog = new ModuleCatalog(new[] { Demo() });
        var store = new MemoryModuleStateStore();
        var audit = new RecordingAuditLogger();
        var useCase = BuildUseCase(catalog, store, audit);
        var userId = Guid.NewGuid();

        var enabled = await useCase.ExecuteAsync("demo.module", true, userId, "tester");
        var repeated = await useCase.ExecuteAsync("demo.module", true, userId, "tester");
        var disabled = await useCase.ExecuteAsync("demo.module", false, userId, "tester");

        Assert.True(enabled.IsSuccess);
        Assert.True(repeated.IsSuccess);
        Assert.True(disabled.IsSuccess);
        Assert.Equal(new[] { AuditAction.ModuleEnabled, AuditAction.ModuleDisabled },
            audit.Entries.Select(entry => entry.Action));
        Assert.All(audit.Entries, entry =>
        {
            Assert.Equal(userId, entry.UserId);
            Assert.Equal("tester", entry.Username);
            Assert.Equal("Module", entry.EntityType);
        });
        Assert.False(store.IsEnabled("demo.module"));
    }

    [Fact]
    public void StateStore_RoundTripsAndKeepsOtherValues()
    {
        var path = TempPath();
        try
        {
            var first = new ModuleStateStore(path);
            Assert.False(first.IsEnabled("a"));
            first.SetEnabled("a", true);
            first.SetEnabled("b", false);

            var second = new ModuleStateStore(path);
            Assert.True(second.IsEnabled("a"));
            Assert.False(second.IsEnabled("b"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StateStore_IgnoresNonBooleanEntriesAndStartsDisabledOnCorruptFile()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new { a = "true", b = 5, c = true }));
            Assert.True(new ModuleStateStore(path).IsEnabled("c"));
            Assert.False(new ModuleStateStore(path).IsEnabled("a"));

            File.WriteAllText(path, "{not json");
            var store = new ModuleStateStore(path);
            Assert.Empty(store.GetAll());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static SetModuleEnabledUseCase BuildUseCase(
        ModuleCatalog catalog, IModuleStateStore store, IAuditLogger audit)
        => new(catalog, store, new ModuleGate(catalog, store), audit);

    private static string TempPath()
        => Path.Combine(Path.GetTempPath(), $"wizz-modules-{Guid.NewGuid():N}.json");

    private sealed class MemoryModuleStateStore : IModuleStateStore
    {
        private readonly Dictionary<string, bool> _states = new(StringComparer.Ordinal);

        public bool IsEnabled(string moduleId)
            => _states.TryGetValue(moduleId, out var enabled) && enabled;

        public IReadOnlyDictionary<string, bool> GetAll() => new Dictionary<string, bool>(_states);

        public void SetEnabled(string moduleId, bool enabled) => _states[moduleId] = enabled;
    }

    private sealed class RecordingAuditLogger : IAuditLogger
    {
        public List<AuditEntry> Entries { get; } = new();

        public Task LogAsync(AuditEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
