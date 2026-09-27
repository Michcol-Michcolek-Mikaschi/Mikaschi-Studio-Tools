using Avalonia.Controls;
using Modules.OldItemEditor.ViewModels;
using Modules.OldItemEditor.Views;
using Narzedzia.Contracts;

namespace Modules.OldItemEditor;

public sealed class OldItemEditorModule : IModule
{
    private readonly OldItemEditorViewModel _viewModel = new();
    private OldItemEditorView? _view;

    public string Name => "Old Item Editor";
    public string Description => "Klasyczny edytor items.otb dla klientów Tibia 8.00–10.98";
    public string IconPath => "fa-solid fa-list";

    public Control CreateMainView()
    {
        _view ??= new OldItemEditorView { DataContext = _viewModel };
        return _view;
    }

    public void OnLoad() { }
    public void OnUnload() => _viewModel.Dispose();
}
