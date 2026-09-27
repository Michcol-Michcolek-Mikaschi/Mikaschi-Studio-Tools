using System.Buffers.Binary;
using Google.Protobuf;
using Narzedzia.Core.Assets;
using Narzedzia.Core.Tibia12;
using TibiaAppearances = Narzedzia.Core.Tibia12.Appearances;

namespace Narzedzia.Core.Interchange;

public sealed record AecSpritePayload(byte[] Pixels, int Width, int Height)
{
    public bool IsEmpty => Pixels.Length == 0 || Width == 0 || Height == 0;
}

public sealed record AecEmbeddedAppearance(
    Appearance Appearance,
    IReadOnlyList<AecSpritePayload> Sprites);

/// <summary>
/// Wspólny kodek kontenera AEC. Wersja rozszerzona przechowuje w każdym payloadzie
/// jednoznaczny rozmiar sprite'a, ale nadal odczytuje wcześniejsze surowe payloady BGRA.
/// Tryb zachowania flag jest wybierany przez moduł korzystający z kodeka.
/// </summary>
public static class AecContainerCodec
{
    private static ReadOnlySpan<byte> SpriteMagic => "AECSPRT2"u8;
    private const int SpriteHeaderSize = 16;
    private const int MaxDimension = 4096;

    public static TibiaAppearances BuildExportContainer(
        IEnumerable<Appearance> appearances,
        Func<uint, AecSpritePayload?> readSprite,
        bool preserveFlags = true)
    {
        ArgumentNullException.ThrowIfNull(appearances);
        ArgumentNullException.ThrowIfNull(readSprite);

        var container = new TibiaAppearances();
        foreach (var appearance in appearances)
        {
            var clone = preserveFlags
                ? appearance.Clone()
                : ObjectTransferProfile.Create(appearance);
            clone.SpriteData.Clear();

            var remap = new Dictionary<uint, uint>();
            foreach (var spriteInfo in EnumerateSpriteInfos(clone))
            {
                for (var index = 0; index < spriteInfo.SpriteId.Count; index++)
                {
                    var sourceId = spriteInfo.SpriteId[index];
                    if (!remap.TryGetValue(sourceId, out var embeddedIndex))
                    {
                        var payload = sourceId == 0 ? null : readSprite(sourceId);
                        embeddedIndex = checked((uint)clone.SpriteData.Count);
                        clone.SpriteData.Add(ByteString.CopyFrom(EncodeSpritePayload(payload)));
                        remap[sourceId] = embeddedIndex;
                    }

                    spriteInfo.SpriteId[index] = embeddedIndex;
                }
            }

            AddToContainer(container, clone);
        }

        return container;
    }

    public static TibiaAppearances BuildExportContainer(
        IEnumerable<Appearance> appearances,
        Func<uint, byte[]?> readSpriteBytes,
        bool preserveFlags = true)
    {
        ArgumentNullException.ThrowIfNull(readSpriteBytes);
        return BuildExportContainer(appearances, spriteId =>
        {
            var pixels = readSpriteBytes(spriteId);
            return pixels is null ? null : InferLegacyPayload(pixels);
        }, preserveFlags);
    }

    public static IReadOnlyList<AecEmbeddedAppearance> ReadEmbeddedAppearances(
        TibiaAppearances container,
        bool preserveFlags = true)
    {
        ArgumentNullException.ThrowIfNull(container);
        var result = new List<AecEmbeddedAppearance>();
        foreach (var (appearance, category) in EnumerateAppearances(container))
        {
            var clone = preserveFlags
                ? appearance.Clone()
                : ObjectTransferProfile.Create(appearance);
            // Sekcja kontenera jest źródłem prawdy także dla starszych AEC, w których
            // AppearanceType mogło pozostać domyślną wartością obiektu.
            clone.AppearanceType = category;
            var sprites = clone.SpriteData
                .Select(data => DecodeSpritePayload(data.Span))
                .ToArray();
            clone.SpriteData.Clear();
            ValidateEmbeddedIndexes(clone, sprites.Length);
            result.Add(new AecEmbeddedAppearance(clone, sprites));
        }

        return result;
    }

    public static IReadOnlyList<Appearance> ImportContainer(
        TibiaAppearances container,
        Func<AecSpritePayload, uint> addSpriteAndReturnNewId,
        bool preserveFlags = true)
    {
        ArgumentNullException.ThrowIfNull(addSpriteAndReturnNewId);
        var imported = new List<Appearance>();
        foreach (var embedded in ReadEmbeddedAppearances(container, preserveFlags))
        {
            var clone = embedded.Appearance;
            var spriteIdMap = embedded.Sprites
                .Select(payload => payload.IsEmpty ? 0u : addSpriteAndReturnNewId(payload))
                .ToArray();

            RemapEmbeddedIndexes(clone, spriteIdMap);
            imported.Add(clone);
        }

        return imported;
    }

    public static IReadOnlyList<Appearance> ImportContainer(
        TibiaAppearances container,
        Func<byte[], uint> addSpriteAndReturnNewId,
        bool preserveFlags = true)
    {
        ArgumentNullException.ThrowIfNull(addSpriteAndReturnNewId);
        return ImportContainer(
            container,
            payload => addSpriteAndReturnNewId(payload.Pixels),
            preserveFlags);
    }

