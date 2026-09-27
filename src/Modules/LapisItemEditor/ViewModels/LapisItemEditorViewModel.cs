using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modules.LapisItemEditor.Services;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.LapisItemEditor.ViewModels;

public partial class LapisItemEditorViewModel : ObservableObject
{
    private readonly OtbDocumentService _documents;
    private readonly LapisItemSearchService _search;
    private readonly LapisItemMutationService _mutation;
    private readonly OtbCompareService _compare;
    private readonly LapisAssetService _assets;
    private OtbFile? _document;
    private Dictionary<ushort, ItemXmlEntry> _itemsXmlLookup = new();
    private bool _isLoadingSelection;

    [ObservableProperty] private string _title = "Item Editor";
    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _itemsXmlPath = string.Empty;
    [ObservableProperty] private string _itemsXmlSummary = "items.xml nie wczytany.";
    [ObservableProperty] private string _assetsFolderPath = string.Empty;
    [ObservableProperty] private string _filter = string.Empty;
    [ObservableProperty] private string _statusText = "Utwórz lub otwórz items.otb. Opcjonalnie wczytaj folder assets dla miniatur.";
    [ObservableProperty] private string _headerSummary = "Brak dokumentu.";
    [ObservableProperty] private string _assetsSummary = "Assets nie wczytane.";
    [ObservableProperty] private string _compareSummary = "Brak porównania.";
    [ObservableProperty] private LapisItemRow? _selectedItem;
    [ObservableProperty] private int _selectedServerId;
    [ObservableProperty] private int _selectedClientId;
    [ObservableProperty] private string _selectedName = string.Empty;
    [ObservableProperty] private int _selectedTypeIndex;
    [ObservableProperty] private int _selectedSpeed;

    [ObservableProperty] private IReadOnlyList<LapisItemRow> _items = Array.Empty<LapisItemRow>();
    public ObservableCollection<LapisItemFlagState> Flags { get; } = new();
    public ObservableCollection<OtbComparisonEntry> ComparisonEntries { get; } = new();

    public IReadOnlyList<string> SelectedTypeLabels { get; } =
        Enum.GetNames<OtbItemType>();

    public LapisItemEditorViewModel()
        : this(new OtbDocumentService(), new LapisItemSearchService(), new LapisItemMutationService(), new OtbCompareService(), new LapisAssetService())
    {
    }

    public LapisItemEditorViewModel(
        OtbDocumentService documents,
        LapisItemSearchService search,
        LapisItemMutationService mutation,
        OtbCompareService compare,
        LapisAssetService assets)
    {
        _documents = documents;
        _search = search;
        _mutation = mutation;
        _compare = compare;
        _assets = assets;
        ResetFlagStates();
    }

    public void OpenItemsXml(string xmlPath)
    {
        try
        {
            var entries = ItemsXmlReader.Read(xmlPath);
            _itemsXmlLookup = ItemsXmlReader.BuildLookup(entries);
            ItemsXmlPath = xmlPath;
            ItemsXmlSummary = $"Wczytano items.xml: {entries.Count} wpisów, {_itemsXmlLookup.Count} ID po rozwinięciu zakresów.";
            StatusText = ItemsXmlSummary;
            RefreshXmlMetadata();
        }
        catch (Exception ex)
        {
            ItemsXmlSummary = $"Błąd items.xml: {ex.Message}";
            StatusText = ItemsXmlSummary;
        }
    }

    private void RefreshXmlMetadata()
    {
        if (_itemsXmlLookup.Count == 0) return;
        foreach (var row in Items)
        {
            if (_itemsXmlLookup.TryGetValue(row.Source.ServerId, out var entry))
            {
                row.ApplyXmlEntry(entry);
            }
            else
            {
                row.ApplyXmlEntry(null);
            }
        }
    }

