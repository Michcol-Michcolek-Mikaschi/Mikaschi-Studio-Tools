using Narzedzia.Contracts;

namespace NarzedziaHost;

public class ModuleLoader
{
    private readonly List<IModule> _modules = new();

    public void RegisterModule(IModule module) => _modules.Add(module);
    public IReadOnlyList<IModule> GetModules() => _modules;
}
