using Avalonia.Controls;
using Narzedzia.Contracts;
using Modules.SpriteSheetCutter.Views;

namespace Modules.SpriteSheetCutter;

public class SpriteSheetCutterModule : IModule
{
    public string Name => "Sprite Sheet Cutter";
    public string Description => "Narzędzie do cięcia sprite sheet'ów";
    public string IconPath => "fa-solid fa-table-cells";

    public Control CreateMainView() => new SpriteSheetCutterView();
    public void OnLoad() { }
    public void OnUnload() { }
}