    public void OpenAssetsFolder(string folderPath)
    {
        try
        {
            var report = _assets.LoadFromFolder(folderPath);
            AssetsFolderPath = folderPath;
            AssetsSummary =
                $"Wczytano assets: {report.Objects} przedm., {report.Outfits} strojów, " +
                $"{report.Effects} efektów, {report.Missiles} pocisków, " +
                $"{report.SpriteSheets} arkuszy sprite.";
            StatusText = AssetsSummary;
            RefreshThumbnails();
        }
        catch (Exception ex)
        {
            AssetsSummary = $"Błąd wczytywania assets: {ex.Message}";
            StatusText = AssetsSummary;
        }
    }

    private void RefreshThumbnails()
    {
        if (!_assets.IsLoaded) return;
        // Lazy load: każdy LapisItemRow ma swój Func<uint, Bitmap?> provider; ustawiamy
        // go ponownie i wymuszamy invalidate przez Refresh, by DataGrid odświeżył widoczne wiersze.
        foreach (var row in Items)
        {
            row.ResetThumbnail(_assets.GetThumbnail);
        }
    }

    public void OpenFile(string path)
    {
        _document = _documents.Load(path);
        FilePath = path;
        Filter = string.Empty;
        RefreshItems();
        StatusText = $"Wczytano {Path.GetFileName(path)}.";
    }

    /// <summary>
    /// Async wczytanie OTB w wątku tła — nie blokuje UI dla dużych plików (60k+ itemów).
    /// </summary>
    public async Task OpenFileAsync(string path)
    {
        StatusText = $"Wczytywanie {Path.GetFileName(path)}...";
        var document = await Task.Run(() => _documents.Load(path)).ConfigureAwait(true);
        _document = document;
        FilePath = path;
        Filter = string.Empty;
        RefreshItems();
        StatusText = $"Wczytano {Path.GetFileName(path)} ({document.Items.Count} itemów).";
    }

    /// <summary>
    /// Wczytuje 3 zasoby równolegle: items.otb + items.xml + folder assets Tibia 12+.
    /// Wszystkie wykonują się w tle. Po zakończeniu lista itemów jest budowana ze
    /// wszystkimi metadanymi (XML name/article/attributes + thumbnail z Appearance).
    /// </summary>
    public async Task LoadAllAsync(string? otbPath, string? xmlPath, string? assetsPath)
    {
        StatusText = "Wczytywanie zasobów...";
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var otbTask = !string.IsNullOrWhiteSpace(otbPath) && File.Exists(otbPath)
            ? Task.Run(() => _documents.Load(otbPath))
            : Task.FromResult<OtbFile?>(null)!;

        var xmlTask = !string.IsNullOrWhiteSpace(xmlPath) && File.Exists(xmlPath)
            ? Task.Run(() =>
            {
                var entries = ItemsXmlReader.Read(xmlPath);
                return (entries.Count, ItemsXmlReader.BuildLookup(entries));
            })
            : Task.FromResult((0, new Dictionary<ushort, ItemXmlEntry>()));

        Task<LapisAssetService.LoadReport?> assetsTask =
            !string.IsNullOrWhiteSpace(assetsPath) && Directory.Exists(assetsPath)
            ? Task.Run<LapisAssetService.LoadReport?>(() => _assets.LoadFromFolder(assetsPath))
            : Task.FromResult<LapisAssetService.LoadReport?>(null);

        try
        {
            await Task.WhenAll(otbTask, xmlTask, assetsTask).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd wczytywania zasobów: {ex.Message}";
            return;
        }

        // OTB
        if (otbTask.Result is { } loadedOtb)
        {
            _document = loadedOtb;
            FilePath = otbPath!;
        }

        // items.xml
        var (xmlCount, xmlLookup) = xmlTask.Result;
        _itemsXmlLookup = xmlLookup;
        if (xmlCount > 0)
        {
            ItemsXmlPath = xmlPath!;
            ItemsXmlSummary = $"Wczytano items.xml: {xmlCount} wpisów, {xmlLookup.Count} ID po rozwinięciu zakresów.";
        }

        // assets
        if (assetsTask.Result is { } assetsReport)
        {
            AssetsFolderPath = assetsPath!;
            AssetsSummary =
                $"Wczytano assets: {assetsReport.Objects} przedm., {assetsReport.Outfits} strojów, " +
                $"{assetsReport.Effects} efektów, {assetsReport.Missiles} pocisków, " +
                $"{assetsReport.SpriteSheets} arkuszy sprite.";
        }

        Filter = string.Empty;
        await RefreshItemsAsync().ConfigureAwait(true);

        stopwatch.Stop();
        var pieces = new List<string>(3);
        if (_document is not null) pieces.Add($"OTB={_document.Items.Count}");
        if (xmlCount > 0)          pieces.Add($"XML={xmlCount}");
        if (assetsTask.Result is { } r) pieces.Add($"assets ok ({r.SpriteSheets} arkuszy)");

        var matched = _document is null || !_assets.IsLoaded ? 0
            : _document.Items.Count(i => i.ClientId != 0 && _assets.GetAppearanceByClientId(i.ClientId) is not null);
        if (_assets.IsLoaded && _document is not null)
        {
            pieces.Add($"CID→appearance: {matched}/{_document.Items.Count(i => i.ClientId != 0)}");
        }

        StatusText = pieces.Count == 0
            ? "Brak wczytanych zasobów — sprawdź ścieżki."
            : $"Gotowe w {stopwatch.ElapsedMilliseconds} ms: " + string.Join(", ", pieces) + ".";
    }

