using Narzedzia.Core.Models;

namespace Modules.LapisItemEditor.Services;

public sealed class LapisItemMutationService
{
    public OtbItem Duplicate(OtbFile file, OtbItem source)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(source);

        var nextServerId = file.Items.Count == 0
            ? (ushort)100
            : checked((ushort)(file.Items.Max(item => item.ServerId) + 1));

        var clone = Clone(source);
        clone.ServerId = nextServerId;
        clone.Name = string.IsNullOrWhiteSpace(source.Name)
            ? $"Item {nextServerId}"
            : $"{source.Name} copy";

        file.Items.Add(clone);
        return clone;
    }

    public void SetClientId(OtbItem item, ushort clientId)
    {
        ArgumentNullException.ThrowIfNull(item);
        item.ClientId = clientId;
    }

    public void SetFlag(OtbItem item, LapisItemFlag flag, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (enabled)
        {
            item.Flags |= (uint)flag;
            return;
        }

        item.Flags &= ~(uint)flag;
    }

    public void ReloadAttributesFrom(OtbItem target, OtbItem source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        target.ItemType = source.ItemType;
        target.Flags = source.Flags;
        target.Speed = source.Speed;
        target.Name = source.Name;
        target.SpriteHash = source.SpriteHash.ToArray();
        target.MinimapColor = source.MinimapColor;
        target.MaxReadWriteChars = source.MaxReadWriteChars;
        target.MaxReadChars = source.MaxReadChars;
        target.LightLevel = source.LightLevel;
        target.LightColor = source.LightColor;
        target.StackOrder = source.StackOrder;
        target.TradeAs = source.TradeAs;
        target.RawAttributes = CloneRawAttributes(source.RawAttributes);
    }

    private static OtbItem Clone(OtbItem source) => new()
    {
        ServerId = source.ServerId,
        ClientId = source.ClientId,
        Name = source.Name,
        ItemType = source.ItemType,
        Flags = source.Flags,
        Speed = source.Speed,
        SpriteHash = source.SpriteHash.ToArray(),
        MinimapColor = source.MinimapColor,
        MaxReadWriteChars = source.MaxReadWriteChars,
        MaxReadChars = source.MaxReadChars,
        LightLevel = source.LightLevel,
        LightColor = source.LightColor,
        StackOrder = source.StackOrder,
        TradeAs = source.TradeAs,
        RawAttributes = CloneRawAttributes(source.RawAttributes)
    };

    private static Dictionary<byte, byte[]> CloneRawAttributes(Dictionary<byte, byte[]> attributes) =>
        attributes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray());
}
