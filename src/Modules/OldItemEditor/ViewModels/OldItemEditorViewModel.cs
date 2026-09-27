using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Modules.OldItemEditor.Services;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.OldItemEditor.ViewModels;

public partial class OldItemEditorViewModel : ObservableObject, IDisposable
{
    private readonly OtbParser _parser = new();
    private readonly OldItemEditorPreferencesStore _preferencesStore;
    private OldItemEditorPreferences _preferences;
    private OldItemClientWorkspace? _client;
    private OldItemClientWorkspace? _previousClient;
    private OtbFile? _document;
    private Dictionary<ushort, ItemXmlEntry> _xmlItems = [];
    private readonly Dictionary<ushort, ushort> _previousClientIds = [];
    private bool _loadingSelection;

    [ObservableProperty] private string _filePath = string.Empty;
    [ObservableProperty] private string _clientFolderPath = string.Empty;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private string _statusText = "Utwórz albo otwórz items.otb, a następnie wskaż folder klienta z Tibia.dat i Tibia.spr.";
    [ObservableProperty] private string _documentSummary = "Brak otwartego dokumentu OTB.";
    [ObservableProperty] private string _clientSummary = "Nie wczytano danych klienta.";
    [ObservableProperty] private string _comparisonText = "Wczytaj dane klienta, aby porównać atrybuty i sprite hash.";
    [ObservableProperty] private string _compareResultText = "Nie wykonano porównania plików OTB.";
    [ObservableProperty] private bool _showOnlyMismatchedItems;
    [ObservableProperty] private bool _showDeprecatedItems;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private OldItemRow? _selectedItem;
    [ObservableProperty] private OldItemCandidate? _selectedCandidate;
    [ObservableProperty] private IReadOnlyList<OldItemRow> _items = Array.Empty<OldItemRow>();

    [ObservableProperty] private int _selectedClientId = 100;
    [ObservableProperty] private string _selectedName = string.Empty;
    [ObservableProperty] private int _selectedTypeIndex;
    [ObservableProperty] private int _selectedStackOrderIndex;
    [ObservableProperty] private int _selectedTradeAs;
    [ObservableProperty] private int _selectedGroundSpeed;
    [ObservableProperty] private int _selectedLightLevel;
    [ObservableProperty] private int _selectedLightColor;
    [ObservableProperty] private int _selectedMinimapColor;
    [ObservableProperty] private int _selectedMaxReadWriteChars;
    [ObservableProperty] private int _selectedMaxReadChars;

    [ObservableProperty] private bool _unpassable;
    [ObservableProperty] private bool _movable;
    [ObservableProperty] private bool _blockMissiles;
    [ObservableProperty] private bool _blockPathfinder;
    [ObservableProperty] private bool _forceUse;
    [ObservableProperty] private bool _multiUse;
    [ObservableProperty] private bool _pickupable;
    [ObservableProperty] private bool _stackable;
    [ObservableProperty] private bool _readable;
    [ObservableProperty] private bool _rotatable;
    [ObservableProperty] private bool _hangable;
    [ObservableProperty] private bool _hookSouth;
    [ObservableProperty] private bool _hookEast;
    [ObservableProperty] private bool _hasElevation;
    [ObservableProperty] private bool _ignoreLook;
    [ObservableProperty] private bool _fullGround;

    [ObservableProperty] private Bitmap? _currentPreview;
    [ObservableProperty] private Bitmap? _previousPreview;
    [ObservableProperty] private OldItemEditorClientProfile _selectedProtocol = OldItemEditorClientCatalog.All[^1];
    [ObservableProperty] private bool _updateReassignSprites = true;
    [ObservableProperty] private bool _updateReloadAttributes = true;
    [ObservableProperty] private bool _updateCreateNewItems = true;

    public IReadOnlyList<OldItemEditorClientProfile> Protocols => OldItemEditorClientCatalog.All;
    public IReadOnlyList<string> TypeLabels { get; } = ["None", "Ground", "Container", "Fluid", "Splash", "Deprecated"];
    public IReadOnlyList<string> StackOrderLabels { get; } = ["None", "Border", "Bottom", "Top"];
    public ObservableCollection<OldItemCandidate> Candidates { get; } = [];
    public bool HasDocument => _document is not null;
    public bool HasClient => _client is not null;
    public string DirtyLabel => IsDirty ? "Niezapisane zmiany" : "Zapisano";
    public string SelectedServerIdText => SelectedItem?.ServerId.ToString() ?? "—";
    public string VisibleItemsText => $"{Items.Count} / {_document?.Items.Count ?? 0} itemów";
    public OldItemEditorPreferences Preferences => _preferences;