    [RelayCommand]
    private async Task LoadAll()
    {
        await LoadAllAsync(FilePath, ItemsXmlPath, AssetsFolderPath).ConfigureAwait(true);
    }

    public void SaveFile()
    {
        if (_document is null)
        {
            StatusText = "Brak dokumentu do zapisu.";
            return;
        }

        if (string.IsNullOrWhiteSpace(FilePath))
        {
            StatusText = "Podaj ścieżkę zapisu items.otb.";
            return;
        }

        ApplySelectedFields();
        _documents.Save(_document, FilePath);
        StatusText = $"Zapisano {Path.GetFileName(FilePath)}.";
    }

    public void SaveFileAs(string path)
    {
        FilePath = path;
        SaveFile();
    }

    public void CompareWithFile(string path)
    {
        if (_document is null)
        {
            StatusText = "Najpierw otwórz bazowy plik OTB.";
            return;
        }

        var other = _documents.Load(path);
        var result = _compare.Compare(_document, other);
        ComparisonEntries.Clear();
        foreach (var entry in result.Entries)
        {
            ComparisonEntries.Add(entry);
        }

        CompareSummary = result.Summary;
        StatusText = $"Porównano z {Path.GetFileName(path)}.";
    }

    [RelayCommand]
    private void NewDocument()
    {
        _document = _documents.CreateEmpty();
        FilePath = string.Empty;
        Filter = string.Empty;
        ComparisonEntries.Clear();
        CompareSummary = "Brak porównania.";
        RefreshItems();
        StatusText = "Utworzono pusty dokument OTB.";
    }

