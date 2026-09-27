using Narzedzia.Core.Appearances;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Services;

public sealed class NpcSaleDataEditState
{
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public uint BuyPrice { get; set; }
    public uint SalePrice { get; set; }
    public uint CurrencyObjectTypeId { get; set; }
    public string CurrencyQuestFlagDisplayName { get; set; } = string.Empty;
}

public sealed class AppearanceEditState
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    public bool HasTransparency { get; set; }
    public uint TransparencyLevel { get; set; }

    public bool IsGround { get; set; }
    public uint GroundSpeed { get; set; }

    public bool Clip { get; set; }
    public bool Bottom { get; set; }
    public bool Top { get; set; }
    public bool Container { get; set; }
    public bool Cumulative { get; set; }
    public bool Usable { get; set; }
    public bool Forceuse { get; set; }
    public bool Multiuse { get; set; }

    public bool IsWriteable { get; set; }
    public uint WriteMaxLength { get; set; }
    public bool IsWriteOnce { get; set; }
    public uint WriteOnceMaxLength { get; set; }

    public bool Liquidpool { get; set; }
    public bool IsUnpassable { get; set; }
    public bool Unmove { get; set; }
    public bool Unsight { get; set; }
    public bool Avoid { get; set; }
    public bool NoMovementAnimation { get; set; }
    public bool Take { get; set; }
    public bool Liquidcontainer { get; set; }
    public bool Hang { get; set; }

    public bool HasHook { get; set; }
    public HOOK_TYPE HookDirection { get; set; } = HOOK_TYPE.South;
    public bool HookSouth { get; set; }
    public bool HookEast { get; set; }
    public bool Rotate { get; set; }

    public bool HasLight { get; set; }
    public uint LightBrightness { get; set; }
    public uint LightColor { get; set; }

    public bool DontHide { get; set; }
    public bool Translucent { get; set; }

    public bool HasShift { get; set; }
    public int ShiftX { get; set; }
    public int ShiftY { get; set; }
    public int ShiftMin { get; set; } = Narzedzia.Core.Appearances.ShiftService.DefaultMin;
    public int ShiftMax { get; set; } = Narzedzia.Core.Appearances.ShiftService.DefaultMax;

    public bool HasHeight { get; set; }
    public uint Elevation { get; set; }

    public bool ReverseAddonsEast { get; set; }
    public bool ReverseAddonsWest { get; set; }
    public bool ReverseAddonsSouth { get; set; }
    public bool ReverseAddonsNorth { get; set; }

    public bool LyingObject { get; set; }
    public bool AnimateAlways { get; set; }

    public bool HasAutomap { get; set; }
    public uint AutomapColor { get; set; }

    public bool HasLenshelp { get; set; }
    public uint LenshelpId { get; set; }

    public bool Fullbank { get; set; }
    public bool IgnoreLook { get; set; }

    public bool HasClothes { get; set; }
    public uint ClothesSlot { get; set; }

    public bool HasDefaultAction { get; set; }
    public PLAYER_ACTION DefaultAction { get; set; } = PLAYER_ACTION.None;

    public bool HasMarket { get; set; }
    public int MarketCategoryIndex { get; set; }
    public uint MarketTradeAsObjectId { get; set; }
    public uint MarketShowAsObjectId { get; set; }
    public uint MarketMinimumLevel { get; set; }
    public List<VOCATION> MarketVocations { get; } = new();

    public bool Wrap { get; set; }
    public bool Unwrap { get; set; }
    public bool Topeffect { get; set; }
    public bool DecoItemKit { get; set; }

    public bool HasChangedToExpire { get; set; }
    public uint ChangedToExpireFormerId { get; set; }

    public bool Corpse { get; set; }
    public bool PlayerCorpse { get; set; }
    public bool HasNpcSaleData { get; set; }
    public List<NpcSaleDataEditState> NpcSaleData { get; } = new();
    public bool ShowOffSocket { get; set; }
    public bool Reportable { get; set; }

    public bool HasUpgradeClassification { get; set; }
    public uint UpgradeClassification { get; set; }

    public bool Wearout { get; set; }
    public bool Clockexpire { get; set; }
    public bool Expire { get; set; }
    public bool Expirestop { get; set; }

    public bool HasCyclopedia { get; set; }
    public uint CyclopediaType { get; set; }

    public bool Ammo { get; set; }

    public bool HasSkillwheelGem { get; set; }
    public uint GemQualityId { get; set; }
    public uint GemVocationId { get; set; }

    public bool DualWielding { get; set; }

    public bool HasImbueable { get; set; }
    public uint ImbueableSlotCount { get; set; }

    public bool HasProficiency { get; set; }
    public uint ProficiencyId { get; set; }

    public List<VOCATION> RestrictToVocations { get; } = new();
    public uint MinimumLevel { get; set; }
    public WEAPON_TYPE WeaponType { get; set; } = WEAPON_TYPE.Noweapon;
}

