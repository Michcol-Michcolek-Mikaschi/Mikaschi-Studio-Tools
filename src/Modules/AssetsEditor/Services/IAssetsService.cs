using Narzedzia.Core.Assets;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Services;

public interface IAssetsService : IDisposable
{
    bool IsLoaded        { get; }
    bool IsLegacyFormat  { get; }

    Appearances?                AppearancesData { get; }
    IReadOnlyList<CatalogEntry> Catalog         { get; }
    string?                     FolderPath      { get; }

    void LoadFromFolder(string folderPath);

    byte[]? GetSpritePixels(uint spriteId);
    (int Width, int Height) GetSpriteSize(uint spriteId);
    IReadOnlyList<AppearanceSpritePart> GetSpriteSlots(Appearance appearance, int groupIndex);
    RenderedSpriteImage? RenderAppearance(Appearance appearance, AppearanceRenderOptions options);
    IAssetSpriteStore CreateSpriteStore();
    void InvalidateSpriteCache();
}
