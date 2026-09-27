using Narzedzia.Core.Models;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Services;

/// <summary>
/// Faza 7c (MVP) — porównuje proto Appearance z OTB ItemType i raportuje różnice
/// (Name, ItemType, Speed). Zwraca diff bez modyfikacji — bezpieczna podstawa
/// pod docelowy "SyncOtbWithTibia" z Lapis.
///
/// Mapowanie flag → ItemType: viz. assets-editor-wymagania-checklista (req-assets-130..138).
/// </summary>
public sealed class OtbSyncService
{
    public SyncReport Diff(Appearances appearances, OtbFile otb)
    {
        ArgumentNullException.ThrowIfNull(appearances);
        ArgumentNullException.ThrowIfNull(otb);

        var byClient = new Dictionary<uint, Appearance>();
        foreach (var a in appearances.Object)
        {
            byClient[a.Id] = a;
        }

        var diffs = new List<ItemDiff>();
        var missingClients = new List<ushort>();

        foreach (var item in otb.Items)
        {
            if (!byClient.TryGetValue(item.ClientId, out var appearance))
            {
                missingClients.Add(item.ClientId);
                continue;
            }

            CompareName(item, appearance, diffs);
            CompareItemType(item, appearance, diffs);
            CompareSpeed(item, appearance, diffs);
        }

        return new SyncReport(diffs, missingClients);
    }

    private static void CompareName(OtbItem item, Appearance appearance, List<ItemDiff> diffs)
    {
        if (!appearance.HasName) return;
        if (!string.Equals(item.Name, appearance.Name, StringComparison.Ordinal))
        {
            diffs.Add(new ItemDiff(item.ServerId, item.ClientId, "Name", item.Name, appearance.Name));
        }
    }

    private static void CompareItemType(OtbItem item, Appearance appearance, List<ItemDiff> diffs)
    {
        var protoType = DeriveOtbType(appearance);
        if (protoType is null) return;
        if (item.ItemType != protoType.Value)
        {
            diffs.Add(new ItemDiff(item.ServerId, item.ClientId, "ItemType",
                item.ItemType.ToString(), protoType.Value.ToString()));
        }
    }

    private static void CompareSpeed(OtbItem item, Appearance appearance, List<ItemDiff> diffs)
    {
        var bank = appearance.Flags?.Bank;
        if (bank is null || !bank.HasWaypoints) return;
        if (item.Speed != bank.Waypoints)
        {
            diffs.Add(new ItemDiff(item.ServerId, item.ClientId, "Speed",
                item.Speed.ToString(), bank.Waypoints.ToString()));
        }
    }

    /// <summary>Mapuje flagi proto na <see cref="OtbItemType"/> (req-assets-130).</summary>
    private static OtbItemType? DeriveOtbType(Appearance appearance)
    {
        var f = appearance.Flags;
        if (f is null) return null;
        if (f.Bank is not null) return OtbItemType.Ground;
        if (f.Container)        return OtbItemType.Container;
        if (f.Liquidcontainer)  return OtbItemType.Fluid;
        if (f.Liquidpool)       return OtbItemType.Splash;
        return null;
    }

    public sealed record ItemDiff(
        ushort ServerId,
        ushort ClientId,
        string Field,
        string? OtbValue,
        string? AppearanceValue);

    public sealed record SyncReport(
        IReadOnlyList<ItemDiff> Diffs,
        IReadOnlyList<ushort> ClientsMissingInAppearances);
}
