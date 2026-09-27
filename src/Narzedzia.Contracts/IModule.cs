using Avalonia.Controls;

namespace Narzedzia.Contracts;

public interface IModule
{
    string Name { get; }
    string Description { get; }
    string IconPath { get; }
    Control CreateMainView();
    void OnLoad();
    void OnUnload();
}
