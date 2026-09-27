using FluentAssertions;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Tests;

public sealed class AppearanceContainerServiceTests
{
    [Fact]
    public void ExportContainer_RewritesSpriteIdsAndEmbedsSpriteData()
    {
        var appearance = new Appearance
        {
            Id = 100,
            AppearanceType = APPEARANCE_TYPE.AppearanceObject,
            Flags = new AppearanceFlags
            {
                Unpass = true,
                Shift = new AppearanceFlagShift { X = unchecked((uint)-3), Y = 5 }
            },
            FrameGroup =
            {
                new FrameGroup
                {
                    SpriteInfo = new SpriteInfo { SpriteId = { 500u, 501u } }
                }
            }
        };

        var exported = AppearanceContainerService.BuildExportContainer(
            [appearance],
            spriteId => [(byte)(spriteId - 500), 1, 2, 3]);

        exported.Object[0].SpriteData.Count.Should().Be(2);
        exported.Object[0].FrameGroup[0].SpriteInfo.SpriteId.Should().Equal(0u, 1u);
        exported.Object[0].Flags.Unpass.Should().BeTrue();
        unchecked((int)exported.Object[0].Flags.Shift!.X).Should().Be(-3);
        exported.Object[0].Flags.Shift.Y.Should().Be(5);
    }

    [Fact]
    public void ImportContainer_RewritesEmbeddedSpriteIdsToNewIds()
    {
        var appearance = new Appearance
        {
            Id = 100,
            AppearanceType = APPEARANCE_TYPE.AppearanceObject,
            Flags = new AppearanceFlags
            {
                Container = true,
                Shift = new AppearanceFlagShift { X = 7, Y = unchecked((uint)-9) }
            },
            SpriteData = { Google.Protobuf.ByteString.CopyFrom([1, 2, 3, 4]) },
            FrameGroup =
            {
                new FrameGroup
                {
                    SpriteInfo = new SpriteInfo { SpriteId = { 0u } }
                }
            }
        };

        var imported = AppearanceContainerService.ImportContainer(
            new Appearances { Object = { appearance } },
            bytes =>
            {
                bytes.Should().Equal(1, 2, 3, 4);
                return 9000;
            });

        imported[0].SpriteData.Should().BeEmpty();
        imported[0].FrameGroup[0].SpriteInfo.SpriteId.Should().Equal(9000u);
        imported[0].Flags.Container.Should().BeTrue();
        imported[0].Flags.Shift!.X.Should().Be(7);
        unchecked((int)imported[0].Flags.Shift.Y).Should().Be(-9);
    }
}
