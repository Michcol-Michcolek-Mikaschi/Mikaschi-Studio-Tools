using Narzedzia.Core.Models;

namespace Modules.MapEditor.Services;

/// <summary>
/// Historia zmian dokumentu w stylu RME. Każda operacja przechowuje stan pola
/// przed i po zmianie, dlatego cofanie nie gubi nieznanych atrybutów OTBM.
/// </summary>
internal sealed class MapEditHistory
{
    private readonly Stack<MapTileEdit> _undo = [];
    private readonly Stack<MapTileEdit> _redo = [];
    private long _nextRevision;

    public long CurrentRevision { get; private set; }
    public long SavedRevision { get; private set; }
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public bool IsDirty => CurrentRevision != SavedRevision;
    public event Action<IReadOnlyList<MapTileChange>>? TileChangesPushed;

    public void Reset()
    {
        _undo.Clear();
        _redo.Clear();
        _nextRevision = 0;
        CurrentRevision = 0;
        SavedRevision = 0;
    }

    public void MarkSaved() => SavedRevision = CurrentRevision;

    public void Push(string description, OtbmTileCoord coordinate, OtbmTile? before, OtbmTile? after) =>
        Push(description, [new MapTileChange(coordinate, before, after)]);

    public void Push(string description, IReadOnlyList<MapTileChange> changes)
        => PushComposite(description, changes, null, null, null, null);

    public void PushComposite(
        string description,
        IReadOnlyList<MapTileChange> changes,
        IReadOnlyList<OtbmSpawn>? spawnsBefore,
        IReadOnlyList<OtbmSpawn>? spawnsAfter,
        MapEntitiesSnapshot? entitiesBefore = null,
        MapEntitiesSnapshot? entitiesAfter = null,
        MapPropertiesSnapshot? propertiesBefore = null,
        MapPropertiesSnapshot? propertiesAfter = null)
    {
        if (changes.Count == 0 && spawnsBefore is null && spawnsAfter is null &&
            entitiesBefore is null && entitiesAfter is null && propertiesBefore is null && propertiesAfter is null) return;
        var clonedChanges = changes.Select(change => new MapTileChange(
            change.Coordinate,
            CloneTile(change.Before),
            CloneTile(change.After))).ToArray();
        var edit = new MapTileEdit(
            description,
            clonedChanges,
            null,
            spawnsBefore is null || spawnsAfter is null
                ? null
                : new MapSpawnChange(CloneSpawns(spawnsBefore), CloneSpawns(spawnsAfter)),
            propertiesBefore is null || propertiesAfter is null
                ? null
                : new MapPropertiesChange(propertiesBefore, propertiesAfter),
            entitiesBefore is null || entitiesAfter is null
                ? null
                : new MapEntitiesChange(entitiesBefore, entitiesAfter),
            CurrentRevision,
            ++_nextRevision);
        _undo.Push(edit);
        _redo.Clear();
        CurrentRevision = edit.AfterRevision;
        if (clonedChanges.Length > 0) TileChangesPushed?.Invoke(clonedChanges);
    }

    public void PushWaypoint(string description, MapWaypointChange change)
    {
        var edit = new MapTileEdit(description, [], change, null, null, null, CurrentRevision, ++_nextRevision);
        _undo.Push(edit);
        _redo.Clear();
        CurrentRevision = edit.AfterRevision;
    }

    public void PushSpawns(string description, IReadOnlyList<OtbmSpawn> before, IReadOnlyList<OtbmSpawn> after)
    {
        var edit = new MapTileEdit(
            description,
            [],
            null,
            new MapSpawnChange(CloneSpawns(before), CloneSpawns(after)),
            null,
            null,
            CurrentRevision,
            ++_nextRevision);
        _undo.Push(edit);
        _redo.Clear();
        CurrentRevision = edit.AfterRevision;
    }

    public void PushMapProperties(string description, MapPropertiesSnapshot before, MapPropertiesSnapshot after)
    {
        if (before == after) return;
        var edit = new MapTileEdit(
            description,
            [],
            null,
            null,
            new MapPropertiesChange(before, after),
            null,
            CurrentRevision,
            ++_nextRevision);
        _undo.Push(edit);
        _redo.Clear();
        CurrentRevision = edit.AfterRevision;
    }

