using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Modules.ObjectBuilder.Services;

public sealed class LegacyDatService
{
    public DatFile? Data { get; private set; }
    public string? FilePath { get; private set; }
    public DatParserOptions Options { get; private set; } = DatParserOptions.Default;

    public void Load(string filePath, DatParserOptions? options = null)
    {
        Options = options ?? DatParserOptions.WithFrameGroups;
        Data = DatParser.Parse(filePath, Options);
        FilePath = filePath;
    }

    public void LoadAuto(string filePath, DatParserOptions? preferredOptions = null)
    {
        var result = DatParser.ParseAuto(filePath, preferredOptions);
        Options = result.Options;
        Data = result.File;
        FilePath = filePath;
    }

    public void Save(string filePath)
    {
        if (Data is null)
        {
            throw new InvalidOperationException("Brak wczytanego pliku DAT.");
        }

        if (!string.IsNullOrWhiteSpace(Data.ParseWarning))
        {
            throw new InvalidOperationException("Nie zapisuję częściowo wczytanego Tibia.dat. Najpierw użyj kompletnego pliku zgodnego z nagłówkiem.");
        }

        DatWriter.Write(filePath, Data, Options);
        FilePath = filePath;
    }

    public IReadOnlyList<DatThingType> GetCategory(DatThingCategory category)
    {
        if (Data is null)
        {
            return [];
        }

        return category switch
        {
            DatThingCategory.Items => Data.Items,
            DatThingCategory.Outfits => Data.Outfits,
            DatThingCategory.Effects => Data.Effects,
            DatThingCategory.Missiles => Data.Missiles,
            _ => []
        };
    }

    public DatThingType AddThing(DatThingType thing)
    {
        if (Data is null)
        {
            throw new InvalidOperationException("Wczytaj Tibia.dat przed importem obiektów.");
        }

        ArgumentNullException.ThrowIfNull(thing);
        var target = thing.Category switch
        {
            DatThingCategory.Items => Data.Items,
            DatThingCategory.Outfits => Data.Outfits,
            DatThingCategory.Effects => Data.Effects,
            DatThingCategory.Missiles => Data.Missiles,
            _ => throw new ArgumentOutOfRangeException(nameof(thing.Category))
        };

        var minimum = thing.Category == DatThingCategory.Items ? 100u : 1u;
        var nextId = target.Count == 0 ? minimum : Math.Max(minimum, target.Max(item => item.Id) + 1);
        if (nextId > ushort.MaxValue)
        {
            throw new InvalidOperationException($"Kategoria {thing.Category} osiągnęła limit ID {ushort.MaxValue}.");
        }

        thing.Id = nextId;
        target.Add(thing);
        switch (thing.Category)
        {
            case DatThingCategory.Items: Data.ItemsMaxId = (ushort)nextId; break;
            case DatThingCategory.Outfits: Data.OutfitsMaxId = (ushort)nextId; break;
            case DatThingCategory.Effects: Data.EffectsMaxId = (ushort)nextId; break;
            case DatThingCategory.Missiles: Data.MissilesMaxId = (ushort)nextId; break;
        }

        return thing;
    }

    /// <summary>
    /// Usuwa obiekty z jednej kategorii i odbudowuje ciągłe ID wymagane przez DAT.
    /// Sprite'y pozostają w SPR, ponieważ mogą być używane przez inne obiekty.
    /// </summary>
    public int RemoveThings(IEnumerable<DatThingType> things)
    {
        if (Data is null)
        {
            throw new InvalidOperationException("Wczytaj Tibia.dat przed usuwaniem obiektów.");
        }

        ArgumentNullException.ThrowIfNull(things);
        var selected = things.Distinct().ToArray();
        if (selected.Length == 0) return 0;

        var category = selected[0].Category;
        if (selected.Any(thing => thing.Category != category))
        {
            throw new InvalidOperationException("Jedna operacja usuwania może obejmować tylko jedną kategorię DAT.");
        }

        var target = GetMutableCategory(Data, category);
        var originalIds = target.ToDictionary(thing => thing, thing => thing.Id);
        var selectedSet = selected.ToHashSet();
        var removedIds = target
            .Where(selectedSet.Contains)
            .Select(thing => thing.Id)
            .ToHashSet();
        var removed = target.RemoveAll(selectedSet.Contains);
        if (removed == 0) return 0;

        var firstId = category == DatThingCategory.Items ? 100u : 1u;
        for (var index = 0; index < target.Count; index++)
        {
            target[index].Id = firstId + (uint)index;
        }

        if (category == DatThingCategory.Items)
        {
            var survivingIdMap = target.ToDictionary(thing => originalIds[thing], thing => thing.Id);
            foreach (var thing in EnumerateAllThings(Data).Where(thing => thing.HasMarketInfo))
            {
                thing.MarketTradeAs = RemapItemReference(thing.MarketTradeAs, survivingIdMap, removedIds);
                thing.MarketShowAs = RemapItemReference(thing.MarketShowAs, survivingIdMap, removedIds);
            }
        }

        var maxId = target.Count == 0 ? firstId - 1 : firstId + (uint)target.Count - 1;
        switch (category)
        {
            case DatThingCategory.Items: Data.ItemsMaxId = checked((ushort)maxId); break;
            case DatThingCategory.Outfits: Data.OutfitsMaxId = checked((ushort)maxId); break;
            case DatThingCategory.Effects: Data.EffectsMaxId = checked((ushort)maxId); break;
            case DatThingCategory.Missiles: Data.MissilesMaxId = checked((ushort)maxId); break;
        }

        return removed;
    }

    private static List<DatThingType> GetMutableCategory(DatFile data, DatThingCategory category) => category switch
    {
        DatThingCategory.Items => data.Items,
        DatThingCategory.Outfits => data.Outfits,
        DatThingCategory.Effects => data.Effects,
        DatThingCategory.Missiles => data.Missiles,
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    private static ushort RemapItemReference(
        ushort value,
        IReadOnlyDictionary<uint, uint> survivingIdMap,
        IReadOnlySet<uint> removedIds)
    {
        if (survivingIdMap.TryGetValue(value, out var remapped)) return checked((ushort)remapped);
        return removedIds.Contains(value) ? (ushort)0 : value;
    }

    private static IEnumerable<DatThingType> EnumerateAllThings(DatFile data)
    {
        foreach (var thing in data.Items) yield return thing;
        foreach (var thing in data.Outfits) yield return thing;
        foreach (var thing in data.Effects) yield return thing;
        foreach (var thing in data.Missiles) yield return thing;
    }
}
