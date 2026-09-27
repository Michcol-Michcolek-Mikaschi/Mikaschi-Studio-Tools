using Narzedzia.Core.Appearances;
using Narzedzia.Core.Models;
using Narzedzia.Core.Tibia12;

namespace Narzedzia.Core.Interchange;

public sealed record LegacyObjectConversion(
    DatThingType Thing,
    IReadOnlyDictionary<uint, byte[]> TileSprites,
    IReadOnlyList<string> Warnings);

public sealed record ModernObjectConversion(
    Appearance Appearance,
    IReadOnlyDictionary<uint, AecSpritePayload> Sprites,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Dwukierunkowa konwersja modelu appearances (nowy protokół) i DAT/OBD
/// (stary protokół). Nowe złożone sprite'y są dzielone na kafle 32×32, a kafle
/// legacy są scalane do rozmiarów obsługiwanych przez katalog assets.
/// </summary>
public static class LegacyAppearanceConverter
{
    private const int TileSize = 32;
    private const int TileByteCount = TileSize * TileSize * 4;
    private static readonly int[] AssetDimensions = [32, 64, 96, 128, 192, 384];

    public static LegacyObjectConversion ToLegacy(
        Appearance appearance,
        APPEARANCE_TYPE category,
        Func<uint, AecSpritePayload?> readSprite,
        bool targetSupportsFrameGroups = true)
    {
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(readSprite);

        var warnings = new List<string>();
        var thing = new DatThingType
        {
            Id = appearance.Id,
            Category = ToLegacyCategory(category)
        };
        CopyFlagsToLegacy(appearance, thing, warnings);

        uint nextSpriteId = 1;
        var tileSprites = new Dictionary<uint, byte[]>();
        IEnumerable<FrameGroup> sourceGroups = appearance.FrameGroup;
        if (category == APPEARANCE_TYPE.AppearanceOutfit && !targetSupportsFrameGroups)
        {
            // Klasyczne DAT (bez frame groups) ma jedną wspólną animację stroju.
            // Najpełniejszym odpowiednikiem jest grupa Walking: jej pierwsza faza
            // jest pozycją stojącą, a kolejne fazy tworzą krok.
            var sharedGroup = appearance.FrameGroup.FirstOrDefault(group =>
                                  group.FixedFrameGroup == FIXED_FRAME_GROUP.OutfitMoving)
                              ?? appearance.FrameGroup.FirstOrDefault();
            sourceGroups = sharedGroup is null ? [] : [sharedGroup];

            if (appearance.FrameGroup.Count > 1)
            {
                warnings.Add(
                    $"Strój #{appearance.Id}: format docelowy nie obsługuje osobnych grup Idle/Walking; " +
                    "zapisano wspólną animację na podstawie grupy Walking.");
            }
        }

        foreach (var sourceGroup in sourceGroups)
        {
            var info = sourceGroup.SpriteInfo;
            if (info is null)
            {
                warnings.Add($"Obiekt #{appearance.Id}: pominięto grupę bez SpriteInfo.");
                continue;
            }

            var layout = AppearanceLayoutInfo.From(info);
            ValidateLegacyDimensions(layout, appearance.Id);
            var payloadCache = new Dictionary<uint, AecSpritePayload?>();
            AecSpritePayload? GetPayload(uint id)
            {
                if (!payloadCache.TryGetValue(id, out var payload))
                {
                    payload = readSprite(id);
                    if (payload is { IsEmpty: false }) ValidatePayload(payload, appearance.Id, id);
                    payloadCache[id] = payload;
                }
                return payload;
            }

            var (cellWidth, cellHeight) = ResolveCellSize(info, GetPayload);
            var widthTiles = checked(layout.TileWidth * (cellWidth / TileSize));
            var heightTiles = checked(layout.TileHeight * (cellHeight / TileSize));
            if (widthTiles > byte.MaxValue || heightTiles > byte.MaxValue)
            {
                throw new InvalidDataException(
                    $"Obiekt #{appearance.Id} przekracza limit rozmiaru legacy 255×255 kafli.");
            }

            var targetGroup = new DatThingFrameGroup
            {
                GroupType = category == APPEARANCE_TYPE.AppearanceOutfit &&
                            sourceGroup.FixedFrameGroup == FIXED_FRAME_GROUP.OutfitMoving ? 1 : 0,
                Width = (byte)widthTiles,
                Height = (byte)heightTiles,
                ExactSize = ToByte(info.BoundingSquare == 0
                    ? (uint)Math.Min(byte.MaxValue, Math.Max(widthTiles, heightTiles) * TileSize)
                    : info.BoundingSquare, "bounding square", warnings),
                Layers = checked((byte)layout.Layers),
                PatternX = checked((byte)layout.PatternX),
                PatternY = checked((byte)layout.PatternY),
                PatternZ = checked((byte)layout.PatternZ),
                Frames = checked((byte)layout.Frames)
            };
            CopyAnimationToLegacy(info, targetGroup, warnings);
            targetGroup.SpriteIds = new uint[targetGroup.TotalSprites];

            for (var frame = 0; frame < layout.Frames; frame++)
            for (var patternZ = 0; patternZ < layout.PatternZ; patternZ++)
            for (var patternY = 0; patternY < layout.PatternY; patternY++)
            for (var patternX = 0; patternX < layout.PatternX; patternX++)
            for (var layer = 0; layer < layout.Layers; layer++)
            {
                var canvasWidth = checked(layout.TileWidth * cellWidth);
                var canvasHeight = checked(layout.TileHeight * cellHeight);
                var canvas = new byte[checked(canvasWidth * canvasHeight * 4)];

                for (var tileY = 0; tileY < layout.TileHeight; tileY++)
                for (var tileX = 0; tileX < layout.TileWidth; tileX++)
                {
                    var sourceIndex = CalculateAppearanceIndex(
                        layout, tileX, tileY, layer, patternX, patternY, patternZ, frame);
                    if (sourceIndex >= info.SpriteId.Count) continue;
                    var payload = GetPayload(info.SpriteId[sourceIndex]);
                    if (payload is null || payload.IsEmpty) continue;

                    var destinationX = (layout.TileWidth - tileX - 1) * cellWidth + (cellWidth - payload.Width);
                    var destinationY = (layout.TileHeight - tileY - 1) * cellHeight + (cellHeight - payload.Height);
                    CopyPixels(payload.Pixels, payload.Width, payload.Height, canvas, canvasWidth, destinationX, destinationY);
                }

                for (var oldTileY = 0; oldTileY < heightTiles; oldTileY++)
                for (var oldTileX = 0; oldTileX < widthTiles; oldTileX++)
                {
                    var sourceX = (widthTiles - oldTileX - 1) * TileSize;
                    var sourceY = (heightTiles - oldTileY - 1) * TileSize;
                    var tile = ExtractTile(canvas, canvasWidth, sourceX, sourceY);
                    var spriteId = IsTransparent(tile) ? 0u : nextSpriteId++;
                    if (spriteId != 0) tileSprites[spriteId] = tile;

                    var targetIndex = CalculateLegacyIndex(
                        targetGroup, oldTileX, oldTileY, layer, patternX, patternY, patternZ, frame);
                    targetGroup.SpriteIds[targetIndex] = spriteId;
                }
            }

            thing.FrameGroups.Add(targetGroup);
        }

        if (thing.FrameGroups.Count == 0)
        {
            thing.FrameGroups.Add(CreateDefaultLegacyGroup());
        }

        return new LegacyObjectConversion(thing, tileSprites, warnings);
    }

    public static ModernObjectConversion ToAppearance(
        DatThingType thing,
        Func<uint, byte[]?> readLegacyTile,
        bool sourceUsesFrameGroups = true)
    {
        ArgumentNullException.ThrowIfNull(thing);
        ArgumentNullException.ThrowIfNull(readLegacyTile);

        var warnings = new List<string>();
        var appearance = new Appearance
        {
            Id = thing.Id,
            AppearanceType = ToAppearanceCategory(thing.Category),
            Flags = CopyFlagsToAppearance(thing)
        };
        if (!string.IsNullOrWhiteSpace(thing.MarketName)) appearance.Name = thing.MarketName;
        if (thing.HasCharges || thing.FloorChange || thing.HasBones)
        {
            warnings.Add(
                $"Obiekt #{thing.Id}: flagi Charges/FloorChange/Bones nie mają odpowiednika w nowym protokole appearances.");
        }

        uint nextSpriteId = 1;
        var sprites = new Dictionary<uint, AecSpritePayload>();
        foreach (var sourceGroup in thing.FrameGroups.DefaultIfEmpty(CreateDefaultLegacyGroup()))
        {
            var widthTiles = Math.Max(1, (int)sourceGroup.Width);
            var heightTiles = Math.Max(1, (int)sourceGroup.Height);
            var layers = Math.Max(1, (int)sourceGroup.Layers);
            var patternX = Math.Max(1, (int)sourceGroup.PatternX);
            var patternY = Math.Max(1, (int)sourceGroup.PatternY);
            var patternZ = Math.Max(1, (int)sourceGroup.PatternZ);
            var frames = Math.Max(1, (int)sourceGroup.Frames);
            var objectWidth = checked(widthTiles * TileSize);
            var objectHeight = checked(heightTiles * TileSize);
            var spriteWidth = NextAssetDimension(objectWidth, thing.Id, "szerokości");
            var spriteHeight = NextAssetDimension(objectHeight, thing.Id, "wysokości");

            var spriteInfo = new SpriteInfo
            {
                PatternWidth = (uint)patternX,
                PatternHeight = (uint)patternY,
                PatternDepth = (uint)patternZ,
                Layers = (uint)layers,
                BoundingSquare = sourceGroup.ExactSize == 0
                    ? (uint)Math.Max(objectWidth, objectHeight)
                    : sourceGroup.ExactSize,
                IsOpaque = false
            };
            CopyAnimationToAppearance(sourceGroup, spriteInfo);
            if (sourceGroup.SpriteIds.Length != sourceGroup.TotalSprites)
            {
                warnings.Add(
                    $"Obiekt #{thing.Id}: grupa ma {sourceGroup.SpriteIds.Length} ID sprite'ów, oczekiwano {sourceGroup.TotalSprites}; brakujące pola wypełniono przezroczystością.");
            }

            for (var frame = 0; frame < frames; frame++)
            for (var z = 0; z < patternZ; z++)
            for (var y = 0; y < patternY; y++)
            for (var x = 0; x < patternX; x++)
            for (var layer = 0; layer < layers; layer++)
            {
                var canvas = new byte[checked(spriteWidth * spriteHeight * 4)];
                var paddingX = spriteWidth - objectWidth;
                var paddingY = spriteHeight - objectHeight;
                for (var tileY = 0; tileY < heightTiles; tileY++)
                for (var tileX = 0; tileX < widthTiles; tileX++)
                {
                    var sourceIndex = CalculateLegacyIndex(
                        sourceGroup, tileX, tileY, layer, x, y, z, frame);
                    if (sourceIndex >= sourceGroup.SpriteIds.Length) continue;
                    var tile = readLegacyTile(sourceGroup.SpriteIds[sourceIndex]);
                    if (tile is null || tile.Length == 0) continue;
                    if (tile.Length != TileByteCount)
                    {
                        throw new InvalidDataException(
                            $"Obiekt #{thing.Id}: sprite legacy #{sourceGroup.SpriteIds[sourceIndex]} nie ma rozmiaru 32×32 BGRA.");
                    }

                    var destinationX = paddingX + (widthTiles - tileX - 1) * TileSize;
                    var destinationY = paddingY + (heightTiles - tileY - 1) * TileSize;
                    CopyPixels(tile, TileSize, TileSize, canvas, spriteWidth, destinationX, destinationY);
                }

                var spriteId = IsTransparent(canvas) ? 0u : nextSpriteId++;
                spriteInfo.SpriteId.Add(spriteId);
                if (spriteId != 0)
                {
                    sprites[spriteId] = new AecSpritePayload(canvas, spriteWidth, spriteHeight);
                }
            }

            appearance.FrameGroup.Add(new FrameGroup
            {
                FixedFrameGroup = thing.Category == DatThingCategory.Outfits
                    ? sourceGroup.GroupType == 1
                        ? FIXED_FRAME_GROUP.OutfitMoving
                        : FIXED_FRAME_GROUP.OutfitIdle
                    : FIXED_FRAME_GROUP.ObjectInitial,
                SpriteInfo = spriteInfo
            });
        }

        if (thing.Category == DatThingCategory.Outfits &&
            !sourceUsesFrameGroups &&
            appearance.FrameGroup.Count == 1)
        {
            SplitSharedLegacyOutfitGroup(appearance, warnings);
        }

        return new ModernObjectConversion(appearance, sprites, warnings);
    }

    private static void SplitSharedLegacyOutfitGroup(
        Appearance appearance,
        ICollection<string> warnings)
    {
        var movingGroup = appearance.FrameGroup[0];
        var movingInfo = movingGroup.SpriteInfo;
        if (movingInfo is null)
        {
            return;
        }

        var layout = AppearanceLayoutInfo.From(movingInfo);
        var spritesPerFrame = Math.Max(1, layout.TotalSprites / Math.Max(1, layout.Frames));
        var idleInfo = movingInfo.Clone();
        idleInfo.Animation = null;
        idleInfo.ClearPatternFrames();
        idleInfo.SpriteId.Clear();
        idleInfo.SpriteId.Add(movingInfo.SpriteId.Take(spritesPerFrame));

        movingGroup.FixedFrameGroup = FIXED_FRAME_GROUP.OutfitMoving;
        appearance.FrameGroup.Insert(0, new FrameGroup
        {
            FixedFrameGroup = FIXED_FRAME_GROUP.OutfitIdle,
            SpriteInfo = idleInfo
        });

        warnings.Add(
            $"Strój #{appearance.Id}: wspólną grupę starego DAT rozdzielono na Idle " +
            $"(pierwsza poza) i Walking ({layout.Frames} klatek).");
    }

    private static void CopyAnimationToLegacy(
        SpriteInfo source,
        DatThingFrameGroup target,
        ICollection<string> warnings)
    {
        var frames = Math.Max(1, (int)target.Frames);
        if (frames <= 1) return;

        var animation = source.Animation;
        target.FrameDurations = new DatFrameDuration[frames];
        for (var index = 0; index < frames; index++)
        {
            var phase = animation is not null && index < animation.SpritePhase.Count
                ? animation.SpritePhase[index]
                : null;
            target.FrameDurations[index] = new DatFrameDuration
            {
                Min = phase?.DurationMin ?? 100,
                Max = phase?.DurationMax ?? 100
            };
        }

        if (animation is null) return;
        target.AnimationMode = animation.Synchronized ||
                               animation.AnimationMode == ANIMATION_ANIMATION_MODE.AnimationSynchronized
            ? (byte)1
            : (byte)0;
        target.LoopCount = animation.LoopType switch
        {
            ANIMATION_LOOP_TYPE.Pingpong => -1,
            ANIMATION_LOOP_TYPE.Infinite => 0,
            _ => (int)Math.Min(int.MaxValue, animation.LoopCount)
        };
        target.StartFrame = animation.RandomStartPhase
            ? (sbyte)-1
            : (sbyte)Math.Clamp(animation.DefaultStartPhase, 0u, (uint)Math.Min(sbyte.MaxValue, frames - 1));

        if (animation.LoopCount > int.MaxValue)
        {
            warnings.Add("Liczba pętli animacji przekracza zakres starego protokołu i została ograniczona.");
        }
    }

    private static void CopyAnimationToAppearance(DatThingFrameGroup source, SpriteInfo target)
    {
        var frames = Math.Max(1, (int)source.Frames);
        if (frames <= 1) return;

        var animation = new SpriteAnimation
        {
            Synchronized = source.AnimationMode == 1,
            AnimationMode = source.AnimationMode == 1
                ? ANIMATION_ANIMATION_MODE.AnimationSynchronized
                : ANIMATION_ANIMATION_MODE.AnimationAsynchronized,
            RandomStartPhase = source.StartFrame < 0,
            DefaultStartPhase = source.StartFrame < 0 ? 0u : (uint)source.StartFrame,
            LoopType = source.LoopCount switch
            {
                < 0 => ANIMATION_LOOP_TYPE.Pingpong,
                0 => ANIMATION_LOOP_TYPE.Infinite,
                _ => ANIMATION_LOOP_TYPE.Counted
            },
            LoopCount = source.LoopCount > 0 ? (uint)source.LoopCount : 0u
        };
        for (var index = 0; index < frames; index++)
        {
            var duration = index < source.FrameDurations.Length
                ? source.FrameDurations[index]
                : new DatFrameDuration { Min = 100, Max = 100 };
            animation.SpritePhase.Add(new SpritePhase
            {
                DurationMin = duration.Min,
                DurationMax = Math.Max(duration.Min, duration.Max)
            });
        }
        target.Animation = animation;
    }

    private static AppearanceFlags CopyFlagsToAppearance(DatThingType thing)
    {
        var flags = new AppearanceFlags();
        if (thing.IsGround) flags.Bank = new AppearanceFlagBank { Waypoints = thing.GroundSpeed };
        if (thing.IsGroundBorder) flags.Clip = true;
        if (thing.IsOnBottom) flags.Bottom = true;
        if (thing.IsOnTop) flags.Top = true;
        if (thing.IsContainer) flags.Container = true;
        if (thing.IsStackable) flags.Cumulative = true;
        if (thing.IsUsable) flags.Usable = true;
        if (thing.ForceUse) flags.Forceuse = true;
        if (thing.IsMultiUse) flags.Multiuse = true;
        if (thing.IsWritable) flags.Write = new AppearanceFlagWrite { MaxTextLength = thing.MaxReadWriteChars };
        if (thing.IsWritableOnce) flags.WriteOnce = new AppearanceFlagWriteOnce { MaxTextLengthOnce = thing.MaxReadChars };
        if (thing.IsFluid) flags.Liquidpool = true;
        if (thing.IsUnpassable) flags.Unpass = true;
        if (thing.IsUnmoveable) flags.Unmove = true;
        if (thing.BlockMissile) flags.Unsight = true;
        if (thing.BlockPathfinder) flags.Avoid = true;
        if (thing.NoMoveAnimation) flags.NoMovementAnimation = true;
        if (thing.IsPickupable) flags.Take = true;
        if (thing.IsFluidContainer) flags.Liquidcontainer = true;
        if (thing.IsHangable) flags.Hang = true;
        if (thing.IsVertical)
        {
            flags.Hook = new AppearanceFlagHook { Direction = HOOK_TYPE.South };
            flags.HookSouth = true;
        }
        if (thing.IsHorizontal)
        {
            flags.Hook = new AppearanceFlagHook { Direction = HOOK_TYPE.East };
            flags.HookEast = true;
        }
        if (thing.IsRotatable) flags.Rotate = true;
        if (thing.HasLight) flags.Light = new AppearanceFlagLight { Brightness = thing.LightLevel, Color = thing.LightColor };
        if (thing.DontHide) flags.DontHide = true;
        if (thing.IsTranslucent) flags.Translucent = true;
        if (thing.HasOffset)
        {
            flags.Shift = new AppearanceFlagShift
            {
                X = unchecked((uint)thing.OffsetX),
                Y = unchecked((uint)thing.OffsetY)
            };
        }
        if (thing.HasElevation) flags.Height = new AppearanceFlagHeight { Elevation = thing.Elevation };
        if (thing.IsLyingObject) flags.LyingObject = true;
        if (thing.AnimateAlways) flags.AnimateAlways = true;
        if (thing.HasMiniMapColor) flags.Automap = new AppearanceFlagAutomap { Color = thing.MiniMapColor };
        if (thing.HasLensHelp) flags.Lenshelp = new AppearanceFlagLenshelp { Id = thing.LensHelp };
        if (thing.IsFullGround) flags.Fullbank = true;
        if (thing.IgnoreLook) flags.IgnoreLook = true;
        if (thing.HasCloth) flags.Clothes = new AppearanceFlagClothes { Slot = thing.ClothSlot };
        if (thing.HasDefaultAction)
        {
            flags.DefaultAction = new AppearanceFlagDefaultAction { Action = (PLAYER_ACTION)thing.DefaultAction };
        }
        if (thing.HasMarketInfo)
        {
            var vocation = Enum.IsDefined(typeof(VOCATION), (int)thing.MarketRestrictProfession)
                ? (VOCATION)thing.MarketRestrictProfession
                : VOCATION.None;
            flags.Market = new AppearanceFlagMarket
            {
                Category = (ITEM_CATEGORY)thing.MarketCategory,
                TradeAsObjectId = thing.MarketTradeAs,
                ShowAsObjectId = thing.MarketShowAs,
                Name = thing.MarketName ?? string.Empty,
                MinimumLevel = thing.MarketRestrictLevel,
                Vocation = vocation
            };
            flags.Market.RestrictToVocation.Add(vocation);
        }
        if (thing.IsWrappable) flags.Wrap = true;
        if (thing.IsUnwrappable) flags.Unwrap = true;
        if (thing.IsTopEffect) flags.Topeffect = true;
        return flags;
    }

    private static void CopyFlagsToLegacy(
        Appearance appearance,
        DatThingType thing,
        ICollection<string> warnings)
    {
        var flags = appearance.Flags;
        if (flags is null) return;

        thing.IsGround = flags.Bank is not null;
        if (flags.Bank is not null) thing.GroundSpeed = ToUShort(flags.Bank.Waypoints, "Ground speed", warnings);
        thing.IsGroundBorder = flags.Clip;
        thing.IsOnBottom = flags.Bottom;
        thing.IsOnTop = flags.Top;
        thing.IsContainer = flags.Container;
        thing.IsStackable = flags.Cumulative;
        thing.IsUsable = flags.Usable;
        thing.ForceUse = flags.Forceuse;
        thing.IsMultiUse = flags.Multiuse;
        thing.IsWritable = flags.Write is not null;
        if (flags.Write is not null) thing.MaxReadWriteChars = ToUShort(flags.Write.MaxTextLength, "Write length", warnings);
        thing.IsWritableOnce = flags.WriteOnce is not null;
        if (flags.WriteOnce is not null) thing.MaxReadChars = ToUShort(flags.WriteOnce.MaxTextLengthOnce, "Write once length", warnings);
        thing.IsFluid = flags.Liquidpool;
        thing.IsUnpassable = flags.Unpass;
        thing.IsUnmoveable = flags.Unmove;
        thing.BlockMissile = flags.Unsight;
        thing.BlockPathfinder = flags.Avoid;
        thing.NoMoveAnimation = flags.NoMovementAnimation;
        thing.IsPickupable = flags.Take;
        thing.IsFluidContainer = flags.Liquidcontainer;
        thing.IsHangable = flags.Hang;

        var hookDirection = flags.Hook?.Direction;
        thing.IsVertical = hookDirection == HOOK_TYPE.South || flags.HookSouth;
        thing.IsHorizontal = hookDirection == HOOK_TYPE.East || flags.HookEast;
        thing.IsRotatable = flags.Rotate;
        thing.HasLight = flags.Light is not null;
        if (flags.Light is not null)
        {
            thing.LightLevel = ToUShort(flags.Light.Brightness, "Light brightness", warnings);
            thing.LightColor = ToUShort(flags.Light.Color, "Light color", warnings);
        }
        thing.DontHide = flags.DontHide;
        thing.IsTranslucent = flags.Translucent;
        thing.HasOffset = flags.Shift is not null;
        if (flags.Shift is not null)
        {
            thing.OffsetX = ToShort(unchecked((int)flags.Shift.X), "Shift X", warnings);
            thing.OffsetY = ToShort(unchecked((int)flags.Shift.Y), "Shift Y", warnings);
        }
        thing.HasElevation = flags.Height is not null;
        if (flags.Height is not null) thing.Elevation = ToUShort(flags.Height.Elevation, "Elevation", warnings);
        thing.IsLyingObject = flags.LyingObject;
        thing.AnimateAlways = flags.AnimateAlways;
        thing.HasMiniMapColor = flags.Automap is not null;
        if (flags.Automap is not null) thing.MiniMapColor = ToUShort(flags.Automap.Color, "Automap color", warnings);
        thing.HasLensHelp = flags.Lenshelp is not null;
        if (flags.Lenshelp is not null) thing.LensHelp = ToUShort(flags.Lenshelp.Id, "Lens help", warnings);
        thing.IsFullGround = flags.Fullbank;
        thing.IgnoreLook = flags.IgnoreLook;
        thing.HasCloth = flags.Clothes is not null;
        if (flags.Clothes is not null) thing.ClothSlot = ToUShort(flags.Clothes.Slot, "Clothes slot", warnings);
        thing.HasDefaultAction = flags.DefaultAction is not null;
        if (flags.DefaultAction is not null) thing.DefaultAction = ToUShort((uint)flags.DefaultAction.Action, "Default action", warnings);
        thing.HasMarketInfo = flags.Market is not null;
        if (flags.Market is not null)
        {
            var market = flags.Market;
            thing.MarketCategory = ToUShort((uint)market.Category, "Market category", warnings);
            thing.MarketTradeAs = ToUShort(market.TradeAsObjectId, "Market trade-as", warnings);
            thing.MarketShowAs = ToUShort(market.ShowAsObjectId, "Market show-as", warnings);
            thing.MarketName = market.Name ?? appearance.Name ?? string.Empty;
            var vocation = market.RestrictToVocation.Count > 0
                ? market.RestrictToVocation[0]
                : market.HasVocation ? market.Vocation : VOCATION.None;
            thing.MarketRestrictProfession = (ushort)Math.Clamp((int)vocation, 0, ushort.MaxValue);
            thing.MarketRestrictLevel = ToUShort(market.MinimumLevel, "Market level", warnings);
            if (market.RestrictToVocation.Count > 1)
            {
                warnings.Add("Stary protokół rynku obsługuje jedną profesję; zachowano pierwszą pozycję listy.");
            }
        }
        thing.IsWrappable = flags.Wrap;
        thing.IsUnwrappable = flags.Unwrap;
        thing.IsTopEffect = flags.Topeffect;

        if (HasNewProtocolOnlyFlags(flags))
        {
            warnings.Add(
                $"Obiekt #{appearance.Id}: pominięto flagi dostępne wyłącznie w nowym protokole, których standard OBD nie zapisuje.");
        }
        if (!string.IsNullOrWhiteSpace(appearance.Description) ||
            (!thing.HasMarketInfo && !string.IsNullOrWhiteSpace(appearance.Name)))
        {
            warnings.Add($"Obiekt #{appearance.Id}: standard OBD nie przechowuje nazwy ani opisu obiektu.");
        }
    }

    private static bool HasNewProtocolOnlyFlags(AppearanceFlags flags) =>
        flags.Npcsaledata.Count > 0 ||
        flags.Changedtoexpire is not null ||
        flags.Corpse || flags.PlayerCorpse || flags.Cyclopediaitem is not null ||
        flags.Ammo || flags.ShowOffSocket || flags.Reportable ||
        flags.Upgradeclassification is not null ||
        flags.ReverseAddonsEast || flags.ReverseAddonsWest ||
        flags.ReverseAddonsSouth || flags.ReverseAddonsNorth ||
        flags.Wearout || flags.Clockexpire || flags.Expire || flags.Expirestop ||
        flags.DecoItemKit || flags.SkillwheelGem is not null || flags.DualWielding ||
        flags.Imbueable is not null || flags.Proficiency is not null ||
        flags.RestrictToVocation.Count > 0 || flags.HasMinimumLevel || flags.HasWeaponType ||
        flags.Transparencylevel is not null;

    private static (int Width, int Height) ResolveCellSize(
        SpriteInfo info,
        Func<uint, AecSpritePayload?> readSprite)
    {
        var maxWidth = TileSize;
        var maxHeight = TileSize;
        foreach (var id in info.SpriteId.Distinct())
        {
            var payload = readSprite(id);
            if (payload is null || payload.IsEmpty) continue;
            maxWidth = Math.Max(maxWidth, payload.Width);
            maxHeight = Math.Max(maxHeight, payload.Height);
        }

        return (RoundUpToTile(maxWidth), RoundUpToTile(maxHeight));
    }

    private static int CalculateAppearanceIndex(
        AppearanceLayoutInfo layout,
        int tileX,
        int tileY,
        int layer,
        int patternX,
        int patternY,
        int patternZ,
        int frame)
    {
        var index = frame;
        index = index * layout.PatternZ + patternZ;
        index = index * layout.PatternY + patternY;
        index = index * layout.PatternX + patternX;
        index = index * layout.Layers + layer;
        index = index * layout.TileHeight + tileY;
        index = index * layout.TileWidth + tileX;
        return index;
    }

    private static int CalculateLegacyIndex(
        DatThingFrameGroup group,
        int tileX,
        int tileY,
        int layer,
        int patternX,
        int patternY,
        int patternZ,
        int frame)
    {
        var index = frame;
        index = index * Math.Max(1, (int)group.PatternZ) + patternZ;
        index = index * Math.Max(1, (int)group.PatternY) + patternY;
        index = index * Math.Max(1, (int)group.PatternX) + patternX;
        index = index * Math.Max(1, (int)group.Layers) + layer;
        index = index * Math.Max(1, (int)group.Height) + tileY;
        index = index * Math.Max(1, (int)group.Width) + tileX;
        return index;
    }

    private static byte[] ExtractTile(byte[] source, int sourceWidth, int x, int y)
    {
        var tile = new byte[TileByteCount];
        for (var row = 0; row < TileSize; row++)
        {
            Buffer.BlockCopy(source, ((y + row) * sourceWidth + x) * 4, tile, row * TileSize * 4, TileSize * 4);
        }
        NormalizeTransparency(tile);
        return tile;
    }

    private static void CopyPixels(
        byte[] source,
        int sourceWidth,
        int sourceHeight,
        byte[] destination,
        int destinationWidth,
        int destinationX,
        int destinationY)
    {
        for (var row = 0; row < sourceHeight; row++)
        {
            var sourceOffset = row * sourceWidth * 4;
            var destinationOffset = ((destinationY + row) * destinationWidth + destinationX) * 4;
            Buffer.BlockCopy(source, sourceOffset, destination, destinationOffset, sourceWidth * 4);
        }
        NormalizeTransparency(destination);
    }

    private static void NormalizeTransparency(byte[] pixels)
    {
        for (var offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            if (pixels[offset] == 255 && pixels[offset + 1] == 0 && pixels[offset + 2] == 255)
            {
                pixels[offset] = 0;
                pixels[offset + 1] = 0;
                pixels[offset + 2] = 0;
                pixels[offset + 3] = 0;
            }
        }
    }

    private static bool IsTransparent(byte[] pixels)
    {
        for (var offset = 3; offset < pixels.Length; offset += 4)
        {
            if (pixels[offset] != 0) return false;
        }
        return true;
    }

    private static int RoundUpToTile(int value) => checked((value + TileSize - 1) / TileSize * TileSize);

    private static int NextAssetDimension(int required, uint objectId, string axis)
    {
        foreach (var dimension in AssetDimensions)
        {
            if (dimension >= required) return dimension;
        }
        throw new InvalidDataException(
            $"Obiekt #{objectId} wymaga {axis} {required}px, a nowy format assets obsługuje maksymalnie 384px.");
    }

    private static void ValidatePayload(AecSpritePayload payload, uint objectId, uint spriteId)
    {
        if (payload.Width <= 0 || payload.Height <= 0 ||
            payload.Pixels.Length != checked(payload.Width * payload.Height * 4))
        {
            throw new InvalidDataException(
                $"Obiekt #{objectId}: sprite #{spriteId} ma niespójny rozmiar {payload.Width}×{payload.Height}.");
        }
    }

    private static void ValidateLegacyDimensions(AppearanceLayoutInfo layout, uint objectId)
    {
        if (layout.TileWidth is <= 0 or > byte.MaxValue || layout.TileHeight is <= 0 or > byte.MaxValue ||
            layout.Layers is <= 0 or > byte.MaxValue || layout.PatternX is <= 0 or > byte.MaxValue ||
            layout.PatternY is <= 0 or > byte.MaxValue || layout.PatternZ is <= 0 or > byte.MaxValue ||
            layout.Frames is <= 0 or > byte.MaxValue)
        {
            throw new InvalidDataException(
                $"Obiekt #{objectId} ma układ tekstury przekraczający limit starego protokołu (255)." );
        }
    }

    private static ushort ToUShort(uint value, string field, ICollection<string> warnings)
    {
        if (value <= ushort.MaxValue) return (ushort)value;
        warnings.Add($"{field}: wartość {value} ograniczono do {ushort.MaxValue} dla starego protokołu.");
        return ushort.MaxValue;
    }

    private static short ToShort(int value, string field, ICollection<string> warnings)
    {
        if (value is >= short.MinValue and <= short.MaxValue) return (short)value;
        var converted = (short)Math.Clamp(value, short.MinValue, short.MaxValue);
        warnings.Add($"{field}: wartość {value} ograniczono do {converted} dla starego protokołu.");
        return converted;
    }

    private static byte ToByte(uint value, string field, ICollection<string> warnings)
    {
        if (value <= byte.MaxValue) return (byte)value;
        warnings.Add($"{field}: wartość {value} ograniczono do {byte.MaxValue} dla starego protokołu.");
        return byte.MaxValue;
    }

    private static DatThingCategory ToLegacyCategory(APPEARANCE_TYPE category) => category switch
    {
        APPEARANCE_TYPE.AppearanceOutfit => DatThingCategory.Outfits,
        APPEARANCE_TYPE.AppearanceEffect => DatThingCategory.Effects,
        APPEARANCE_TYPE.AppearanceMissile => DatThingCategory.Missiles,
        _ => DatThingCategory.Items
    };

    private static APPEARANCE_TYPE ToAppearanceCategory(DatThingCategory category) => category switch
    {
        DatThingCategory.Outfits => APPEARANCE_TYPE.AppearanceOutfit,
        DatThingCategory.Effects => APPEARANCE_TYPE.AppearanceEffect,
        DatThingCategory.Missiles => APPEARANCE_TYPE.AppearanceMissile,
        _ => APPEARANCE_TYPE.AppearanceObject
    };

    private static DatThingFrameGroup CreateDefaultLegacyGroup() => new()
    {
        Width = 1,
        Height = 1,
        ExactSize = (byte)TileSize,
        Layers = 1,
        PatternX = 1,
        PatternY = 1,
        PatternZ = 1,
        Frames = 1,
        SpriteIds = [0]
    };
}
