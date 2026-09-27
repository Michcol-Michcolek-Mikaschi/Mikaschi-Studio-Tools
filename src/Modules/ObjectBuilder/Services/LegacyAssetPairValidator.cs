using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.ObjectBuilder.Services;

/// <summary>
/// Sprawdza zgodność pary Tibia.dat + Tibia.spr bez odrzucania legalnych,
/// zmodyfikowanych zestawów używanych przez stare klienty OTS.
/// </summary>
public sealed record LegacyAssetPairValidation(
    string? Error,
    IReadOnlyList<string> Warnings)
{
    public bool IsCompatible => string.IsNullOrWhiteSpace(Error);
}

public static class LegacyAssetPairValidator
{
    public static LegacyAssetPairValidation Validate(
        DatFile? dat,
        DatParserOptions datOptions,
        SprFile? spr)
    {
        if (dat is null || spr is null)
        {
            return new LegacyAssetPairValidation(null, Array.Empty<string>());
        }

        if (!LegacyObjectBuilderProtocolCatalog.AreSignaturesCompatible(dat.Signature, spr.Signature))
        {
            return new LegacyAssetPairValidation(
                $"Sygnatury Tibia.dat (0x{dat.Signature:X8}) i Tibia.spr " +
                $"(0x{spr.Signature:X8}) należą do różnych protokołów Object Buildera.",
                Array.Empty<string>());
        }

        var warnings = new List<string>();
        if (spr.ExtendedSprites != datOptions.ExtendedSprites)
        {
            warnings.Add(
                $"DAT używa {(datOptions.ExtendedSprites ? 32 : 16)}-bitowych ID sprite'ów, " +
                $"a tabela SPR jest {(spr.ExtendedSprites ? 32 : 16)}-bitowa. " +
                "Zestaw wczytano zgodnie z rzeczywistą strukturą obu plików.");
        }

        var maximumReferencedSpriteId = EnumerateThings(dat)
            .SelectMany(thing => thing.FrameGroups)
            .SelectMany(group => group.SpriteIds)
            .DefaultIfEmpty(0u)
            .Max();
        if (maximumReferencedSpriteId > spr.Sprites.Count)
        {
            warnings.Add(
                $"DAT odwołuje się do sprite ID {maximumReferencedSpriteId}, ale SPR zawiera " +
                $"{spr.Sprites.Count} wpisów. Brakujące sprite'y będą wyświetlane jako puste.");
        }

        return new LegacyAssetPairValidation(null, warnings);
    }

    private static IEnumerable<DatThingType> EnumerateThings(DatFile dat) =>
        dat.Items.Concat(dat.Outfits).Concat(dat.Effects).Concat(dat.Missiles);
}
