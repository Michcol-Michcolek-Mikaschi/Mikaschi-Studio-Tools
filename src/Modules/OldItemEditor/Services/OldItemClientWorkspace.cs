using System.Security.Cryptography;
using System.Numerics;
using Avalonia.Media.Imaging;
using Modules.ObjectBuilder.Services;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.OldItemEditor.Services;

public sealed record OldClientLoadResult(
    OldItemEditorClientProfile? Profile,
    string DatPath,
    string SprPath,
    IReadOnlyList<string> Warnings);

public sealed class OldItemClientSnapshot
{
    private readonly DatThingType _thing;
    private readonly LegacySpriteStore _sprites;
    private readonly int _clientVersion;
    private byte[]? _spriteHash;

    public OldItemClientSnapshot(DatThingType thing, LegacySpriteStore sprites, int clientVersion)
    {
        _thing = thing;
        _sprites = sprites;
        _clientVersion = clientVersion;
    }

    public ushort ClientId => checked((ushort)_thing.Id);
    public OtbItemType ItemType => _thing.IsGround ? OtbItemType.Ground
        : _thing.IsContainer ? OtbItemType.Container
        : _thing.IsFluidContainer ? OtbItemType.Fluid
        : _thing.IsFluid ? OtbItemType.Splash
        : OtbItemType.None;
    public byte StackOrder => _thing.IsGroundBorder ? (byte)1
        : _thing.IsOnBottom ? (byte)2
        : _thing.IsOnTop ? (byte)3
        : (byte)0;
    public uint Flags => BuildFlags(_thing, StackOrder, _clientVersion);
    public ushort GroundSpeed => _thing.GroundSpeed;
    public ushort LightLevel => _thing.LightLevel;
    public ushort LightColor => _thing.LightColor;
    public ushort MaxReadWriteChars => _thing.MaxReadWriteChars;
    public ushort MaxReadChars => _thing.MaxReadChars;
    public ushort MinimapColor => _thing.MiniMapColor;
    public ushort TradeAs => _thing.MarketTradeAs;
    public string Name => _thing.MarketName;
    public byte[] SpriteHash => _spriteHash ??= ComputeSpriteHash(_thing, _sprites);

    public bool Matches(OtbItem item, bool compareHash = true)
    {
        if (item.ItemType == OtbItemType.Deprecated ||
            item.ItemType != ItemType ||
            item.StackOrder != StackOrder ||
            (item.Flags & (uint)OldItemFlagMasks.Comparable) != (Flags & (uint)OldItemFlagMasks.Comparable) ||
            item.Speed != GroundSpeed ||
            item.LightLevel != LightLevel ||
            item.LightColor != LightColor ||
            item.MaxReadChars != MaxReadChars ||
            item.MaxReadWriteChars != MaxReadWriteChars ||
            item.MinimapColor != MinimapColor ||
            item.TradeAs != TradeAs ||
            !string.Equals(item.Name, Name, StringComparison.Ordinal))
        {
            return false;
        }

        return !compareHash || item.SpriteHash.AsSpan().SequenceEqual(SpriteHash);
    }

    public void ApplyTo(OtbItem item)
    {
        var serverId = item.ServerId;
        item.ClientId = ClientId;
        item.ItemType = ItemType;
        item.Flags = Flags;
        item.Speed = GroundSpeed;
        item.SpriteHash = SpriteHash.ToArray();
        item.MinimapColor = MinimapColor;
        item.MaxReadWriteChars = MaxReadWriteChars;
        item.MaxReadChars = MaxReadChars;
        item.LightLevel = LightLevel;
        item.LightColor = LightColor;
        item.StackOrder = StackOrder;
        item.TradeAs = TradeAs;
        item.Name = Name;
        item.ServerId = serverId;
    }

    private static uint BuildFlags(DatThingType thing, byte stackOrder, int clientVersion)
    {
        var flags = OldItemFlags.None;
        void Set(OldItemFlags flag, bool enabled)
        {
            if (enabled) flags |= flag;
        }

        Set(OldItemFlags.Unpassable, thing.IsUnpassable);
        Set(OldItemFlags.BlockMissiles, thing.BlockMissile);
        Set(OldItemFlags.BlockPathfinder, thing.BlockPathfinder);
        Set(OldItemFlags.HasElevation, thing.HasElevation);
        // PluginOne/PluginTwo z oryginalnego ItemEditora celowo ignorowały Force Use
        // i Full Ground; PluginThree (10.10+) odwzorowywał obie flagi.
        Set(OldItemFlags.ForceUse, clientVersion >= 1010 && thing.ForceUse);
        Set(OldItemFlags.MultiUse, thing.IsMultiUse);
        Set(OldItemFlags.Pickupable, thing.IsPickupable);
        Set(OldItemFlags.Movable, !thing.IsUnmoveable);
        Set(OldItemFlags.Stackable, thing.IsStackable);
        Set(OldItemFlags.StackOrder, stackOrder != 0);
        Set(OldItemFlags.Readable,
            thing.IsWritable || thing.IsWritableOnce || (thing.HasLensHelp && thing.LensHelp == 1112));
        Set(OldItemFlags.Rotatable, thing.IsRotatable);
        Set(OldItemFlags.Hangable, thing.IsHangable);
        Set(OldItemFlags.HookSouth, thing.IsVertical);
        Set(OldItemFlags.HookEast, thing.IsHorizontal);
        Set(OldItemFlags.IgnoreLook, thing.IgnoreLook);
        Set(OldItemFlags.IsAnimation, thing.FirstGroup?.IsAnimation == true);
        Set(OldItemFlags.FullGround, clientVersion >= 1010 && thing.IsFullGround);
        return (uint)flags;
    }

