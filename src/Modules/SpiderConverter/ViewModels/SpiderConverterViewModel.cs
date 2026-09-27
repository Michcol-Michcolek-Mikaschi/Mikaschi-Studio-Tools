using CommunityToolkit.Mvvm.ComponentModel;

namespace Modules.SpiderConverter.ViewModels;

public partial class SpiderConverterViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Converter";
}
