using FluentAssertions;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Tests;

/// <summary>
/// Weryfikuje kompatybilność AppearanceFlagHook z OTClient mehah.
/// Nasza proto: AppearanceFlagHook.direction = field 1
/// OTClient mehah proto: AppearanceFlagHook.south = field 1, .east = field 2
///
/// Kompatybilność wynika ze zbieżności numerów pól:
///   direction=HOOK_TYPE_SOUTH(1) → OTClient czyta south=1 → ustawia HookSouth ✅
///   direction=HOOK_TYPE_EAST(2)  → OTClient czyta south=2=HOOK_TYPE_EAST → ustawia HookEast ✅
/// </summary>
public sealed class HookDirectionCompatibilityTests
{
    [Fact]
    public void Apply_HookSouth_SetsProtoHookDirectionField1_WithValue1()
    {
        var appearance = new Appearance { Id = 1, Flags = new AppearanceFlags() };
        var state = new AppearanceEditState
        {
            HasHook       = true,
            HookDirection = HOOK_TYPE.South,   // HOOK_TYPE_SOUTH = 1
        };

        AppearanceEditorService.Apply(appearance, state);

        appearance.Flags.Hook.Should().NotBeNull();
        // OTClient mehah czyta pole 1 jako "south" — wartość HOOK_TYPE_SOUTH (1) → ustawia HookSouth
        appearance.Flags.Hook!.Direction.Should().Be(HOOK_TYPE.South,
            "pole 1 (direction) mapuje na pole 1 (south) w OTClient; wartość 1 = HOOK_TYPE_SOUTH");
    }

    [Fact]
    public void Apply_HookEast_SetsProtoHookDirectionField1_WithValue2()
    {
        var appearance = new Appearance { Id = 2, Flags = new AppearanceFlags() };
        var state = new AppearanceEditState
        {
            HasHook       = true,
            HookDirection = HOOK_TYPE.East,    // HOOK_TYPE_EAST = 2
        };

        AppearanceEditorService.Apply(appearance, state);

        appearance.Flags.Hook.Should().NotBeNull();
        // OTClient mehah czyta pole 1 jako "south"; wartość 2 = HOOK_TYPE_EAST → ustawia HookEast
        appearance.Flags.Hook!.Direction.Should().Be(HOOK_TYPE.East,
            "pole 1 (direction) mapuje na pole 1 (south) w OTClient; wartość 2 = HOOK_TYPE_EAST");
    }

    [Fact]
    public void Apply_HookDisabled_ClearsHookField()
    {
        var appearance = new Appearance
        {
            Id    = 3,
            Flags = new AppearanceFlags { Hook = new AppearanceFlagHook { Direction = HOOK_TYPE.South } },
        };
        var state = new AppearanceEditState { HasHook = false };

        AppearanceEditorService.Apply(appearance, state);

        appearance.Flags.Hook.Should().BeNull("wyłączona flaga hook musi usunąć pole z proto");
    }

    [Fact]
    public void HookType_South_HasEnumValue1()
    {
        // Dokumentacja: wartości enum muszą być stabilne dla kompatybilności z OTClient
        ((int)HOOK_TYPE.South).Should().Be(1, "OTClient mehah: HOOK_TYPE_SOUTH = 1");
    }

    [Fact]
    public void HookType_East_HasEnumValue2()
    {
        ((int)HOOK_TYPE.East).Should().Be(2, "OTClient mehah: HOOK_TYPE_EAST = 2");
    }

    [Fact]
    public void Apply_HookSouth_EmitsBothDirectionAndHookSouthBoolean()
    {
        var appearance = new Appearance { Id = 10, Flags = new AppearanceFlags() };
        var state = new AppearanceEditState
        {
            HasHook       = true,
            HookDirection = HOOK_TYPE.South,
            HookSouth     = true,
            HookEast      = false,
        };

        AppearanceEditorService.Apply(appearance, state);

        appearance.Flags.Hook!.Direction.Should().Be(HOOK_TYPE.South);
        appearance.Flags.HookSouth.Should().BeTrue("podwójna emisja: bool HookSouth dla OTClient mehah");
        appearance.Flags.HookEast.Should().BeFalse();
    }

    [Fact]
    public void Apply_HookEast_EmitsBothDirectionAndHookEastBoolean()
    {
        var appearance = new Appearance { Id = 11, Flags = new AppearanceFlags() };
        var state = new AppearanceEditState
        {
            HasHook       = true,
            HookDirection = HOOK_TYPE.East,
            HookSouth     = false,
            HookEast      = true,
        };

        AppearanceEditorService.Apply(appearance, state);

        appearance.Flags.Hook!.Direction.Should().Be(HOOK_TYPE.East);
        appearance.Flags.HookEast.Should().BeTrue("podwójna emisja: bool HookEast dla OTClient mehah");
        appearance.Flags.HookSouth.Should().BeFalse();
    }
}
