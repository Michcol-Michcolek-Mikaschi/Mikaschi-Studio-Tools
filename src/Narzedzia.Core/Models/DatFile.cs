namespace Narzedzia.Core.Models;

/// <summary>
/// Dane wczytane z pliku .dat (Tibia Objects Data).
/// Zawiera sygnaturę, maks. ID poszczególnych kategorii i kolekcje obiektów.
/// </summary>
public sealed class DatFile
{
    /// <summary>Sygnatura (uint32) — identyfikuje wersję klienta.</summary>
    public uint Signature { get; set; }

    /// <summary>Maks. ID item (items zaczynają się od ID 100).</summary>
    public ushort ItemsMaxId { get; set; }

    /// <summary>Maks. ID outfitu (zaczynają się od ID 1).</summary>
    public ushort OutfitsMaxId { get; set; }

    /// <summary>Maks. ID efektu (zaczyna się od ID 1).</summary>
    public ushort EffectsMaxId { get; set; }

    /// <summary>Maks. ID pocisku (zaczyna się od ID 1).</summary>
    public ushort MissilesMaxId { get; set; }

    /// <summary>Lista wszystkich itemów (ID 100..ItemsMaxId).</summary>
    public List<DatThingType> Items    { get; } = new();

    /// <summary>Lista wszystkich outfitów (ID 1..OutfitsMaxId).</summary>
    public List<DatThingType> Outfits  { get; } = new();

    /// <summary>Lista wszystkich efektów (ID 1..EffectsMaxId).</summary>
    public List<DatThingType> Effects  { get; } = new();

    /// <summary>Lista wszystkich pocisków (ID 1..MissilesMaxId).</summary>
    public List<DatThingType> Missiles { get; } = new();

    /// <summary>Ostrzeżenie ustawiane, gdy parser musiał zakończyć odczyt częściowo.</summary>
    public string? ParseWarning { get; set; }

    /// <summary>Rzeczywista liczba itemów (Items.Count).</summary>
    public int ItemCount    => Items.Count;

    /// <summary>Rzeczywista liczba outfitów (Outfits.Count).</summary>
    public int OutfitCount  => Outfits.Count;

    /// <summary>Rzeczywista liczba efektów (Effects.Count).</summary>
    public int EffectCount  => Effects.Count;

    /// <summary>Rzeczywista liczba pocisków (Missiles.Count).</summary>
    public int MissileCount => Missiles.Count;
}
