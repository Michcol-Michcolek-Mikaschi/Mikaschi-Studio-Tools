using FluentAssertions;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Assets;

namespace Modules.AssetsEditor.Tests;

public sealed class AssetSpriteIndexTests
{
    [Fact]
    public void GetPage_ReturnsSpriteIdsAcrossCatalogRanges()
    {
        var index = AssetSpriteIndex.FromCatalog(
        [
            new CatalogEntry { Type = "sprite", File = "a.bmp.lzma", SpriteType = 0, FirstSpriteid = 10, LastSpriteid = 12 },
            new CatalogEntry { Type = "sprite", File = "b.bmp.lzma", SpriteType = 0, FirstSpriteid = 20, LastSpriteid = 22 }
        ]);

        var page = index.GetPage(1, 3);

        page.Select(item => item.SpriteId).Should().Equal(20u, 21u, 22u);
    }

    [Fact]
    public void FindPageForSprite_ReturnsPageContainingSpriteId()
    {
        var index = AssetSpriteIndex.FromCatalog(
        [
            new CatalogEntry { Type = "sprite", File = "a.bmp.lzma", SpriteType = 0, FirstSpriteid = 100, LastSpriteid = 105 },
            new CatalogEntry { Type = "sprite", File = "b.bmp.lzma", SpriteType = 0, FirstSpriteid = 200, LastSpriteid = 205 }
        ]);

        index.FindPageForSprite(202, pageSize: 5).Should().Be(1);
        index.GetPage(1, 5).Select(item => item.SpriteId).Should().Contain(202u);
    }
}
