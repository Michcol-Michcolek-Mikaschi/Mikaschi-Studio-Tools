using Avalonia.Controls;
using Modules.SpriteResizer.ViewModels;

namespace Modules.SpriteResizer.Views;

public partial class SpriteResizerView : UserControl
{
    public SpriteResizerView()
    {
        InitializeComponent();
        DataContext = new SpriteResizerViewModel();
    }
}