    public void PushEntities(string description, MapEntitiesSnapshot before, MapEntitiesSnapshot after)
    {
        if (before == after) return;
        PushComposite(description, [], null, null, before, after);
    }

    public MapTileEdit? Undo()
    {
        if (!_undo.TryPop(out var edit)) return null;
        _redo.Push(edit);
        CurrentRevision = edit.BeforeRevision;
        return edit;
    }

    public MapTileEdit? Redo()
    {
        if (!_redo.TryPop(out var edit)) return null;
        _undo.Push(edit);
        CurrentRevision = edit.AfterRevision;
        return edit;
    }

    public static OtbmTile? CloneTile(OtbmTile? source)
    {
        if (source is null) return null;
        var clone = new OtbmTile
        {
            X = source.X,
            Y = source.Y,
            Z = source.Z,
            Flags = source.Flags,
            GroundItemId = source.GroundItemId,
            IsHouseTile = source.IsHouseTile,
            HouseId = source.HouseId,
            RawAttributeData = (byte[])source.RawAttributeData.Clone()
        };
        foreach (var item in source.Items) clone.Items.Add(CloneItem(item));
        return clone;
    }

    private static OtbmItem CloneItem(OtbmItem source)
    {
        var clone = new OtbmItem
        {
            Id = source.Id,
            ActionId = source.ActionId,
            UniqueId = source.UniqueId,
            Count = source.Count,
            Text = source.Text,
            Description = source.Description,
            DepotId = source.DepotId,
            HouseDoorId = source.HouseDoorId,
            RuneCharges = source.RuneCharges,
            Duration = source.Duration,
            DecayingState = source.DecayingState,
            WrittenDate = source.WrittenDate,
            WrittenBy = source.WrittenBy,
            SleeperGuid = source.SleeperGuid,
            SleepStart = source.SleepStart,
            Charges = source.Charges,
            Tier = source.Tier,
            PodiumOutfit = source.PodiumOutfit is null ? null : (byte[])source.PodiumOutfit.Clone(),
            RawAttributeData = (byte[])source.RawAttributeData.Clone(),
            TeleportX = source.TeleportX,
            TeleportY = source.TeleportY,
            TeleportZ = source.TeleportZ
        };
        foreach (var attribute in source.CustomAttributes)
        {
            clone.CustomAttributes.Add(new OtbmCustomAttribute
            {
                Key = attribute.Key,
                Type = attribute.Type,
                StringValue = attribute.StringValue,
                IntegerValue = attribute.IntegerValue,
                FloatValue = attribute.FloatValue,
                BooleanValue = attribute.BooleanValue,
                DoubleValue = attribute.DoubleValue
            });
        }
        foreach (var child in source.Contents) clone.Contents.Add(CloneItem(child));
        return clone;
    }

    public static IReadOnlyList<OtbmSpawn> CloneSpawns(IEnumerable<OtbmSpawn> source) =>
        source.Select(spawn =>
        {
            var clone = new OtbmSpawn
            {
                CenterX = spawn.CenterX,
                CenterY = spawn.CenterY,
                CenterZ = spawn.CenterZ,
                Radius = spawn.Radius
            };
            foreach (var creature in spawn.Creatures)
            {
                clone.Creatures.Add(new OtbmCreature
                {
                    Name = creature.Name,
                    X = creature.X,
                    Y = creature.Y,
                    Z = creature.Z,
                    SpawnTime = creature.SpawnTime,
                    Direction = creature.Direction,
                    IsNpc = creature.IsNpc
                });
            }
            return clone;
        }).ToArray();
}

internal sealed record MapTileEdit(
    string Description,
    IReadOnlyList<MapTileChange> Changes,
    MapWaypointChange? WaypointChange,
    MapSpawnChange? SpawnChange,
    MapPropertiesChange? PropertiesChange,
    MapEntitiesChange? EntitiesChange,
    long BeforeRevision,
    long AfterRevision);