public static class AppearanceEditorService
{
    public static void Apply(Appearance appearance, AppearanceEditState state)
    {
        ApplyName(appearance, state);

        appearance.Flags ??= new AppearanceFlags();
        var flags = appearance.Flags;

        flags.Transparencylevel = state.HasTransparency
            ? new AppearanceFlagTransparencyLevel { Level = state.TransparencyLevel }
            : null;

        flags.Bank = state.IsGround
            ? new AppearanceFlagBank { Waypoints = state.GroundSpeed }
            : null;

        ApplyOptional(state.Clip, () => flags.Clip = true, flags.ClearClip);
        ApplyOptional(state.Bottom, () => flags.Bottom = true, flags.ClearBottom);
        ApplyOptional(state.Top, () => flags.Top = true, flags.ClearTop);
        ApplyOptional(state.Container, () => flags.Container = true, flags.ClearContainer);
        ApplyOptional(state.Cumulative, () => flags.Cumulative = true, flags.ClearCumulative);
        ApplyOptional(state.Usable, () => flags.Usable = true, flags.ClearUsable);
        ApplyOptional(state.Forceuse, () => flags.Forceuse = true, flags.ClearForceuse);
        ApplyOptional(state.Multiuse, () => flags.Multiuse = true, flags.ClearMultiuse);

        flags.Write = state.IsWriteable
            ? new AppearanceFlagWrite { MaxTextLength = state.WriteMaxLength }
            : null;
        flags.WriteOnce = state.IsWriteOnce
            ? new AppearanceFlagWriteOnce { MaxTextLengthOnce = state.WriteOnceMaxLength }
            : null;

        ApplyOptional(state.Liquidpool, () => flags.Liquidpool = true, flags.ClearLiquidpool);
        ApplyOptional(state.IsUnpassable, () => flags.Unpass = true, flags.ClearUnpass);
        ApplyOptional(state.Unmove, () => flags.Unmove = true, flags.ClearUnmove);
        ApplyOptional(state.Unsight, () => flags.Unsight = true, flags.ClearUnsight);
        ApplyOptional(state.Avoid, () => flags.Avoid = true, flags.ClearAvoid);
        ApplyOptional(state.NoMovementAnimation, () => flags.NoMovementAnimation = true, flags.ClearNoMovementAnimation);
        ApplyOptional(state.Take, () => flags.Take = true, flags.ClearTake);
        ApplyOptional(state.Liquidcontainer, () => flags.Liquidcontainer = true, flags.ClearLiquidcontainer);
        ApplyOptional(state.Hang, () => flags.Hang = true, flags.ClearHang);

        flags.Hook = state.HasHook
            ? new AppearanceFlagHook { Direction = state.HookDirection == 0 ? HOOK_TYPE.South : state.HookDirection }
            : null;
        ApplyOptional(state.HookSouth, () => flags.HookSouth = true, flags.ClearHookSouth);
        ApplyOptional(state.HookEast, () => flags.HookEast = true, flags.ClearHookEast);
        ApplyOptional(state.Rotate, () => flags.Rotate = true, flags.ClearRotate);

        flags.Light = state.HasLight
            ? new AppearanceFlagLight { Brightness = state.LightBrightness, Color = state.LightColor }
            : null;

        ApplyOptional(state.DontHide, () => flags.DontHide = true, flags.ClearDontHide);
        ApplyOptional(state.Translucent, () => flags.Translucent = true, flags.ClearTranslucent);

        if (state.HasShift)
        {
            ShiftService.SetShift(appearance, state.ShiftX, state.ShiftY, state.ShiftMin, state.ShiftMax);
        }
        else
        {
            ShiftService.Clear(appearance);
        }
        flags.Height = state.HasHeight
            ? new AppearanceFlagHeight { Elevation = state.Elevation }
            : null;

        ApplyOptional(state.ReverseAddonsEast, () => flags.ReverseAddonsEast = true, flags.ClearReverseAddonsEast);
        ApplyOptional(state.ReverseAddonsWest, () => flags.ReverseAddonsWest = true, flags.ClearReverseAddonsWest);
        ApplyOptional(state.ReverseAddonsSouth, () => flags.ReverseAddonsSouth = true, flags.ClearReverseAddonsSouth);
        ApplyOptional(state.ReverseAddonsNorth, () => flags.ReverseAddonsNorth = true, flags.ClearReverseAddonsNorth);

        ApplyOptional(state.LyingObject, () => flags.LyingObject = true, flags.ClearLyingObject);
        ApplyOptional(state.AnimateAlways, () => flags.AnimateAlways = true, flags.ClearAnimateAlways);

        flags.Automap = state.HasAutomap
            ? new AppearanceFlagAutomap { Color = state.AutomapColor }
            : null;
        flags.Lenshelp = state.HasLenshelp
            ? new AppearanceFlagLenshelp { Id = state.LenshelpId }
            : null;

        ApplyOptional(state.Fullbank, () => flags.Fullbank = true, flags.ClearFullbank);
        ApplyOptional(state.IgnoreLook, () => flags.IgnoreLook = true, flags.ClearIgnoreLook);

        flags.Clothes = state.HasClothes
            ? new AppearanceFlagClothes { Slot = state.ClothesSlot }
            : null;
        flags.DefaultAction = state.HasDefaultAction
            ? new AppearanceFlagDefaultAction { Action = state.DefaultAction }
            : null;

        ApplyMarket(flags, state);

        ApplyOptional(state.Wrap, () => flags.Wrap = true, flags.ClearWrap);
        ApplyOptional(state.Unwrap, () => flags.Unwrap = true, flags.ClearUnwrap);
        ApplyOptional(state.Topeffect, () => flags.Topeffect = true, flags.ClearTopeffect);
        ApplyOptional(state.DecoItemKit, () => flags.DecoItemKit = true, flags.ClearDecoItemKit);

        flags.Changedtoexpire = state.HasChangedToExpire
            ? new AppearanceFlagChangedToExpire { FormerObjectTypeid = state.ChangedToExpireFormerId }
            : null;

        ApplyOptional(state.Corpse, () => flags.Corpse = true, flags.ClearCorpse);
        ApplyOptional(state.PlayerCorpse, () => flags.PlayerCorpse = true, flags.ClearPlayerCorpse);
        flags.Npcsaledata.Clear();
        if (state.HasNpcSaleData)
        {
            foreach (var entry in state.NpcSaleData)
            {
                flags.Npcsaledata.Add(CreateNpcSaleData(entry));
            }
        }

        ApplyOptional(state.ShowOffSocket, () => flags.ShowOffSocket = true, flags.ClearShowOffSocket);
        ApplyOptional(state.Reportable, () => flags.Reportable = true, flags.ClearReportable);

        flags.Upgradeclassification = state.HasUpgradeClassification
            ? new AppearanceFlagUpgradeClassification { UpgradeClassification = state.UpgradeClassification }
            : null;

        ApplyOptional(state.Wearout, () => flags.Wearout = true, flags.ClearWearout);
        ApplyOptional(state.Clockexpire, () => flags.Clockexpire = true, flags.ClearClockexpire);
        ApplyOptional(state.Expire, () => flags.Expire = true, flags.ClearExpire);
        ApplyOptional(state.Expirestop, () => flags.Expirestop = true, flags.ClearExpirestop);

        flags.Cyclopediaitem = state.HasCyclopedia
            ? new AppearanceFlagCyclopedia { CyclopediaType = state.CyclopediaType }
            : null;
        ApplyOptional(state.Ammo, () => flags.Ammo = true, flags.ClearAmmo);

        flags.SkillwheelGem = state.HasSkillwheelGem
            ? new AppearanceFlagSkillWheelGem { GemQualityId = state.GemQualityId, VocationId = state.GemVocationId }
            : null;
        ApplyOptional(state.DualWielding, () => flags.DualWielding = true, flags.ClearDualWielding);

        flags.Imbueable = state.HasImbueable
            ? new AppearanceFlagImbueable { SlotCount = state.ImbueableSlotCount }
            : null;
        flags.Proficiency = state.HasProficiency
            ? new AppearanceFlagProficiency { ProficiencyId = state.ProficiencyId }
            : null;

        flags.RestrictToVocation.Clear();
        flags.RestrictToVocation.Add(state.RestrictToVocations);

        if (state.MinimumLevel > 0)
        {
            flags.MinimumLevel = state.MinimumLevel;
        }
        else
        {
            flags.ClearMinimumLevel();
        }

        if (state.WeaponType != WEAPON_TYPE.Noweapon)
        {
            flags.WeaponType = state.WeaponType;
        }
        else
        {
            flags.ClearWeaponType();
        }
    }