    public OldItemEditorViewModel()
        : this(new OldItemEditorPreferencesStore())
    {
    }

    public OldItemEditorViewModel(OldItemEditorPreferencesStore preferencesStore)
    {
        _preferencesStore = preferencesStore;
        _preferences = preferencesStore.Load();
    }

    public async Task OpenFileAsync(string path)
    {
        await RunBusyAsync(async () =>
        {
            var document = await Task.Run(() => _parser.Parse(path)).ConfigureAwait(true);
            _document = document;
            FilePath = path;
            IsDirty = false;
            _previousClientIds.Clear();
            LoadNearbyItemsXml(path);
            RefreshRows();

            var profile = OldItemEditorClientCatalog.FindByOtbVersion(document.MinorVersion);
            SelectedProtocol = profile ?? SelectedProtocol;
            StatusText = profile is null
                ? $"Wczytano {Path.GetFileName(path)}. OTB {document.MinorVersion} nie występuje w katalogu oryginalnego ItemEditora."
                : $"Wczytano {Path.GetFileName(path)} dla Tibia {profile.DisplayName}.";
        });
    }

    public async Task LoadClientFolderAsync(string folderPath)
    {
        await RunBusyAsync(async () =>
        {
            var workspace = new OldItemClientWorkspace();
            OldClientLoadResult result;
            try
            {
                result = await Task.Run(() => workspace.Load(folderPath, _preferences)).ConfigureAwait(true);
            }
            catch
            {
                workspace.Dispose();
                throw;
            }

            _client?.Dispose();
            _client = workspace;
            _previousClient?.Dispose();
            _previousClient = null;
            ClientFolderPath = folderPath;
            OnPropertyChanged(nameof(HasClient));

            var profileText = result.Profile is null
                ? "zmodyfikowany lub nierozpoznany klient"
                : $"Tibia {result.Profile.DisplayName}, OTB {result.Profile.OtbVersion}";
            var warning = result.Warnings.Count == 0 ? string.Empty : " Ostrzeżenia: " + string.Join(" ", result.Warnings);
            ClientSummary = $"{profileText}; {_client.Items.Count} itemów DAT.{warning}";

            if (_document is not null && result.Profile is not null && _document.MinorVersion != result.Profile.OtbVersion)
            {
                StatusText = $"Wczytano klienta, ale items.otb wymaga profilu OTB {_document.MinorVersion}, a klient ma OTB {result.Profile.OtbVersion}.";
            }
            else
            {
                StatusText = $"Wczytano Tibia.dat i Tibia.spr z {Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar))}.";
            }

            RefreshRows(SelectedItem?.Source);
        });
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
            StatusText = "Użyj Zapisz jako, aby wskazać ścieżkę items.otb.";
            return;
        }

