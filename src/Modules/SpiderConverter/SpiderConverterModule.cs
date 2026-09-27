using Avalonia.Controls;
using Narzedzia.Contracts;
using Modules.SpiderConverter.Views;

namespace Modules.SpiderConverter;

public class SpiderConverterModule : IModule
{
    public string Name => "Converter";
    public string Description => "Konwerter formatów assetów Tibia";
    public string IconPath => "fa-solid fa-arrows-rotate";

    public Control CreateMainView() => new SpiderConverterView();
    public void OnLoad() { }
    public void OnUnload() { }
}
