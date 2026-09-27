namespace Narzedzia.Core.Models;

/// <summary>
/// Typ obiektu z pliku .dat — odpowiada jednej pozycji (item/outfit/effect/missile).
/// Zawiera flagi właściwości i listę grup klatek (frame groups).
/// </summary>
public sealed class DatThingType
{
    public uint Id { get; set; }
    public DatThingCategory Category { get; set; }

    // --- Frame groups (zwykle 1; outfity mają 2 dla >= 10.57+) ---
    public List<DatThingFrameGroup> FrameGroups { get; private set; } = new();

    public DatThingFrameGroup? FirstGroup => FrameGroups.Count > 0 ? FrameGroups[0] : null;

    // ================================================================
    // Flagi z MetadataFlags6 (bajtowe znaczniki w pliku .dat)
    // Odczytywane jako sekwencja bajtów, dopóki nie natrafimy na 0xFF.
    // ================================================================

    public bool IsGround         { get; set; }
    public ushort GroundSpeed    { get; set; }   // tylko gdy IsGround

    public bool IsGroundBorder   { get; set; }
    public bool IsOnBottom       { get; set; }
    public bool IsOnTop          { get; set; }
    public bool IsContainer      { get; set; }
    public bool IsStackable      { get; set; }
    public bool ForceUse         { get; set; }
    public bool IsMultiUse       { get; set; }
    public bool HasCharges       { get; set; }
    public bool IsWritable       { get; set; }
    public ushort MaxReadWriteChars { get; set; }
    public bool IsWritableOnce   { get; set; }
    public ushort MaxReadChars   { get; set; }
    public bool IsFluidContainer { get; set; }
    public bool IsFluid          { get; set; }

    public bool IsUnpassable     { get; set; }   // blokuje ruch
    public bool IsUnmoveable     { get; set; }   // nie można przesunąć
    public bool BlockMissile     { get; set; }   // blokuje pociski
    public bool BlockPathfinder  { get; set; }   // blokuje ścieżkę NPC
    public bool NoMoveAnimation  { get; set; }
    public bool IsPickupable     { get; set; }   // można podnieść

    public bool IsHangable       { get; set; }
    public bool IsHorizontal     { get; set; }
    public bool IsVertical       { get; set; }
    public bool IsRotatable      { get; set; }

    public bool HasLight         { get; set; }
    public ushort LightLevel     { get; set; }
    public ushort LightColor     { get; set; }

    public bool DontHide         { get; set; }
    public bool IsTranslucent    { get; set; }
    public bool FloorChange      { get; set; }

    public bool HasOffset        { get; set; }
    public short OffsetX         { get; set; }
    public short OffsetY         { get; set; }

    public bool HasElevation     { get; set; }
    public ushort Elevation      { get; set; }

    public bool IsLyingObject    { get; set; }
    public bool AnimateAlways    { get; set; }

    public bool HasMiniMapColor  { get; set; }
    public ushort MiniMapColor   { get; set; }

    public bool HasLensHelp      { get; set; }
    public ushort LensHelp       { get; set; }

    public bool IsFullGround     { get; set; }
    public bool IgnoreLook       { get; set; }   // nie widać pod kursorem myszy

    public bool HasCloth         { get; set; }
    public ushort ClothSlot      { get; set; }

    public bool HasMarketInfo    { get; set; }
    public ushort MarketCategory { get; set; }
    public ushort MarketTradeAs  { get; set; }
    public ushort MarketShowAs   { get; set; }
    public string MarketName     { get; set; } = "";
    public ushort MarketRestrictProfession { get; set; }
    public ushort MarketRestrictLevel      { get; set; }

    public bool IsWrappable      { get; set; }
    public bool IsUnwrappable    { get; set; }
    public bool IsTopEffect      { get; set; }
    public bool IsUsable         { get; set; }
    public bool HasDefaultAction { get; set; }
    public ushort DefaultAction  { get; set; }
    public bool HasBones         { get; set; }
    public short[] BoneOffsets   { get; set; } = new short[8];

    /// <summary>
    /// Tworzy pełną, niezależną kopię obiektu. Sprite ID są zachowane, natomiast
    /// tablice i grupy klatek nie są współdzielone z obiektem źródłowym.
    /// </summary>
    public DatThingType DeepClone()
    {
        var clone = (DatThingType)MemberwiseClone();
        clone.BoneOffsets = [.. BoneOffsets];
        clone.FrameGroups = FrameGroups.Select(group => group.DeepClone()).ToList();
        return clone;
    }

    /// <summary>Zwraca czytelną listę aktywnych flag (do panelu "Other").</summary>
    public string ActiveFlagsText()
    {
        var sb = new System.Text.StringBuilder();
        void Add(string name) { if (sb.Length > 0) sb.Append(", "); sb.Append(name); }

        if (IsGround)        Add($"Ground({GroundSpeed})");
        if (IsGroundBorder)  Add("GroundBorder");
        if (IsOnBottom)      Add("OnBottom");
        if (IsOnTop)         Add("OnTop");
        if (IsContainer)     Add("Container");
        if (IsStackable)     Add("Stackable");
        if (ForceUse)        Add("ForceUse");
        if (IsMultiUse)      Add("MultiUse");
        if (HasCharges)      Add("Charges");
        if (IsWritable)      Add($"Writable({MaxReadWriteChars})");
        if (IsWritableOnce)  Add($"WritableOnce({MaxReadChars})");
        if (IsFluidContainer) Add("FluidContainer");
        if (IsFluid)         Add("Fluid");
        if (IsUnpassable)    Add("Unpassable");
        if (IsUnmoveable)    Add("Unmoveable");
        if (BlockMissile)    Add("BlockMissile");
        if (BlockPathfinder) Add("BlockPathfinder");
        if (NoMoveAnimation) Add("NoMoveAnim");
        if (IsPickupable)    Add("Pickupable");
        if (IsHangable)      Add("Hangable");
        if (IsHorizontal)    Add("Horizontal");
        if (IsVertical)      Add("Vertical");
        if (IsRotatable)     Add("Rotatable");
        if (HasLight)        Add($"Light({LightLevel},{LightColor})");
        if (DontHide)        Add("DontHide");
        if (IsTranslucent)   Add("Translucent");
        if (FloorChange)     Add("FloorChange");
        if (HasOffset)       Add($"Offset({OffsetX},{OffsetY})");
        if (HasElevation)    Add($"Elevation({Elevation})");
        if (IsLyingObject)   Add("LyingObject");
        if (AnimateAlways)   Add("AnimateAlways");
        if (HasMiniMapColor) Add($"MiniMap({MiniMapColor:X4})");
        if (IsFullGround)    Add("FullGround");
        if (IgnoreLook)      Add("IgnoreLook");
        if (HasCloth)        Add($"Cloth({ClothSlot})");
        if (HasMarketInfo)   Add($"Market({MarketName})");
        if (IsWrappable)     Add("Wrappable");
        if (IsUnwrappable)   Add("Unwrappable");
        if (IsTopEffect)     Add("TopEffect");
        if (IsUsable)        Add("Usable");
        if (HasDefaultAction) Add($"DefaultAction({DefaultAction})");
        if (HasBones)        Add("Bones");

        return sb.Length > 0 ? sb.ToString() : "(brak flag)";
    }
}

public enum DatThingCategory
{
    Items    = 0,
    Outfits  = 1,
    Effects  = 2,
    Missiles = 3
}