    [RelayCommand]
    private async Task LoadFromPath()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            StatusText = "Podaj ścieżkę do items.otb albo użyj przycisku Otwórz.";
            return;
        }

        await OpenFileAsync(FilePath).ConfigureAwait(true);
    }

    [RelayCommand]
    private void Save() => SaveFile();

    [RelayCommand]
    private void AddEmptyItem()
    {
        EnsureDocument();
        if (_document is null)
        {
            return;
        }

        var nextServerId = _document.Items.Count == 0
            ? (ushort)100
            : checked((ushort)(_document.Items.Max(item => item.ServerId) + 1));
        var item = new OtbItem
        {
            ServerId = nextServerId,
            ClientId = 0,
            Name = $"Item {nextServerId}",
            ItemType = OtbItemType.None
        };

        _document.Items.Add(item);
        RefreshItems(item);
        StatusText = $"Dodano item #{nextServerId}.";
    }

    [RelayCommand]
    private void DuplicateSelected()
    {
        if (_document is null || SelectedItem is null)
        {
            StatusText = "Wybierz item do duplikowania.";
            return;
        }

        var duplicate = _mutation.Duplicate(_document, SelectedItem.Source);
        RefreshItems(duplicate);
        StatusText = $"Zduplikowano item #{SelectedItem.ServerId}.";
    }

    [RelayCommand]
    private void ApplySelectedFields()
    {
        if (SelectedItem is null)
        {
            return;
        }

        var item = SelectedItem.Source;
        item.ServerId = ClampUshort(SelectedServerId);
        item.ClientId = ClampUshort(SelectedClientId);
        item.Name = SelectedName.Trim();
        item.ItemType = (OtbItemType)Math.Clamp(SelectedTypeIndex, 0, SelectedTypeLabels.Count - 1);
        item.Speed = ClampUshort(SelectedSpeed);
        SelectedItem.Refresh();
        RefreshHeaderSummary();
        StatusText = $"Zaktualizowano item #{item.ServerId}.";
    }

    [RelayCommand]
    private void ClearFilter()
    {
        Filter = string.Empty;
    }

    partial void OnFilterChanged(string value)
    {
        RefreshItems(SelectedItem?.Source);
    }

    partial void OnSelectedItemChanged(LapisItemRow? value)
    {
        LoadSelectedItem(value);
    }

    private void EnsureDocument()
    {
        if (_document is null)
        {
            NewDocument();
        }
    }

    private void RefreshItems(OtbItem? preferredSelection = null)
    {
        // Wersja synchroniczna jest używana przez polecenia, które natychmiast po
        // odświeżeniu pracują na SelectedItem (nowy, duplikuj, filtr). Nie może
        // uruchamiać zadania fire-and-forget, bo powodowałoby wyścig w UI.
        if (_document is null)
        {
            Items = Array.Empty<LapisItemRow>();
            HeaderSummary = "Brak dokumentu.";
            SelectedItem = null;
            return;
        }

        Func<uint, Avalonia.Media.Imaging.Bitmap?>? thumbProvider =
            _assets.IsLoaded ? _assets.GetThumbnail : null;
        Func<uint, Narzedzia.Core.Tibia12.Appearance?>? appearanceProvider =
            _assets.IsLoaded ? _assets.GetAppearanceByClientId : null;
        var built = new List<LapisItemRow>(_document.Items.Count);
        foreach (var item in _search.Filter(_document.Items, Filter))
        {
            var row = new LapisItemRow(item, thumbProvider);
            if (_itemsXmlLookup.TryGetValue(item.ServerId, out var xmlEntry))
            {
                row.ApplyXmlEntry(xmlEntry);
            }

            if (appearanceProvider is not null)
            {
                row.ApplyAppearance(appearanceProvider(item.ClientId));
            }

            built.Add(row);
        }

        Items = built;
        SelectedItem = preferredSelection is not null
            ? built.FirstOrDefault(row => ReferenceEquals(row.Source, preferredSelection)) ?? built.FirstOrDefault()
            : built.FirstOrDefault();
        RefreshHeaderSummary();
    }

    private async Task RefreshItemsAsync(OtbItem? preferredSelection = null)
    {
        if (_document is null)
        {
            Items = Array.Empty<LapisItemRow>();
            HeaderSummary = "Brak dokumentu.";
            SelectedItem = null;
            return;
        }

        var filter = Filter;
        var xmlLookup = _itemsXmlLookup;
        var assetsLoaded = _assets.IsLoaded;
        Func<uint, Avalonia.Media.Imaging.Bitmap?>? thumbProvider =
            assetsLoaded ? _assets.GetThumbnail : null;
        Func<uint, Narzedzia.Core.Tibia12.Appearance?>? appearanceProvider =
            assetsLoaded ? _assets.GetAppearanceByClientId : null;
        var sourceItems = _document.Items;
        var searchService = _search;

        // Build off-thread (45k+ itemów dla TFS) — nie blokuje UI.
        var built = await Task.Run(() =>
        {
            var list = new List<LapisItemRow>(sourceItems.Count);
            foreach (var item in searchService.Filter(sourceItems, filter))
            {
                var row = new LapisItemRow(item, thumbProvider);
                if (xmlLookup.TryGetValue(item.ServerId, out var xmlEntry))
                {
                    row.ApplyXmlEntry(xmlEntry);
                }
                if (appearanceProvider is not null && item.ClientId != 0)
                {
                    row.ApplyAppearance(appearanceProvider(item.ClientId));
                }
                list.Add(row);
            }
            return (IReadOnlyList<LapisItemRow>)list;
        }).ConfigureAwait(true);

        // Atomic swap — single property-changed notification, DataGrid wirtualizuje rendering.
        Items = built;

        SelectedItem = preferredSelection is not null
            ? built.FirstOrDefault(row => ReferenceEquals(row.Source, preferredSelection)) ?? built.FirstOrDefault()
            : built.FirstOrDefault();
        RefreshHeaderSummary();
    }

    private void RefreshHeaderSummary()
    {
        var total = _document?.Items.Count ?? 0;
        HeaderSummary = $"{Items.Count}/{total} itemów | OTB {(_document?.MajorVersion ?? 0)}.{(_document?.MinorVersion ?? 0)} build {(_document?.BuildNumber ?? 0)}";
    }

    private void LoadSelectedItem(LapisItemRow? row)
    {
        _isLoadingSelection = true;
        try
        {
            if (row is null)
            {
                SelectedServerId = 0;
                SelectedClientId = 0;
                SelectedName = string.Empty;
                SelectedTypeIndex = 0;
                SelectedSpeed = 0;
                UpdateFlagStates(0);
                return;
            }

            SelectedServerId = row.Source.ServerId;
            SelectedClientId = row.Source.ClientId;
            SelectedName = row.Source.Name;
            SelectedTypeIndex = (int)row.Source.ItemType;
            SelectedSpeed = row.Source.Speed;
            UpdateFlagStates(row.Source.Flags);
        }
        finally
        {
            _isLoadingSelection = false;
        }
    }

    private void ResetFlagStates()
    {
        Flags.Clear();
        foreach (var descriptor in LapisItemFlagDescriptor.All)
        {
            Flags.Add(new LapisItemFlagState(descriptor, OnFlagChanged));
        }
    }

    private void UpdateFlagStates(uint flags)
    {
        foreach (var state in Flags)
        {
            state.IsEnabled = (flags & (uint)state.Flag) != 0;
        }
    }

    private void OnFlagChanged(LapisItemFlagState state)
    {
        if (_isLoadingSelection || SelectedItem is null)
        {
            return;
        }

        _mutation.SetFlag(SelectedItem.Source, state.Flag, state.IsEnabled);
        SelectedItem.Refresh();
        RefreshHeaderSummary();
    }

    private static ushort ClampUshort(int value) => (ushort)Math.Clamp(value, ushort.MinValue, ushort.MaxValue);
}