    private static byte[] ComputeSpriteHash(DatThingType thing, LegacySpriteStore sprites)
    {
        var group = thing.FirstGroup;
        if (group is null) return new byte[16];

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var width = Math.Max(1, (int)group.Width);
        var height = Math.Max(1, (int)group.Height);
        var layers = Math.Max(1, (int)group.Layers);
        var normalized = new byte[32 * 32 * 4];

        for (var layer = 0; layer < layers; layer++)
        {
            for (var tileY = 0; tileY < height; tileY++)
            {
                for (var tileX = 0; tileX < width; tileX++)
                {
                    Array.Fill(normalized, (byte)0);
                    var index = DatParser.CalculateSpriteIndex(group, tileX, tileY, layer, 0, 0, 0, 0);
                    var pixels = index >= 0 && index < group.SpriteIds.Length
                        ? sprites.GetSpritePixels(group.SpriteIds[index])
                        : null;

                    for (var y = 0; y < 32; y++)
                    {
                        for (var x = 0; x < 32; x++)
                        {
                            var destination = (y * 32 + x) * 4;
                            var source = ((31 - y) * 32 + x) * 4;
                            if (pixels is null || pixels[source + 3] == 0)
                            {
                                normalized[destination] = 0x11;
                                normalized[destination + 1] = 0x11;
                                normalized[destination + 2] = 0x11;
                            }
                            else
                            {
                                normalized[destination] = pixels[source];
                                normalized[destination + 1] = pixels[source + 1];
                                normalized[destination + 2] = pixels[source + 2];
                            }

                            normalized[destination + 3] = 0;
                        }
                    }

                    hash.AppendData(normalized);
                }
            }
        }

        return hash.GetHashAndReset();
    }
}

public sealed class OldItemClientWorkspace : IDisposable
{
    private readonly LegacyDatService _dat = new();
    private readonly LegacySpriteStore _sprites = new();
    private readonly Dictionary<ushort, OldItemClientSnapshot> _items = [];
    private readonly Dictionary<ushort, Bitmap?> _previewCache = [];
    private readonly Dictionary<ushort, ulong> _perceptualHashCache = [];

    public string? FolderPath { get; private set; }
    public OldItemEditorClientProfile? Profile { get; private set; }
    public IReadOnlyDictionary<ushort, OldItemClientSnapshot> Items => _items;
    public ushort MinimumClientId => _items.Count == 0 ? (ushort)100 : _items.Keys.Min();
    public ushort MaximumClientId => _items.Count == 0 ? (ushort)100 : _items.Keys.Max();

    public OldClientLoadResult Load(string folderPath, OldItemEditorPreferences? preferences = null)
    {
        if (!Directory.Exists(folderPath))
            throw new DirectoryNotFoundException($"Nie znaleziono folderu klienta: {folderPath}");

        var datPath = FindClientFile(folderPath, ".dat");
        var sprPath = FindClientFile(folderPath, ".spr");
        if (datPath is null || sprPath is null)
            throw new FileNotFoundException("Folder klienta musi zawierać parę Tibia.dat i Tibia.spr.");

        var datSignature = ReadSignature(datPath);
        var sprSignature = ReadSignature(sprPath);
        var profile = OldItemEditorClientCatalog.FindBySignatures(datSignature, sprSignature);
        DatParserOptions? preferred = null;
        if (profile is not null)
        {
            preferred = new DatParserOptions
            {
                MetadataFormat = LegacyObjectBuilderProtocolCatalog.GetMetadataFormat(profile.ClientVersion),
                ExtendedSprites = preferences?.ExtendedSprites == true || profile.ClientVersion >= 960,
                ImprovedAnimations = preferences?.FrameDurations == true || profile.ClientVersion >= 1050,
                FrameGroups = profile.ClientVersion >= 1057
            };
        }
        else if (preferences is not null)
        {
            preferred = new DatParserOptions
            {
                MetadataFormat = DatParser.GuessMetadataFormat(datSignature),
                ExtendedSprites = preferences.ExtendedSprites,
                ImprovedAnimations = preferences.FrameDurations,
                FrameGroups = false
            };
        }

        ClearLoadedData();
        _dat.LoadAuto(datPath, preferred);
        _sprites.Load(sprPath, _dat.Options.ExtendedSprites, preferences?.Transparency);
        var validation = LegacyAssetPairValidator.Validate(_dat.Data, _dat.Options, _sprites.Data);
        if (!validation.IsCompatible)
            throw new InvalidDataException(validation.Error);

        var compatibilityVersion = profile?.ClientVersion ??
            (_dat.Options.MetadataFormat == DatMetadataFormat.Versions1010AndNewer ? 1010 : 986);
        foreach (var thing in _dat.Data?.Items ?? [])
        {
            if (thing.Id <= ushort.MaxValue)
                _items[(ushort)thing.Id] = new OldItemClientSnapshot(thing, _sprites, compatibilityVersion);
        }

        FolderPath = folderPath;
        Profile = profile;
        return new OldClientLoadResult(profile, datPath, sprPath, validation.Warnings);
    }

