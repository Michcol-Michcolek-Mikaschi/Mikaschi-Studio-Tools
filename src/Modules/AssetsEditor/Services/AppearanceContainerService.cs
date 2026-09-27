using Narzedzia.Core.Interchange;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Services;

/// <summary>
/// Fasada modułu Assets Editor nad wspólnym kodekiem AEC używanym również
/// przez Old Assets Editor.
/// </summary>
public static class AppearanceContainerService
{
    public static Appearances BuildExportContainer(
        IEnumerable<Appearance> appearances,
        Func<uint, byte[]?> readSpriteBytes) =>
        AecContainerCodec.BuildExportContainer(appearances, readSpriteBytes);

    public static Appearances BuildExportContainer(
        IEnumerable<Appearance> appearances,
        Func<uint, AecSpritePayload?> readSprite) =>
        AecContainerCodec.BuildExportContainer(appearances, readSprite);

    public static IReadOnlyList<Appearance> ImportContainer(
        Appearances container,
        Func<byte[], uint> addSpriteAndReturnNewId) =>
        AecContainerCodec.ImportContainer(container, addSpriteAndReturnNewId);

    public static IReadOnlyList<Appearance> ImportContainer(
        Appearances container,
        Func<AecSpritePayload, uint> addSpriteAndReturnNewId) =>
        AecContainerCodec.ImportContainer(container, addSpriteAndReturnNewId);
}