    public static byte[] EncodeSpritePayload(AecSpritePayload? payload)
    {
        if (payload is null || payload.IsEmpty)
        {
            return [];
        }

        ValidatePayload(payload.Pixels, payload.Width, payload.Height);
        var encoded = new byte[checked(SpriteHeaderSize + payload.Pixels.Length)];
        SpriteMagic.CopyTo(encoded);
        BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(8, 2), checked((ushort)payload.Width));
        BinaryPrimitives.WriteUInt16LittleEndian(encoded.AsSpan(10, 2), checked((ushort)payload.Height));
        BinaryPrimitives.WriteUInt32LittleEndian(encoded.AsSpan(12, 4), checked((uint)payload.Pixels.Length));
        payload.Pixels.CopyTo(encoded, SpriteHeaderSize);
        return encoded;
    }

    public static AecSpritePayload DecodeSpritePayload(ReadOnlySpan<byte> encoded)
    {
        if (encoded.Length == 0)
        {
            return new AecSpritePayload([], 0, 0);
        }

        if (encoded.Length >= SpriteHeaderSize && encoded[..8].SequenceEqual(SpriteMagic))
        {
            var width = BinaryPrimitives.ReadUInt16LittleEndian(encoded.Slice(8, 2));
            var height = BinaryPrimitives.ReadUInt16LittleEndian(encoded.Slice(10, 2));
            var length = BinaryPrimitives.ReadUInt32LittleEndian(encoded.Slice(12, 4));
            if (length > int.MaxValue || SpriteHeaderSize + (long)length != encoded.Length)
            {
                throw new InvalidDataException("Uszkodzony rozmiar danych sprite'a w kontenerze AEC.");
            }

            var pixels = encoded[SpriteHeaderSize..].ToArray();
            ValidatePayload(pixels, width, height);
            return new AecSpritePayload(pixels, width, height);
        }

        return InferLegacyPayload(encoded.ToArray());
    }

    private static AecSpritePayload InferLegacyPayload(byte[] pixels)
    {
        if (pixels.Length == 0)
        {
            return new AecSpritePayload([], 0, 0);
        }

        if (pixels.Length % 4 != 0)
        {
            throw new InvalidDataException("Starszy payload AEC nie zawiera pełnych pikseli BGRA.");
        }

        var pixelCount = pixels.Length / 4;
        for (var type = 0; type < 36; type++)
        {
            var layout = SpriteSheetLayout.FromSpriteType(type);
            if (layout.TileWidth * layout.TileHeight == pixelCount)
            {
                return new AecSpritePayload(pixels, layout.TileWidth, layout.TileHeight);
            }
        }

        var square = (int)Math.Sqrt(pixelCount);
        if (square * square == pixelCount)
        {
            return new AecSpritePayload(pixels, square, square);
        }

        throw new InvalidDataException($"Nie można rozpoznać wymiarów starszego sprite'a AEC ({pixels.Length} bajtów)." );
    }

    private static void ValidatePayload(byte[] pixels, int width, int height)
    {
        if (width is <= 0 or > MaxDimension || height is <= 0 or > MaxDimension)
        {
            throw new InvalidDataException($"Nieprawidłowy rozmiar sprite'a AEC: {width}×{height}.");
        }

        var expected = checked(width * height * 4);
        if (pixels.Length != expected)
        {
            throw new InvalidDataException(
                $"Sprite AEC {width}×{height} wymaga {expected} bajtów BGRA, otrzymano {pixels.Length}.");
        }
    }

    private static void ValidateEmbeddedIndexes(Appearance appearance, int spriteCount)
    {
        foreach (var spriteInfo in EnumerateSpriteInfos(appearance))
        foreach (var embeddedIndex in spriteInfo.SpriteId)
        {
            if (embeddedIndex >= spriteCount)
            {
                throw new InvalidDataException(
                    $"Obiekt AEC #{appearance.Id} wskazuje brakujący sprite osadzony #{embeddedIndex}.");
            }
        }
    }

    private static void RemapEmbeddedIndexes(Appearance appearance, IReadOnlyList<uint> spriteIdMap)
    {
        foreach (var spriteInfo in EnumerateSpriteInfos(appearance))
        for (var index = 0; index < spriteInfo.SpriteId.Count; index++)
        {
            spriteInfo.SpriteId[index] = spriteIdMap[checked((int)spriteInfo.SpriteId[index])];
        }
    }

    private static IEnumerable<(Appearance Appearance, APPEARANCE_TYPE Category)> EnumerateAppearances(
        TibiaAppearances container)
    {
        foreach (var item in container.Object) yield return (item, APPEARANCE_TYPE.AppearanceObject);
        foreach (var outfit in container.Outfit) yield return (outfit, APPEARANCE_TYPE.AppearanceOutfit);
        foreach (var effect in container.Effect) yield return (effect, APPEARANCE_TYPE.AppearanceEffect);
        foreach (var missile in container.Missile) yield return (missile, APPEARANCE_TYPE.AppearanceMissile);
    }

    private static IEnumerable<SpriteInfo> EnumerateSpriteInfos(Appearance appearance)
    {
        foreach (var group in appearance.FrameGroup)
        {
            if (group.SpriteInfo is { } spriteInfo) yield return spriteInfo;
        }
    }

    private static void AddToContainer(TibiaAppearances container, Appearance appearance)
    {
        switch (appearance.AppearanceType)
        {
            case APPEARANCE_TYPE.AppearanceOutfit: container.Outfit.Add(appearance); break;
            case APPEARANCE_TYPE.AppearanceEffect: container.Effect.Add(appearance); break;
            case APPEARANCE_TYPE.AppearanceMissile: container.Missile.Add(appearance); break;
            default: container.Object.Add(appearance); break;
        }
    }
}
