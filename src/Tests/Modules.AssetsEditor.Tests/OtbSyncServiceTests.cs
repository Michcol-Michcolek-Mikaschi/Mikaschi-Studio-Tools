using FluentAssertions;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Models;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Tests;

public sealed class OtbSyncServiceTests
{
    [Fact]
    public void Diff_DetectsNameMismatch()
    {
        var appearances = new Appearances();
        appearances.Object.Add(new Appearance { Id = 100, Name = "sword" });

        var otb = new OtbFile { Items = { new OtbItem { ServerId = 200, ClientId = 100, Name = "old-sword" } } };

        var report = new OtbSyncService().Diff(appearances, otb);

        report.Diffs.Should().ContainSingle(d =>
            d.Field == "Name" && d.OtbValue == "old-sword" && d.AppearanceValue == "sword");
    }

    [Fact]
    public void Diff_DetectsItemTypeMismatchFromContainerFlag()
    {
        var appearances = new Appearances();
        appearances.Object.Add(new Appearance
        {
            Id = 50,
            Flags = new AppearanceFlags { Container = true },
        });

        var otb = new OtbFile
        {
            Items = { new OtbItem { ServerId = 50, ClientId = 50, ItemType = OtbItemType.None } },
        };

        var report = new OtbSyncService().Diff(appearances, otb);

        report.Diffs.Should().ContainSingle(d => d.Field == "ItemType")
              .Which.AppearanceValue.Should().Be(nameof(OtbItemType.Container));
    }

    [Fact]
    public void Diff_DetectsSpeedMismatchFromBankWaypoints()
    {
        var appearances = new Appearances();
        appearances.Object.Add(new Appearance
        {
            Id = 7,
            Flags = new AppearanceFlags { Bank = new AppearanceFlagBank { Waypoints = 250 } },
        });
        var otb = new OtbFile { Items = { new OtbItem { ServerId = 7, ClientId = 7, Speed = 100 } } };

        var report = new OtbSyncService().Diff(appearances, otb);

        report.Diffs.Should().ContainSingle(d => d.Field == "Speed")
              .Which.AppearanceValue.Should().Be("250");
    }

    [Fact]
    public void Diff_ReportsMissingClientIds()
    {
        var appearances = new Appearances();
        var otb = new OtbFile { Items = { new OtbItem { ServerId = 1, ClientId = 999 } } };

        var report = new OtbSyncService().Diff(appearances, otb);

        report.ClientsMissingInAppearances.Should().Equal(new ushort[] { 999 });
    }
}
