namespace Modules.AssetsEditor.Services;

/// <summary>
/// Reprezentacja kierunku haka (wieszaka) w UI.
/// Mapuje się na proto:
///   None  → brak `Flags.Hook` i brak `HookSouth/East`
///   South → `Flags.Hook.Direction = HOOK_TYPE_SOUTH` (1) ORAZ `HookSouth = true`
///   East  → `Flags.Hook.Direction = HOOK_TYPE_EAST` (2)  ORAZ `HookEast = true`
///
/// Podwójna emisja zapewnia kompatybilność zarówno z oryginalnym Tibia/Assets-Editor
/// (czytającym `direction`) jak i OTClient mehah (czytającym `south`/`east`).
/// </summary>
public enum HookMode
{
    None  = 0,
    South = 1,
    East  = 2,
}
