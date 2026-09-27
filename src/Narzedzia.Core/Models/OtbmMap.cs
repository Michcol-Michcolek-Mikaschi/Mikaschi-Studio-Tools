namespace Narzedzia.Core.Models;

/// <summary>
/// Mapa OTBM (OpenTibia Binary Map) — format używany przez Remere's Map Editor (RME),
/// TFS oraz OTClient. Zawiera siatkę pól (`Tiles`), miasta, punkty trasy.
/// </summary>
public sealed class OtbmMap
{
    public uint FileVersion { get; set; }
    // RME zapisuje techniczny typ korzenia jako 0. OTBM_ROOTV1 (=1) opisuje
    // format logiczny, lecz nie jest wartością emitowaną przez saveMap().
    public byte RootNodeType { get; set; }
    public uint Version { get; set; }
    public ushort Width { get; set; }
    public ushort Height { get; set; }
    public uint ItemsMajorVersion { get; set; }
    public uint ItemsMinorVersion { get; set; }

    public string Description { get; set; } = string.Empty;
    public string HouseFile { get; set; } = string.Empty;
    public string SpawnFile { get; set; } = string.Empty;
    public string SpawnNpcFile { get; set; } = string.Empty;
    /// <summary>Oryginalne atrybuty węzła MAP_DATA do bezstratnego zapisu.</summary>
    public byte[] RawMapDataAttributeData { get; set; } = [];

    /// <summary>Wszystkie tile'e mapy zindeksowane po (x, y, z).</summary>
    public Dictionary<OtbmTileCoord, OtbmTile> Tiles { get; } = new();
    public List<OtbmTown> Towns { get; } = new();
    public List<OtbmWaypoint> Waypoints { get; } = new();
    public List<OtbmHouse> Houses { get; } = new();
    public List<OtbmSpawn> Spawns { get; } = new();

    public IEnumerable<byte> UsedFloors => Tiles.Keys.Select(t => t.Z).Distinct().OrderBy(z => z);
}

public readonly record struct OtbmTileCoord(ushort X, ushort Y, byte Z);

public sealed class OtbmTile
{
    public ushort X { get; set; }
    public ushort Y { get; set; }
    public byte Z { get; set; }
    public uint Flags { get; set; }
    public ushort GroundItemId { get; set; }
    public List<OtbmItem> Items { get; } = new();
    public bool IsHouseTile { get; set; }
    public uint HouseId { get; set; }
    /// <summary>Oryginalne atrybuty tile po współrzędnych i opcjonalnym HouseId.</summary>
    public byte[] RawAttributeData { get; set; } = [];
}

public sealed class OtbmItem
{
    public ushort Id { get; set; }
    public ushort? ActionId { get; set; }
    public ushort? UniqueId { get; set; }
    public byte? Count { get; set; }
    public string? Text { get; set; }
    public string? Description { get; set; }
    public ushort? DepotId { get; set; }
    public byte? HouseDoorId { get; set; }
    public byte? RuneCharges { get; set; }
    public uint? Duration { get; set; }
    public byte? DecayingState { get; set; }
    public uint? WrittenDate { get; set; }
    public string? WrittenBy { get; set; }
    public uint? SleeperGuid { get; set; }
    public uint? SleepStart { get; set; }
    public ushort? Charges { get; set; }
    public byte? Tier { get; set; }
    public byte[]? PodiumOutfit { get; set; }
    /// <summary>
    /// Dowolne atrybuty itemu zapisywane przez RME w OTBM 4 jako
    /// OTBM_ATTR_ATTRIBUTE_MAP. Lista zachowuje również typ wartości.
    /// </summary>
    public List<OtbmCustomAttribute> CustomAttributes { get; } = new();
    /// <summary>
    /// Oryginalny strumień atrybutów po ID. Zapewnia bezstratny zapis również dla
    /// rozszerzeń OTBM 4 i atrybutów nieznanych tej wersji programu.
    /// </summary>
    public byte[] RawAttributeData { get; set; } = [];
    public ushort? TeleportX { get; set; }
    public ushort? TeleportY { get; set; }
    public byte? TeleportZ { get; set; }
    public List<OtbmItem> Contents { get; } = new();
}

public enum OtbmCustomAttributeType : byte
{
    String = 1,
    Integer = 2,
    Float = 3,
    Boolean = 4,
    Double = 5
}

public sealed class OtbmCustomAttribute
{
    public string Key { get; set; } = string.Empty;
    public OtbmCustomAttributeType Type { get; set; }
    public string StringValue { get; set; } = string.Empty;
    public int IntegerValue { get; set; }
    public float FloatValue { get; set; }
    public bool BooleanValue { get; set; }
    public double DoubleValue { get; set; }
}

public sealed class OtbmTown
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ushort TempleX { get; set; }
    public ushort TempleY { get; set; }
    public byte TempleZ { get; set; }
}

public sealed class OtbmWaypoint
{
    public string Name { get; set; } = string.Empty;
    public ushort X { get; set; }
    public ushort Y { get; set; }
    public byte Z { get; set; }
}

public sealed class OtbmHouse
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public ushort EntryX { get; set; }
    public ushort EntryY { get; set; }
    public byte EntryZ { get; set; }
    public uint Rent { get; set; }
    public uint TownId { get; set; }
    public uint Size { get; set; }
    public bool IsGuildhall { get; set; }
}

public sealed class OtbmSpawn
{
    public ushort CenterX { get; set; }
    public ushort CenterY { get; set; }
    public byte CenterZ { get; set; }
    public int Radius { get; set; }
    public List<OtbmCreature> Creatures { get; } = new();
}

public sealed class OtbmCreature
{
    public string Name { get; set; } = string.Empty;
    public ushort X { get; set; }
    public ushort Y { get; set; }
    public byte Z { get; set; }
    public int SpawnTime { get; set; }
    public int Direction { get; set; }
    public bool IsNpc { get; set; }
}
