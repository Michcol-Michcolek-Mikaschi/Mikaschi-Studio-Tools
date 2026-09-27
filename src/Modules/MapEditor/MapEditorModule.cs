using Avalonia.Controls;
using Narzedzia.Contracts;
using Modules.MapEditor.Views;

namespace Modules.MapEditor;

public class MapEditorModule : IModule
{
    public string Name => "Map Editor";
    public string Description => "Edytor map OTBM";
    public string IconPath => "fa-solid fa-map";

    public Control CreateMainView() => new MapEditorView();
    public void OnLoad() { }
    public void OnUnload() { }
}
