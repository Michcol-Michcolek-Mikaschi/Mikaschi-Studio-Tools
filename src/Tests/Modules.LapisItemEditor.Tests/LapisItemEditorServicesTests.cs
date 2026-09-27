using FluentAssertions;
using Modules.LapisItemEditor.Services;
using Narzedzia.Core.Models;

namespace Modules.LapisItemEditor.Tests;

public sealed class LapisItemEditorServicesTests
{
    [Fact]
    public void OtbDocumentService_ShouldRoundTripItemsAndFlags()
    {
        var path = Path.GetTempFileName();
        try
        {
            var service = new OtbDocumentService();
            var file = new OtbFile
            {
                MajorVersion = 3,
                MinorVersion = 61,
                BuildNumber = 1310,
                Description = "Lapis parity"
            };
            file.Items.Add(new OtbItem
            {
                ServerId = 2160,
                ClientId = 3043,
                Name = "crystal coin",
                ItemType = OtbItemType.None,
                Flags = (uint)(LapisItemFlag.Pickupable | LapisItemFlag.Stackable)
            });

            service.Save(file, path);

            var loaded = service.Load(path);
            loaded.Description.Should().Be("Lapis parity");
            loaded.Items.Should().ContainSingle();
            loaded.Items[0].ServerId.Should().Be(2160);
            loaded.Items[0].ClientId.Should().Be(3043);
            loaded.Items[0].Flags.Should().Be(file.Items[0].Flags);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LapisItemMutationService_ShouldDuplicateItemWithNextServerId()
    {
        var file = new OtbFile();
        var source = new OtbItem
        {
            ServerId = 100,
            ClientId = 200,
            Name = "source",
            Flags = (uint)LapisItemFlag.MultiUse,
            SpriteHash = [1, 2, 3, 4],
            MinimapColor = 129,
            MaxReadWriteChars = 512,
            MaxReadChars = 1024,
            LightLevel = 3,
            LightColor = 156,
            StackOrder = 2,
            TradeAs = 3031,
            RawAttributes = new Dictionary<byte, byte[]> { [0x31] = [1, 2, 3] }
        };
        file.Items.Add(source);
        file.Items.Add(new OtbItem { ServerId = 101, ClientId = 201 });

        var duplicate = new LapisItemMutationService().Duplicate(file, source);

        duplicate.ServerId.Should().Be(102);
        duplicate.ClientId.Should().Be(200);
        duplicate.Name.Should().Be("source copy");
        duplicate.SpriteHash.Should().Equal(1, 2, 3, 4);
        duplicate.SpriteHash.Should().NotBeSameAs(source.SpriteHash);
        duplicate.MinimapColor.Should().Be(129);
        duplicate.MaxReadWriteChars.Should().Be(512);
        duplicate.MaxReadChars.Should().Be(1024);
        duplicate.LightLevel.Should().Be(3);
        duplicate.LightColor.Should().Be(156);
        duplicate.StackOrder.Should().Be(2);
        duplicate.TradeAs.Should().Be(3031);
        duplicate.RawAttributes[0x31].Should().Equal(1, 2, 3);
        duplicate.RawAttributes[0x31].Should().NotBeSameAs(source.RawAttributes[0x31]);
        file.Items.Should().Contain(duplicate);
    }

    [Fact]
    public void LapisItemMutationService_ShouldSetClientIdAndFlag()
    {
        var item = new OtbItem();
        var service = new LapisItemMutationService();

        service.SetClientId(item, 555);
        service.SetFlag(item, LapisItemFlag.Unpassable, true);
        service.SetFlag(item, LapisItemFlag.Stackable, true);
        service.SetFlag(item, LapisItemFlag.Unpassable, false);

        item.ClientId.Should().Be(555);
        item.Flags.Should().Be((uint)LapisItemFlag.Stackable);
    }

    [Fact]
    public void LapisItemSearchService_ShouldMatchIdNameTypeAndFlag()
    {
        var items = new[]
        {
            new OtbItem { ServerId = 100, ClientId = 200, Name = "crystal coin", Flags = (uint)LapisItemFlag.Stackable },
            new OtbItem { ServerId = 101, ClientId = 201, Name = "rope", ItemType = OtbItemType.Ground }
        };
        var service = new LapisItemSearchService();

        service.Filter(items, "100").Should().ContainSingle().Which.Name.Should().Be("crystal coin");
        service.Filter(items, "201").Should().ContainSingle().Which.Name.Should().Be("rope");
        service.Filter(items, "coin").Should().ContainSingle().Which.ServerId.Should().Be(100);
        service.Filter(items, "ground").Should().ContainSingle().Which.ServerId.Should().Be(101);
        service.Filter(items, "stackable").Should().ContainSingle().Which.ServerId.Should().Be(100);
    }

    [Fact]
    public void OtbCompareService_ShouldReportAddedRemovedAndChangedItems()
    {
        var left = new OtbFile
        {
            Items =
            {
                new OtbItem { ServerId = 1, ClientId = 10, Name = "same" },
                new OtbItem { ServerId = 2, ClientId = 20, Name = "old" }
            }
        };
        var right = new OtbFile
        {
            Items =
            {
                new OtbItem { ServerId = 1, ClientId = 11, Name = "same" },
                new OtbItem { ServerId = 3, ClientId = 30, Name = "new" }
            }
        };

        var result = new OtbCompareService().Compare(left, right);

        result.Entries.Should().Contain(e => e.Kind == OtbComparisonKind.ClientIdChanged && e.ServerId == 1);
        result.Entries.Should().Contain(e => e.Kind == OtbComparisonKind.Removed && e.ServerId == 2);
        result.Entries.Should().Contain(e => e.Kind == OtbComparisonKind.Added && e.ServerId == 3);
    }
}
