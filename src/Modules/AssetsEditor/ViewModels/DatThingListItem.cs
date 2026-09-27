using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Narzedzia.Core.Models;

namespace Modules.AssetsEditor.ViewModels;

/// <summary>
/// Element listy dla starego formatu .dat — zachowany dla ewentualnej obsługi plików legacy.
/// </summary>
public sealed partial class DatThingListItem : ObservableObject
{
    [ObservableProperty] private Bitmap? _thumbnail;

    public DatThingType Thing { get; }

    public uint   Id          => Thing.Id;
    public string DisplayName => $"{Thing.Id}";

    public DatThingListItem(DatThingType thing) => Thing = thing;
}

