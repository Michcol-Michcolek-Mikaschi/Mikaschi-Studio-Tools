using Avalonia.Controls;
using Avalonia.Interactivity;
using Narzedzia.Contracts.Localization;

namespace Modules.ObjectBuilder.Views;

public partial class LegacyDeleteConfirmationWindow : Window
{
    private readonly LocalizationScope _localizationScope;

    public LegacyDeleteConfirmationWindow()
        : this(1, false)
    {
    }

    public LegacyDeleteConfirmationWindow(int selectedCount, bool willRenumberIds)
    {
        InitializeComponent();
        _localizationScope = LocalizationScope.Attach(this);
        Closed += (_, _) => _localizationScope.Dispose();
        SelectionSummaryText.Text = selectedCount == 1
            ? "Zostanie usunięty 1 zaznaczony obiekt."
            : $"Zostaną usunięte zaznaczone obiekty: {selectedCount}.";
        RenumberWarningBorder.IsVisible = willRenumberIds;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);
}
