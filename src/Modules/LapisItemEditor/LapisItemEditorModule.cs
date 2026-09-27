using Avalonia.Controls;
using Modules.LapisItemEditor.ViewModels;
using Narzedzia.Contracts;
using Modules.LapisItemEditor.Views;

namespace Modules.LapisItemEditor;

public class LapisItemEditorModule : IModule
{
    private readonly LapisItemEditorViewModel _viewModel = new();
    private LapisItemEditorView? _view;

    public string Name => "Item Editor";
    public string Description => "Edytor items.otb i appearances.dat";
    public string IconPath => "fa-solid fa-list-check";

    public Control CreateMainView()
    {
        _view ??= new LapisItemEditorView
        {
            DataContext = _viewModel
        };

        return _view;
    }
    public void OnLoad() { }
    public void OnUnload() { }
}