public sealed partial class LapisItemRow : ObservableObject
{
    private Func<uint, Avalonia.Media.Imaging.Bitmap?>? _thumbnailProvider;
    private Avalonia.Media.Imaging.Bitmap? _thumbnail;
    private bool _thumbnailResolved;

    public LapisItemRow(OtbItem source, Func<uint, Avalonia.Media.Imaging.Bitmap?>? thumbnailProvider = null)
    {
        Source = source;
        _thumbnailProvider = thumbnailProvider;
    }

    public OtbItem Source { get; }
    public ushort ServerId => Source.ServerId;
    public ushort ClientId => Source.ClientId;
    public string Name => Source.Name;
    public string Type => Source.ItemType.ToString();
    public uint Flags => Source.Flags;
    public string DisplayName => string.IsNullOrWhiteSpace(Source.Name) ? $"#{Source.ServerId}" : Source.Name;
    public string FlagsSummary => BuildFlagsSummary(Source.Flags);

    /// <summary>
    /// Lazy thumbnail: ładowany dopiero gdy DataGrid renderuje widoczny wiersz.
    /// Dla 45k+ itemów odciąża to UI thread o tysiące LZMA-decode'ów na starcie.
    /// </summary>
    public Avalonia.Media.Imaging.Bitmap? Thumbnail
    {
        get
        {
            if (!_thumbnailResolved)
            {
                _thumbnailResolved = true;
                if (_thumbnailProvider is not null && Source.ClientId != 0)
                {
                    try { _thumbnail = _thumbnailProvider(Source.ClientId); }
                    catch { _thumbnail = null; }
                }
            }
            return _thumbnail;
        }
    }