internal sealed record MapTileChange(
    OtbmTileCoord Coordinate,
    OtbmTile? Before,
    OtbmTile? After);

internal sealed record MapWaypointChange(
    int Index,
    OtbmTileCoord Before,
    OtbmTileCoord After);

internal sealed record MapSpawnChange(
    IReadOnlyList<OtbmSpawn> Before,
    IReadOnlyList<OtbmSpawn> After);

internal sealed record MapPropertiesChange(
    MapPropertiesSnapshot Before,
    MapPropertiesSnapshot After);

internal sealed record MapEntitiesChange(
    MapEntitiesSnapshot Before,
    MapEntitiesSnapshot After);

internal sealed record TownSnapshot(uint Id, string Name, ushort TempleX, ushort TempleY, byte TempleZ);
internal sealed record HouseSnapshot(
    uint Id, string Name, ushort EntryX, ushort EntryY, byte EntryZ,
    uint Rent, uint TownId, uint Size, bool IsGuildhall);
internal sealed record WaypointSnapshot(string Name, ushort X, ushort Y, byte Z);

internal sealed record MapEntitiesSnapshot(
    IReadOnlyList<TownSnapshot> Towns,
    IReadOnlyList<HouseSnapshot> Houses,
    IReadOnlyList<WaypointSnapshot> Waypoints)
{
    public static MapEntitiesSnapshot FromMap(OtbmMap map) => new(
        map.Towns.Select(town => new TownSnapshot(
            town.Id, town.Name, town.TempleX, town.TempleY, town.TempleZ)).ToArray(),
        map.Houses.Select(house => new HouseSnapshot(
            house.Id, house.Name, house.EntryX, house.EntryY, house.EntryZ,
            house.Rent, house.TownId, house.Size, house.IsGuildhall)).ToArray(),
        map.Waypoints.Select(waypoint => new WaypointSnapshot(
            waypoint.Name, waypoint.X, waypoint.Y, waypoint.Z)).ToArray());

    public void ApplyTo(OtbmMap map)
    {
        map.Towns.Clear();
        foreach (var town in Towns)
            map.Towns.Add(new OtbmTown
            {
                Id = town.Id, Name = town.Name,
                TempleX = town.TempleX, TempleY = town.TempleY, TempleZ = town.TempleZ
            });
        map.Houses.Clear();
        foreach (var house in Houses)
            map.Houses.Add(new OtbmHouse
            {
                Id = house.Id, Name = house.Name,
                EntryX = house.EntryX, EntryY = house.EntryY, EntryZ = house.EntryZ,
                Rent = house.Rent, TownId = house.TownId, Size = house.Size,
                IsGuildhall = house.IsGuildhall
            });
        map.Waypoints.Clear();
        foreach (var waypoint in Waypoints)
            map.Waypoints.Add(new OtbmWaypoint
            {
                Name = waypoint.Name, X = waypoint.X, Y = waypoint.Y, Z = waypoint.Z
            });
    }
}

internal sealed record MapPropertiesSnapshot(
    uint Version,
    ushort Width,
    ushort Height,
    uint ItemsMajorVersion,
    uint ItemsMinorVersion,
    string Description,
    string HouseFile,
    string SpawnFile,
    string SpawnNpcFile)
{
    public static MapPropertiesSnapshot FromMap(OtbmMap map) => new(
        map.Version,
        map.Width,
        map.Height,
        map.ItemsMajorVersion,
        map.ItemsMinorVersion,
        map.Description,
        map.HouseFile,
        map.SpawnFile,
        map.SpawnNpcFile);

    public void ApplyTo(OtbmMap map)
    {
        map.Version = Version;
        map.Width = Width;
        map.Height = Height;
        map.ItemsMajorVersion = ItemsMajorVersion;
        map.ItemsMinorVersion = ItemsMinorVersion;
        map.Description = Description;
        map.HouseFile = HouseFile;
        map.SpawnFile = SpawnFile;
        map.SpawnNpcFile = SpawnNpcFile;
    }
}
