using Avalonia.Controls;
using Narzedzia.Contracts;
using Modules.SpriteResizer.Views;

namespace Modules.SpriteResizer;

public class SpriteResizerModule : IModule
{
    public string Name => "Sprite Resizer";
    public string Description => "Narzędzie do skalowania sprite'ów";
    public string IconPath => "fa-solid fa-up-right-and-down-left-from-center";

    public Control CreateMainView() => new SpriteResizerView();
    public void OnLoad() { }
    public void OnUnload() { }
}