    public OldItemClientSnapshot? GetItem(ushort clientId) =>
        _items.GetValueOrDefault(clientId);

    public Bitmap? GetPreview(ushort clientId)
    {
        if (_previewCache.TryGetValue(clientId, out var cached)) return cached;
        if (_dat.Data?.Items.FirstOrDefault(thing => thing.Id == clientId) is not { } thing)
            return null;
        var rendered = LegacyThingRenderer.Render(thing, _sprites, 0, 0, 0, 0, 0);
        var bitmap = rendered is null ? null : LegacyBitmapFactory.FromBgra(rendered.Pixels, rendered.Width, rendered.Height);
        _previewCache[clientId] = bitmap;
        return bitmap;
    }

    public IReadOnlyList<(ushort ClientId, int Distance)> FindClosest(ulong sourceHash, int limit = 5)
    {
        return _items.Keys
            .Select(clientId => (ClientId: clientId, Hash: GetPerceptualHash(clientId)))
            .Where(entry => entry.Hash.HasValue)
            .Select(entry => (entry.ClientId, Distance: BitOperations.PopCount(sourceHash ^ entry.Hash!.Value)))
            .OrderBy(entry => entry.Distance)
            .ThenBy(entry => entry.ClientId)
            .Take(Math.Max(1, limit))
            .ToArray();
    }

    public void Dispose() => ClearLoadedData();

    private void ClearLoadedData()
    {
        foreach (var bitmap in _previewCache.Values)
            bitmap?.Dispose();
        _previewCache.Clear();
        _perceptualHashCache.Clear();
        _items.Clear();
        FolderPath = null;
        Profile = null;
    }

    private static string? FindClientFile(string directory, string extension)
    {
        var preferred = Path.Combine(directory, "Tibia" + extension);
        if (File.Exists(preferred)) return preferred;
        return Directory.EnumerateFiles(directory, "*" + extension, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static uint ReadSignature(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 4) throw new InvalidDataException($"Plik {Path.GetFileName(path)} jest za mały.");
        return reader.ReadUInt32();
    }

    public ulong? GetPerceptualHash(ushort clientId)
    {
        if (_perceptualHashCache.TryGetValue(clientId, out var cached)) return cached;
        if (_dat.Data?.Items.FirstOrDefault(thing => thing.Id == clientId) is not { } thing)
            return null;
        var rendered = LegacyThingRenderer.Render(thing, _sprites, 0, 0, 0, 0, 0);
        if (rendered is null) return null;

        Span<byte> samples = stackalloc byte[9 * 8];
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 9; x++)
            {
                var sourceX = Math.Clamp((x * rendered.Width) / 9, 0, rendered.Width - 1);
                var sourceY = Math.Clamp((y * rendered.Height) / 8, 0, rendered.Height - 1);
                var offset = (sourceY * rendered.Width + sourceX) * 4;
                var alpha = rendered.Pixels[offset + 3];
                samples[y * 9 + x] = alpha == 0
                    ? (byte)0
                    : (byte)((rendered.Pixels[offset] * 11 + rendered.Pixels[offset + 1] * 59 + rendered.Pixels[offset + 2] * 30) / 100);
            }
        }

        ulong hash = 0;
        var bit = 0;
        for (var y = 0; y < 8; y++)
        {
            for (var x = 0; x < 8; x++)
            {
                if (samples[y * 9 + x] > samples[y * 9 + x + 1]) hash |= 1UL << bit;
                bit++;
            }
        }

        _perceptualHashCache[clientId] = hash;
        return hash;
    }
}
