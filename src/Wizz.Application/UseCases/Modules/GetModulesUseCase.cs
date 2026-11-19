namespace Wizz.Application.UseCases.Modules;

using Wizz.Application.Abstractions;
using Wizz.Application.Modules;

/// <summary>قراءة كتالوج الوحدات وحالتها للعرض في الإعدادات.</summary>
public sealed class GetModulesUseCase
{
    private readonly IModuleGate _gate;

    public GetModulesUseCase(IModuleGate gate) => _gate = gate;

    public IReadOnlyList<ModuleStatus> Execute() => _gate.GetAllStatuses();
}