    public void ResetThumbnail(Func<uint, Avalonia.Media.Imaging.Bitmap?>? provider)
    {
        _thumbnailProvider = provider;
        _thumbnail = null;
        _thumbnailResolved = false;
        OnPropertyChanged(nameof(Thumbnail));
    }

    [ObservableProperty] private string _xmlName = string.Empty;
    [ObservableProperty] private string _xmlArticle = string.Empty;
    [ObservableProperty] private string _xmlSummary = string.Empty;
    [ObservableProperty] private Narzedzia.Core.Models.ItemXmlEntry? _xmlEntry;

    /// <summary>Nazwa z appearances.dat (proto Appearance.Name) — odpowiada wartości w oryginalnym Lapis.</summary>
    [ObservableProperty] private string _appearanceName = string.Empty;
    [ObservableProperty] private bool _hasAppearance;

    public void ApplyAppearance(Narzedzia.Core.Tibia12.Appearance? appearance)
    {
        HasAppearance = appearance is not null;
        AppearanceName = appearance?.Name ?? string.Empty;
    }

    public void ApplyXmlEntry(Narzedzia.Core.Models.ItemXmlEntry? entry)
    {
        XmlEntry = entry;
        if (entry is null)
        {
            XmlName = string.Empty;
            XmlArticle = string.Empty;
            XmlSummary = string.Empty;
            return;
        }
        XmlName = entry.Name;
        XmlArticle = entry.Article;
        XmlSummary = BuildXmlSummary(entry);
    }

    private static string BuildXmlSummary(Narzedzia.Core.Models.ItemXmlEntry entry)
    {
        var parts = new List<string>(4);
        if (entry.GetAttribute("type") is { Length: > 0 } t) parts.Add($"type={t}");
        if (entry.GetAttribute("weight") is { Length: > 0 } w) parts.Add($"weight={w}");
        if (entry.GetAttribute("slotType") is { Length: > 0 } s) parts.Add($"slot={s}");
        if (entry.GetAttribute("attack") is { Length: > 0 } atk) parts.Add($"atk={atk}");
        if (entry.GetAttribute("defense") is { Length: > 0 } def) parts.Add($"def={def}");
        if (entry.GetAttribute("armor") is { Length: > 0 } arm) parts.Add($"arm={arm}");
        return parts.Count == 0 ? "—" : string.Join(", ", parts);
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(ServerId));
        OnPropertyChanged(nameof(ClientId));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(Flags));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(FlagsSummary));
        OnPropertyChanged(nameof(XmlName));
        OnPropertyChanged(nameof(XmlArticle));
        OnPropertyChanged(nameof(XmlSummary));
    }

    private static string BuildFlagsSummary(uint flags)
    {
        var names = LapisItemFlagDescriptor.All
            .Where(descriptor => (flags & (uint)descriptor.Flag) != 0)
            .Select(descriptor => descriptor.Flag.ToString())
            .Take(4)
            .ToArray();

        return names.Length == 0 ? "brak" : string.Join(", ", names);
    }
}

public sealed partial class LapisItemFlagState : ObservableObject
{
    private readonly Action<LapisItemFlagState> _changed;

    public LapisItemFlagState(LapisItemFlagDescriptor descriptor, Action<LapisItemFlagState> changed)
    {
        _changed = changed;
        Descriptor = descriptor;
    }

    public LapisItemFlagDescriptor Descriptor { get; }
    public LapisItemFlag Flag => Descriptor.Flag;
    public string Label => Descriptor.Label;
    public string Description => Descriptor.Description;

    [ObservableProperty] private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value) => _changed(this);
}
