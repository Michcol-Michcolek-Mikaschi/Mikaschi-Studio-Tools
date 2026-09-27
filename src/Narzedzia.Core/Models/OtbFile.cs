namespace Narzedzia.Core.Models;

public class OtbFile
{
    public uint MajorVersion { get; set; }
    public uint MinorVersion { get; set; }
    public uint BuildNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<OtbItem> Items { get; set; } = new();
}