    private static void ApplyName(Appearance appearance, AppearanceEditState state)
    {
        if (string.IsNullOrWhiteSpace(state.Name))
        {
            appearance.ClearName();
        }
        else
        {
            appearance.Name = state.Name;
        }

        if (string.IsNullOrWhiteSpace(state.Description))
        {
            appearance.ClearDescription();
        }
        else
        {
            appearance.Description = state.Description;
        }
    }

    private static void ApplyMarket(AppearanceFlags flags, AppearanceEditState state)
    {
        if (!state.HasMarket)
        {
            flags.Market = null;
            return;
        }

        var market = new AppearanceFlagMarket
        {
            Category = ToItemCategory(state.MarketCategoryIndex)
        };

        if (state.MarketTradeAsObjectId > 0)
        {
            market.TradeAsObjectId = state.MarketTradeAsObjectId;
        }

        if (state.MarketShowAsObjectId > 0)
        {
            market.ShowAsObjectId = state.MarketShowAsObjectId;
        }

        if (state.MarketMinimumLevel > 0)
        {
            market.MinimumLevel = state.MarketMinimumLevel;
        }

        market.RestrictToVocation.Add(state.MarketVocations);
        flags.Market = market;
    }

    private static ITEM_CATEGORY ToItemCategory(int categoryIndex)
    {
        var raw = categoryIndex + 1;
        return Enum.IsDefined(typeof(ITEM_CATEGORY), raw)
            ? (ITEM_CATEGORY)raw
            : ITEM_CATEGORY.Others;
    }

    private static AppearanceFlagNPC CreateNpcSaleData(NpcSaleDataEditState state)
    {
        var result = new AppearanceFlagNPC();
        if (!string.IsNullOrWhiteSpace(state.Name)) result.Name = state.Name;
        if (!string.IsNullOrWhiteSpace(state.Location)) result.Location = state.Location;
        if (state.BuyPrice > 0) result.BuyPrice = state.BuyPrice;
        if (state.SalePrice > 0) result.SalePrice = state.SalePrice;
        if (state.CurrencyObjectTypeId > 0) result.CurrencyObjectTypeId = state.CurrencyObjectTypeId;
        if (!string.IsNullOrWhiteSpace(state.CurrencyQuestFlagDisplayName))
        {
            result.CurrencyQuestFlagDisplayName = state.CurrencyQuestFlagDisplayName;
        }

        return result;
    }

    private static void ApplyOptional(bool enabled, Action set, Action clear)
    {
        if (enabled)
        {
            set();
        }
        else
        {
            clear();
        }
    }
}
