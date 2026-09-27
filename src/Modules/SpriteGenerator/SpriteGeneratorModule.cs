using Avalonia.Controls;
using Modules.SpriteGenerator.Views;
using Narzedzia.Contracts;

namespace Modules.SpriteGenerator;

public class SpriteGeneratorModule : IModule
{
    public string Name => "Generator Sprite'ów AI";
    public string Description => "AI sprite/animation generator (ComfyUI) — Outfit/Item/Effect/Missile";
    public string IconPath => "fa-solid fa-wand-magic-sparkles";

    public Control CreateMainView() => new SpriteGeneratorView();
    public void OnLoad() { }
    public void OnUnload() { }
}
