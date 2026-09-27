using Avalonia.Controls;
using Modules.AssetsEditor.Services;
using Modules.AssetsEditor.ViewModels;
using Modules.AssetsEditor.Views;
using Narzedzia.Contracts;

namespace Modules.AssetsEditor;

public sealed class AssetsEditorModule : IModule
{
    private readonly IAssetsService _assetsService = new AssetsService();
    private readonly AssetsEditorViewModel _viewModel;
    private AssetsEditorView? _view;

    public AssetsEditorModule()
    {
        _viewModel = new AssetsEditorViewModel(_assetsService);
    }

    public string Name => "Edytor Assetów";
    public string Description => "Edycja assetów Tibii 12+ (appearances.dat Protobuf, catalog-content.json i arkusze sprite'ów LZMA): przedmioty, stroje, efekty, pociski, flagi i podgląd tekstur.";
    public string IconPath => "fa-solid fa-cubes";

    public Control CreateMainView()
    {
        _view ??= new AssetsEditorView
        {
            DataContext = _viewModel
        };

        return _view;
    }

    public void OnLoad()
    {
    }

    public void OnUnload()
    {
        _assetsService.Dispose();
    }
}
