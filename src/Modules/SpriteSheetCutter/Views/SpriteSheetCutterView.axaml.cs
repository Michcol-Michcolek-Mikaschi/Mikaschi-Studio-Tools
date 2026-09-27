using Avalonia.Controls;
using Modules.SpriteSheetCutter.ViewModels;

namespace Modules.SpriteSheetCutter.Views;

public partial class SpriteSheetCutterView : UserControl
{
    public SpriteSheetCutterView()
    {
        InitializeComponent();
        DataContext = new SpriteSheetCutterViewModel();
    }
}
