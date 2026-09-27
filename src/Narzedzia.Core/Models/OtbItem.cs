namespace Narzedzia.Core.Models;

public enum OtbItemType : byte
{
    None = 0x00,
    Ground = 0x01,
    Container = 0x02,
    Fluid = 0x03,
    Splash = 0x04,
    Deprecated = 0x05
}

public class OtbItem
{
    public ushort ServerId { get; set; }
    public ushort ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public OtbItemType ItemType { get; set; }
    public uint Flags { get; set; }
    public ushort Speed { get; set; }
    public byte[] SpriteHash { get; set; } = new byte[16];
    public ushort MinimapColor { get; set; }
    public ushort MaxReadWriteChars { get; set; }
    public ushort MaxReadChars { get; set; }
    public ushort LightLevel { get; set; }
    public ushort LightColor { get; set; }
    public byte StackOrder { get; set; }
    public ushort TradeAs { get; set; }
    public Dictionary<byte, byte[]> RawAttributes { get; set; } = new();
}
