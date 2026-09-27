namespace Narzedzia.Core.Models;

public class AppearancesFile
{
    public List<AppearanceEntry> Objects { get; set; } = new();
    public List<AppearanceEntry> Outfits { get; set; } = new();
    public List<AppearanceEntry> Effects { get; set; } = new();
    public List<AppearanceEntry> Missiles { get; set; } = new();
}

public class AppearanceEntry
{
    public uint Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
