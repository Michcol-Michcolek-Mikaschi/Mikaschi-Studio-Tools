using Narzedzia.Core.Parsers;

namespace Modules.ObjectBuilder.Services;

/// <summary>
/// Odwzorowuje progi wersji używane przez oryginalny Object Builder.
/// Format parsera obejmuje kilka wersji klienta, dlatego dla progów wewnątrz
/// jednego formatu używana jest także sygnatura Tibia.dat.
/// </summary>
public sealed record LegacyObjectBuilderCapabilities(
    int ClientVersion,
    string ProtocolName,
    bool GroundBorder,
    bool WallHooks,
    bool DontHide,
    bool Translucent,
    bool Charges,
    bool FloorChange,
    bool Equip,
    bool Market,
    bool NoMoveAnimation,
    bool DefaultAction,
    bool Useable,
    bool Wrapping,
    bool TopEffect,
    bool CanPersistWrapping,
    bool CanPersistTopEffect,
    bool Bones,
    bool PatternZ,
    int OffsetMinimum,
    int OffsetMaximum)
{
    public static LegacyObjectBuilderCapabilities For(DatMetadataFormat format, uint datSignature)
    {
        var protocol = LegacyObjectBuilderProtocolCatalog.Resolve(format, datSignature);
        var version = protocol.ClientVersion;
        var extendedOffset = version >= 755;
        var wrappingVisible = version is >= 710 and <= 792 or >= 1092;

        // Object Builder 0.4.3 pokazuje wrapping także w 7.55–7.72 i Top Effect
        // w 7.55–7.92, ale odpowiednie MetadataWriter3/4 nie potrafią tych flag
        // zapisać. Osobne pola CanPersist* pozwalają zachować układ 1:1 bez
        // tworzenia zmian, które zniknęłyby po ponownym otwarciu Tibia.dat.
        var wrappingPersisted = version <= 750 || version is >= 780 and <= 792 || version >= 1092;
        var topEffectPersisted = version <= 750 || version >= 1092;

        return new LegacyObjectBuilderCapabilities(
            ClientVersion: version,
            ProtocolName: protocol.DisplayName,
            GroundBorder: version >= 755,
            WallHooks: version >= 755,
            DontHide: version >= 780,
            Translucent: version >= 860,
            Charges: version is >= 780 and <= 854,
            FloorChange: version is >= 710 and <= 854,
            Equip: version >= 900,
            Market: version >= 940,
            NoMoveAnimation: version >= 1010,
            DefaultAction: version >= 1021,
            Useable: version >= 1021,
            Wrapping: wrappingVisible,
            TopEffect: wrappingVisible,
            CanPersistWrapping: wrappingPersisted,
            CanPersistTopEffect: topEffectPersisted,
            Bones: version >= 780,
            PatternZ: extendedOffset,
            OffsetMinimum: extendedOffset ? -256 : 8,
            OffsetMaximum: extendedOffset ? 256 : 8);
    }
}