        try
        {
            SaveAtomically(_document, FilePath);
            IsDirty = false;
            StatusText = $"Zapisano {Path.GetFileName(FilePath)}. Utworzono kopię .bak, jeśli plik wcześniej istniał.";
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd zapisu OTB: {ex.Message}";
        }
    }

    public void SaveFileAs(string path)
    {
        FilePath = path;
        SaveFile();
    }

    public bool SavePreferences(OldItemEditorPreferences preferences)
    {
        try
        {
            _preferencesStore.Save(preferences);
            _preferences = preferences;
            StatusText = $"Zapisano preferencje klienta Tibia w folderze {preferences.ClientDirectory}.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Nie udało się zapisać preferencji: {ex.Message}";
            return false;
        }
    }

    public async Task CompareWithFileAsync(string path)
    {
        if (_document is null)
        {
            StatusText = "Najpierw otwórz bazowy plik OTB.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var other = await Task.Run(() => _parser.Parse(path)).ConfigureAwait(true);
            CompareResultText = BuildOtbComparison(_document, other, Path.GetFileName(FilePath), Path.GetFileName(path));
            StatusText = $"Porównano bieżący dokument z {Path.GetFileName(path)}.";
        });
    }

    public async Task UpdateToClientFolderAsync(string folderPath)
    {
        if (_document is null)
        {
            StatusText = "Najpierw otwórz albo utwórz items.otb.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var target = new OldItemClientWorkspace();
            OldClientLoadResult result;
            try
            {
                result = await Task.Run(() => target.Load(folderPath, _preferences)).ConfigureAwait(true);
            }
            catch
            {
                target.Dispose();
                throw;
            }

            if (result.Profile is null)
            {
                target.Dispose();
                throw new InvalidDataException("Aktualizacja wersji wymaga klienta rozpoznanego przez oryginalny katalog ItemEditora.");
            }

            var targetByHash = UpdateReassignSprites
                ? await Task.Run(() => target.Items.Values
                    .GroupBy(snapshot => Convert.ToHexString(snapshot.SpriteHash), StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal)).ConfigureAwait(true)
                : new Dictionary<string, OldItemClientSnapshot[]>(StringComparer.Ordinal);

            _previousClientIds.Clear();
            var assigned = new HashSet<ushort>();
            var matched = 0;
            var reloaded = 0;
            foreach (var item in _document.Items.Where(item => item.ItemType != OtbItemType.Deprecated))
            {
                _previousClientIds[item.ServerId] = item.ClientId;
                OldItemClientSnapshot? match = null;
                if (target.GetItem(item.ClientId) is { } sameId && sameId.Matches(item))
                {
                    match = sameId;
                }
                else if (UpdateReassignSprites)
                {
                    var key = Convert.ToHexString(item.SpriteHash);
                    if (targetByHash.TryGetValue(key, out var hashMatches))
                    {
                        match = hashMatches.FirstOrDefault(candidate => candidate.Matches(item, compareHash: false));
                    }
                }

                match ??= UpdateReloadAttributes ? target.GetItem(item.ClientId) : null;
                if (match is null) continue;

                if (UpdateReloadAttributes)
                {
                    match.ApplyTo(item);
                    reloaded++;
                }
                else
                {
                    item.ClientId = match.ClientId;
                    item.SpriteHash = match.SpriteHash.ToArray();
                }

                assigned.Add(match.ClientId);
                matched++;
            }

            var created = 0;
            if (UpdateCreateNewItems)
            {
                foreach (var snapshot in target.Items.Values.Where(snapshot => !assigned.Contains(snapshot.ClientId)))
                {
                    var item = CreateBlankItem();
                    snapshot.ApplyTo(item);
                    _document.Items.Add(item);
                    assigned.Add(snapshot.ClientId);
                    created++;
                }
            }

            _document.MinorVersion = result.Profile.OtbVersion;
            _document.BuildNumber++;
            SelectedProtocol = result.Profile;
            _previousClient?.Dispose();
            _previousClient = _client;
            _client = target;
            ClientFolderPath = folderPath;
            ClientSummary = $"Tibia {result.Profile.DisplayName}, OTB {result.Profile.OtbVersion}; {_client.Items.Count} itemów DAT.";
            IsDirty = true;
            OnPropertyChanged(nameof(HasClient));
            RefreshRows();
            StatusText = $"Zaktualizowano OTB do Tibia {result.Profile.DisplayName}: dopasowano {matched}, przeładowano {reloaded}, utworzono {created}.";
        });
    }

    [RelayCommand]
    private void NewDocument()
    {
        _document = new OtbFile
        {
            MajorVersion = 3,
            MinorVersion = SelectedProtocol.OtbVersion,
            BuildNumber = 1,
            Description = $"OTB 3.{SelectedProtocol.OtbVersion}.1-{SelectedProtocol.DisplayName}"
        };
        var item = CreateBlankItem();
        item.ServerId = 100;
        item.ClientId = 100;
        _document.Items.Add(item);
        FilePath = string.Empty;
        IsDirty = true;
        _xmlItems.Clear();
        _previousClientIds.Clear();
        RefreshRows(item);
        OnPropertyChanged(nameof(HasDocument));
        StatusText = $"Utworzono nowy dokument dla Tibia {SelectedProtocol.DisplayName}.";
    }

    [RelayCommand]
    private void Save() => SaveFile();

    [RelayCommand]
    private void AddNewItem()
    {
        if (_document is null) NewDocument();
        if (_document is null) return;
        var item = CreateBlankItem();
        _document.Items.Add(item);
        IsDirty = true;
        RefreshRows(item);
        StatusText = $"Utworzono item Server ID {item.ServerId}.";
    }

    [RelayCommand]
    private void DuplicateSelected()
    {
        if (_document is null || SelectedItem is null)
        {
            StatusText = "Wybierz item do duplikowania.";
            return;
        }

        var source = SelectedItem.Source;
        var clone = new OtbItem
        {
            ServerId = NextServerId(),
            ClientId = source.ClientId,
            Name = source.Name,
            ItemType = source.ItemType,
            Flags = source.Flags,
            Speed = source.Speed,
            SpriteHash = source.SpriteHash.ToArray(),
            MinimapColor = source.MinimapColor,
            MaxReadWriteChars = source.MaxReadWriteChars,
            MaxReadChars = source.MaxReadChars,
            LightLevel = source.LightLevel,
            LightColor = source.LightColor,
            StackOrder = source.StackOrder,
            TradeAs = source.TradeAs,
            RawAttributes = source.RawAttributes.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray())
        };
        _document.Items.Add(clone);
        IsDirty = true;
        RefreshRows(clone);
        StatusText = $"Zduplikowano item {source.ServerId} jako {clone.ServerId}.";
    }

    [RelayCommand]
    private void ReloadSelected()
    {
        if (SelectedItem is null || _client?.GetItem(SelectedItem.Source.ClientId) is not { } clientItem)
        {
            StatusText = "Dla wybranego Client ID nie ma itemu w aktualnym Tibia.dat.";
            return;
        }

        clientItem.ApplyTo(SelectedItem.Source);
        IsDirty = true;
        SelectedItem.Refresh();
        LoadSelection(SelectedItem);
        StatusText = $"Przeładowano atrybuty itemu {SelectedItem.ServerId} z Tibia.dat.";
    }

    [RelayCommand]
    private void ReloadAllItems()
    {
        if (_document is null || _client is null)
        {
            StatusText = "Otwórz items.otb i wczytaj folder klienta.";
            return;
        }

        var count = 0;
        foreach (var item in _document.Items)
        {
            if (_client.GetItem(item.ClientId) is not { } source || source.Matches(item)) continue;
            source.ApplyTo(item);
            count++;
        }
        IsDirty |= count > 0;
        RefreshRows(SelectedItem?.Source);
        StatusText = $"Przeładowano atrybuty {count} itemów z Tibia.dat.";
    }

    [RelayCommand]
    private void CreateMissingItems()
    {
        if (_document is null || _client is null)
        {
            StatusText = "Otwórz items.otb i wczytaj folder klienta.";
            return;
        }

        var lastClientId = _document.Items.Where(item => item.ItemType != OtbItemType.Deprecated)
            .Select(item => item.ClientId).DefaultIfEmpty((ushort)99).Max();
        var created = 0;
        foreach (var snapshot in _client.Items.Values.Where(item => item.ClientId > lastClientId).OrderBy(item => item.ClientId))
        {
            var item = CreateBlankItem();
            snapshot.ApplyTo(item);
            _document.Items.Add(item);
            created++;
        }

        IsDirty |= created > 0;
        RefreshRows();
        StatusText = $"Utworzono {created} brakujących itemów.";
    }

    [RelayCommand]
    private void ApplySelected() => ApplyEditorToSelection(force: true);

    [RelayCommand]
    private void ClearFilter() => FilterText = string.Empty;

    [RelayCommand]
    private void RefreshItems()
    {
        RefreshRows(SelectedItem?.Source);
        StatusText = $"Odświeżono listę: {Items.Count} itemów.";
    }

    [RelayCommand]
    private async Task FindCandidates()
    {
        Candidates.Clear();
        if (SelectedItem is null || _client is null)
        {
            StatusText = "Wybierz item i wczytaj dane klienta.";
            return;
        }

        var previousId = _previousClientIds.GetValueOrDefault(SelectedItem.ServerId, SelectedItem.Source.ClientId);
        var sourceWorkspace = _previousClient ?? _client;
        var hash = sourceWorkspace.GetPerceptualHash(previousId);
        if (hash is null)
        {
            StatusText = "Brak obrazu źródłowego do wyszukania kandydatów.";
            return;
        }

        IsBusy = true;
        try
        {
            var candidates = await Task.Run(() => _client.FindClosest(hash.Value, 5)).ConfigureAwait(true);
            foreach (var candidate in candidates)
                Candidates.Add(new OldItemCandidate(candidate.ClientId, candidate.Distance, _client.GetPreview(candidate.ClientId)));
            SelectedCandidate = Candidates.FirstOrDefault();
            StatusText = $"Znaleziono {Candidates.Count} kandydatów sprite.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AssignCandidate()
    {
        if (SelectedItem is null || SelectedCandidate is null) return;
        SelectedClientId = SelectedCandidate.ClientId;
        Candidates.Clear();
        StatusText = $"Przypisano Client ID {SelectedClientId}. Użyj Przeładuj item, aby pobrać również atrybuty DAT.";
    }

    partial void OnSelectedItemChanged(OldItemRow? value) => LoadSelection(value);
    partial void OnFilterTextChanged(string value) => RefreshRows(SelectedItem?.Source);
    partial void OnShowOnlyMismatchedItemsChanged(bool value) => RefreshRows(SelectedItem?.Source);
    partial void OnShowDeprecatedItemsChanged(bool value) => RefreshRows();
    partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(DirtyLabel));

    partial void OnSelectedClientIdChanged(int value) => EditorValueChanged();
    partial void OnSelectedNameChanged(string value) => EditorValueChanged();
    partial void OnSelectedTypeIndexChanged(int value) => EditorValueChanged();
    partial void OnSelectedStackOrderIndexChanged(int value) => EditorValueChanged();
    partial void OnSelectedTradeAsChanged(int value) => EditorValueChanged();
    partial void OnSelectedGroundSpeedChanged(int value) => EditorValueChanged();
    partial void OnSelectedLightLevelChanged(int value) => EditorValueChanged();
    partial void OnSelectedLightColorChanged(int value) => EditorValueChanged();
    partial void OnSelectedMinimapColorChanged(int value) => EditorValueChanged();
    partial void OnSelectedMaxReadWriteCharsChanged(int value) => EditorValueChanged();
    partial void OnSelectedMaxReadCharsChanged(int value) => EditorValueChanged();
    partial void OnUnpassableChanged(bool value) => EditorValueChanged();
    partial void OnMovableChanged(bool value) => EditorValueChanged();
    partial void OnBlockMissilesChanged(bool value) => EditorValueChanged();
    partial void OnBlockPathfinderChanged(bool value) => EditorValueChanged();
    partial void OnForceUseChanged(bool value) => EditorValueChanged();
    partial void OnMultiUseChanged(bool value) => EditorValueChanged();
    partial void OnPickupableChanged(bool value) => EditorValueChanged();
    partial void OnStackableChanged(bool value) => EditorValueChanged();
    partial void OnReadableChanged(bool value) => EditorValueChanged();
    partial void OnRotatableChanged(bool value) => EditorValueChanged();
    partial void OnHangableChanged(bool value) => EditorValueChanged();
    partial void OnHookSouthChanged(bool value) => EditorValueChanged();
    partial void OnHookEastChanged(bool value) => EditorValueChanged();
    partial void OnHasElevationChanged(bool value) => EditorValueChanged();
    partial void OnIgnoreLookChanged(bool value) => EditorValueChanged();
    partial void OnFullGroundChanged(bool value) => EditorValueChanged();

    private void EditorValueChanged()
    {
        if (!_loadingSelection) ApplyEditorToSelection(force: false);
    }

    private void ApplyEditorToSelection(bool force)
    {
        if (_loadingSelection || SelectedItem is null) return;
        var item = SelectedItem.Source;
        var oldClientId = item.ClientId;
        item.ClientId = ClampUshort(SelectedClientId);
        item.Name = SelectedName.Trim();
        item.ItemType = (OtbItemType)Math.Clamp(SelectedTypeIndex, 0, TypeLabels.Count - 1);
        item.StackOrder = (byte)Math.Clamp(SelectedStackOrderIndex, 0, StackOrderLabels.Count - 1);
        item.TradeAs = ClampUshort(SelectedTradeAs);
        item.Speed = ClampUshort(SelectedGroundSpeed);
        item.LightLevel = ClampUshort(SelectedLightLevel);
        item.LightColor = ClampUshort(SelectedLightColor);
        item.MinimapColor = ClampUshort(SelectedMinimapColor);
        item.MaxReadWriteChars = ClampUshort(SelectedMaxReadWriteChars);
        item.MaxReadChars = ClampUshort(SelectedMaxReadChars);

        SetFlag(item, OldItemFlags.Unpassable, Unpassable);
        SetFlag(item, OldItemFlags.Movable, Movable);
        SetFlag(item, OldItemFlags.BlockMissiles, BlockMissiles);
        SetFlag(item, OldItemFlags.BlockPathfinder, BlockPathfinder);
        SetFlag(item, OldItemFlags.ForceUse, ForceUse);
        SetFlag(item, OldItemFlags.MultiUse, MultiUse);
        SetFlag(item, OldItemFlags.Pickupable, Pickupable);
        SetFlag(item, OldItemFlags.Stackable, Stackable);
        SetFlag(item, OldItemFlags.Readable, Readable);
        SetFlag(item, OldItemFlags.Rotatable, Rotatable);
        SetFlag(item, OldItemFlags.Hangable, Hangable);
        SetFlag(item, OldItemFlags.HookSouth, HookSouth);
        SetFlag(item, OldItemFlags.HookEast, HookEast);
        SetFlag(item, OldItemFlags.HasElevation, HasElevation);
        SetFlag(item, OldItemFlags.IgnoreLook, IgnoreLook);
        SetFlag(item, OldItemFlags.FullGround, FullGround);
        SetFlag(item, OldItemFlags.StackOrder, item.StackOrder != 0);

        if (oldClientId != item.ClientId)
            _previousClientIds[item.ServerId] = oldClientId;
        IsDirty = true;
        SelectedItem.Refresh();
        RefreshSelectionPreviewAndComparison();
        RefreshDocumentSummary();
        if (force) StatusText = $"Zastosowano zmiany itemu {item.ServerId} w pamięci.";
    }

    private void LoadSelection(OldItemRow? row)
    {
        _loadingSelection = true;
        try
        {
            Candidates.Clear();
            SelectedCandidate = null;
            if (row is null)
            {
                CurrentPreview = null;
                PreviousPreview = null;
                ComparisonText = "Nie wybrano itemu.";
                OnPropertyChanged(nameof(SelectedServerIdText));
                return;
            }

            var item = row.Source;
            SelectedClientId = item.ClientId;
            SelectedName = item.Name;
            SelectedTypeIndex = (int)item.ItemType;
            SelectedStackOrderIndex = item.StackOrder;
            SelectedTradeAs = item.TradeAs;
            SelectedGroundSpeed = item.Speed;
            SelectedLightLevel = item.LightLevel;
            SelectedLightColor = item.LightColor;
            SelectedMinimapColor = item.MinimapColor;
            SelectedMaxReadWriteChars = item.MaxReadWriteChars;
            SelectedMaxReadChars = item.MaxReadChars;
            Unpassable = HasFlag(item, OldItemFlags.Unpassable);
            Movable = HasFlag(item, OldItemFlags.Movable);
            BlockMissiles = HasFlag(item, OldItemFlags.BlockMissiles);
            BlockPathfinder = HasFlag(item, OldItemFlags.BlockPathfinder);
            ForceUse = HasFlag(item, OldItemFlags.ForceUse);
            MultiUse = HasFlag(item, OldItemFlags.MultiUse);
            Pickupable = HasFlag(item, OldItemFlags.Pickupable);
            Stackable = HasFlag(item, OldItemFlags.Stackable);
            Readable = HasFlag(item, OldItemFlags.Readable);
            Rotatable = HasFlag(item, OldItemFlags.Rotatable);
            Hangable = HasFlag(item, OldItemFlags.Hangable);
            HookSouth = HasFlag(item, OldItemFlags.HookSouth);
            HookEast = HasFlag(item, OldItemFlags.HookEast);
            HasElevation = HasFlag(item, OldItemFlags.HasElevation);
            IgnoreLook = HasFlag(item, OldItemFlags.IgnoreLook);
            FullGround = HasFlag(item, OldItemFlags.FullGround);
            OnPropertyChanged(nameof(SelectedServerIdText));
            RefreshSelectionPreviewAndComparison();
        }
        finally
        {
            _loadingSelection = false;
        }
    }

    private void RefreshSelectionPreviewAndComparison()
    {
        if (SelectedItem is null)
        {
            CurrentPreview = null;
            PreviousPreview = null;
            return;
        }

        var item = SelectedItem.Source;
        CurrentPreview = _client?.GetPreview(item.ClientId);
        var previousId = _previousClientIds.GetValueOrDefault(item.ServerId, item.ClientId);
        PreviousPreview = (_previousClient ?? _client)?.GetPreview(previousId);
        if (_client?.GetItem(item.ClientId) is not { } snapshot)
        {
            ComparisonText = _client is null
                ? "Wczytaj folder klienta, aby zobaczyć sprite i porównanie."
                : $"Client ID {item.ClientId} nie istnieje w wczytanym Tibia.dat.";
            return;
        }

        ComparisonText = snapshot.Matches(item)
            ? "Item jest zgodny z Tibia.dat i sprite hash."
            : BuildClientDifference(item, snapshot);
    }

    private void RefreshRows(OtbItem? preferred = null)
    {
        if (_document is null)
        {
            Items = [];
            SelectedItem = null;
            RefreshDocumentSummary();
            return;
        }

        var query = _document.Items.AsEnumerable();
        query = ShowDeprecatedItems
            ? query.Where(item => item.ItemType == OtbItemType.Deprecated)
            : query.Where(item => item.ItemType != OtbItemType.Deprecated);
        if (ShowOnlyMismatchedItems && _client is not null)
            query = query.Where(item => _client.GetItem(item.ClientId)?.Matches(item) != true);

        var filter = FilterText.Trim();
        var rows = query.Select(item => new OldItemRow(item, _client, _xmlItems.GetValueOrDefault(item.ServerId))).ToList();
        if (filter.Length > 0)
            rows = rows.Where(row => row.SearchText.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

        var preferredItem = preferred ?? SelectedItem?.Source;
        Items = rows;
        SelectedItem = preferredItem is null
            ? rows.FirstOrDefault()
            : rows.FirstOrDefault(row => ReferenceEquals(row.Source, preferredItem)) ?? rows.FirstOrDefault();
        OnPropertyChanged(nameof(VisibleItemsText));
        RefreshDocumentSummary();
    }

    private void RefreshDocumentSummary()
    {
        DocumentSummary = _document is null
            ? "Brak otwartego dokumentu OTB."
            : $"OTB {_document.MajorVersion}.{_document.MinorVersion}.{_document.BuildNumber} • {_document.Items.Count} itemów • {Path.GetFileName(FilePath) switch { "" => "nowy dokument", var name => name }}";
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(VisibleItemsText));
    }

    private OtbItem CreateBlankItem() => new()
    {
        ServerId = NextServerId(),
        ClientId = _client?.MinimumClientId ?? 100,
        Flags = (uint)OldItemFlags.Movable,
        SpriteHash = new byte[16]
    };

    private ushort NextServerId()
    {
        if (_document is null || _document.Items.Count == 0) return 100;
        var maximum = _document.Items.Max(item => item.ServerId);
        if (maximum == ushort.MaxValue) throw new InvalidOperationException("Osiągnięto maksymalny Server ID 65535.");
        return (ushort)(maximum + 1);
    }

    private void LoadNearbyItemsXml(string otbPath)
    {
        _xmlItems = [];
        var directory = Path.GetDirectoryName(otbPath);
        if (directory is null) return;
        var candidates = new[]
        {
            Path.Combine(directory, "items.xml"),
            Path.Combine(directory, "items", "items.xml"),
            Path.Combine(directory, "..", "items", "items.xml")
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) return;
        try
        {
            _xmlItems = ItemsXmlReader.BuildLookup(ItemsXmlReader.Read(Path.GetFullPath(path)));
        }
        catch
        {
            _xmlItems = [];
        }
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            StatusText = $"Błąd Old Item Editor: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SaveAtomically(OtbFile file, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? throw new InvalidOperationException("Nieprawidłowa ścieżka zapisu.");
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            _parser.Save(file, temporary);
            if (File.Exists(fullPath))
                File.Replace(temporary, fullPath, fullPath + ".bak", ignoreMetadataErrors: true);
            else
                File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static string BuildClientDifference(OtbItem item, OldItemClientSnapshot client)
    {
        var differences = new List<string>();
        void Add(string name, object current, object expected)
        {
            if (!Equals(current, expected)) differences.Add($"{name}: OTB={current}, DAT={expected}");
        }
        Add("Type", item.ItemType, client.ItemType);
        var comparableMask = (uint)OldItemFlagMasks.Comparable;
        Add("Flags", $"0x{item.Flags & comparableMask:X8}", $"0x{client.Flags & comparableMask:X8}");
        Add("Stack Order", item.StackOrder, client.StackOrder);
        Add("Ground Speed", item.Speed, client.GroundSpeed);
        Add("Light Level", item.LightLevel, client.LightLevel);
        Add("Light Color", item.LightColor, client.LightColor);
        Add("Read / Write Length", item.MaxReadWriteChars, client.MaxReadWriteChars);
        Add("Read Length", item.MaxReadChars, client.MaxReadChars);
        Add("Minimap Color", item.MinimapColor, client.MinimapColor);
        Add("Ware ID", item.TradeAs, client.TradeAs);
        Add("Name", item.Name, client.Name);
        if (!item.SpriteHash.AsSpan().SequenceEqual(client.SpriteHash)) differences.Add("Sprite hash różni się");
        return differences.Count == 0 ? "Brak różnic." : string.Join(" • ", differences);
    }

    private static string BuildOtbComparison(OtbFile left, OtbFile right, string leftName, string rightName)
    {
        var lines = new List<string>
        {
            $"{leftName}: {left.Items.Count} itemów, OTB {left.MajorVersion}.{left.MinorVersion}.{left.BuildNumber}",
            $"{rightName}: {right.Items.Count} itemów, OTB {right.MajorVersion}.{right.MinorVersion}.{right.BuildNumber}"
        };
        var rightById = right.Items.GroupBy(item => item.ServerId).ToDictionary(group => group.Key, group => group.First());
        foreach (var item in left.Items)
        {
            if (!rightById.TryGetValue(item.ServerId, out var other))
            {
                lines.Add($"SID {item.ServerId}: brak w drugim pliku");
                continue;
            }
            if (item.ClientId != other.ClientId) lines.Add($"SID {item.ServerId}: Client ID {item.ClientId} → {other.ClientId}");
            if (!item.SpriteHash.AsSpan().SequenceEqual(other.SpriteHash)) lines.Add($"SID {item.ServerId}: zmieniony sprite hash");
            if (item.Flags != other.Flags) lines.Add($"SID {item.ServerId}: flags 0x{item.Flags:X8} → 0x{other.Flags:X8}");
            if (item.ItemType != other.ItemType) lines.Add($"SID {item.ServerId}: type {item.ItemType} → {other.ItemType}");
        }
        var leftIds = left.Items.Select(item => item.ServerId).ToHashSet();
        foreach (var item in right.Items.Where(item => !leftIds.Contains(item.ServerId)))
            lines.Add($"SID {item.ServerId}: tylko w drugim pliku");
        if (lines.Count == 2) lines.Add("Brak różnic.");
        return string.Join(Environment.NewLine, lines.Take(5000));
    }

    private static bool HasFlag(OtbItem item, OldItemFlags flag) => (item.Flags & (uint)flag) != 0;
    private static void SetFlag(OtbItem item, OldItemFlags flag, bool enabled)
    {
        if (enabled) item.Flags |= (uint)flag;
        else item.Flags &= ~(uint)flag;
    }
    private static ushort ClampUshort(int value) => (ushort)Math.Clamp(value, ushort.MinValue, ushort.MaxValue);

    public void Dispose()
    {
        _client?.Dispose();
        _previousClient?.Dispose();
    }
}

public sealed partial class OldItemRow : ObservableObject
{
    private readonly OldItemClientWorkspace? _client;
    private Bitmap? _thumbnail;
    private bool _thumbnailResolved;

    public OldItemRow(OtbItem source, OldItemClientWorkspace? client, ItemXmlEntry? xml)
    {
        Source = source;
        _client = client;
        XmlName = xml?.Name ?? string.Empty;
    }

    public OtbItem Source { get; }
    public ushort ServerId => Source.ServerId;
    public ushort ClientId => Source.ClientId;
    public string Name => string.IsNullOrWhiteSpace(XmlName) ? Source.Name : XmlName;
    public string XmlName { get; }
    public string Type => Source.ItemType.ToString();
    public bool IsMismatch => _client?.GetItem(Source.ClientId)?.Matches(Source) == false;
    public string MatchLabel => _client is null ? "—" : IsMismatch ? "Różni się" : "Zgodny";
    public string FlagsSummary => BuildFlagsSummary(Source.Flags);
    public string SearchText => $"{ServerId} {ClientId} {Name} {Source.Name} {Type} {FlagsSummary}";
    public Bitmap? Thumbnail
    {
        get
        {
            if (!_thumbnailResolved)
            {
                _thumbnailResolved = true;
                _thumbnail = _client?.GetPreview(Source.ClientId);
            }
            return _thumbnail;
        }
    }

    public void Refresh()
    {
        _thumbnailResolved = false;
        _thumbnail = null;
        OnPropertyChanged(nameof(ServerId));
        OnPropertyChanged(nameof(ClientId));
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(IsMismatch));
        OnPropertyChanged(nameof(MatchLabel));
        OnPropertyChanged(nameof(FlagsSummary));
        OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(Thumbnail));
    }

    private static string BuildFlagsSummary(uint flags)
    {
        var values = Enum.GetValues<OldItemFlags>()
            .Where(flag => flag != OldItemFlags.None && (flags & (uint)flag) != 0)
            .Take(4)
            .Select(flag => flag.ToString())
            .ToArray();
        return values.Length == 0 ? "brak flag" : string.Join(", ", values);
    }
}

public sealed record OldItemCandidate(ushort ClientId, int Distance, Bitmap? Preview)
{
    public string Label => $"Client ID {ClientId} • różnica {Distance}";
}
