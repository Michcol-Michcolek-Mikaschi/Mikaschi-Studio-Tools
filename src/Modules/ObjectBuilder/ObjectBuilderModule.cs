using Avalonia.Controls;
using Narzedzia.Contracts;
using Modules.ObjectBuilder.Views;
using Modules.ObjectBuilder.ViewModels;

namespace Modules.ObjectBuilder;

public class ObjectBuilderModule : IModule
{
    private readonly ObjectBuilderViewModel _viewModel = new();
    private ObjectBuilderView? _view;

    public string Name => "Old Assets Editor";
    public string Description => "Wizualny edytor obiektów sprite";
    public string IconPath => "fa-solid fa-cube";

    public Control CreateMainView()
    {
        _view ??= new ObjectBuilderView
        {
            DataContext = _viewModel
        };

        return _view;
    }
    public void OnLoad() { }
    public void OnUnload() => _viewModel.StopAnimation();
}
