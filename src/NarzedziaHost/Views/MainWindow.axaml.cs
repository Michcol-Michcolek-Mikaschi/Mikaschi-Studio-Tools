using Avalonia.Controls;
using Narzedzia.Contracts.Localization;

namespace NarzedziaHost.Views;

public partial class MainWindow : Window
{
    private readonly LocalizationScope _localizationScope;

    public MainWindow()
    {
        InitializeComponent();
        _localizationScope = LocalizationScope.Attach(this);
        Closed += (_, _) => _localizationScope.Dispose();
    }
}
