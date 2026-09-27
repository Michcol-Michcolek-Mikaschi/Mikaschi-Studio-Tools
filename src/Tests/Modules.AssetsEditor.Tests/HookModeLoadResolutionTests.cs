using FluentAssertions;
using Modules.AssetsEditor.Services;
using Modules.AssetsEditor.ViewModels;
using Narzedzia.Core.Tibia12;
using NSubstitute;

namespace Modules.AssetsEditor.Tests;

/// <summary>
/// Weryfikuje że ładowanie Appearance z proto poprawnie wylicza HookMode niezależnie
/// od źródła (oryginał WPF używa `direction`, OTClient mehah używa `south`/`east`).
/// </summary>
public sealed class HookModeLoadResolutionTests
{
    [Fact]
    public void Load_WpfStyle_DirectionSouth_ResolvesToHookModeSouth()
    {
        var vm = NewVm();
        var appearance = new Appearance
        {
            Id = 1,
            Flags = new AppearanceFlags
            {
                Hook = new AppearanceFlagHook { Direction = HOOK_TYPE.South },
            },
        };

        vm.Selected = new AppearanceListItem(appearance);

        vm.FlagHook.Should().BeTrue();
        vm.HookMode.Should().Be(HookMode.South);
        vm.FlagHookSouth.Should().BeTrue("podwójna emisja: bool jest pochodną HookMode");
        vm.FlagHookEast.Should().BeFalse();
    }

    [Fact]
    public void Load_WpfStyle_DirectionEast_ResolvesToHookModeEast()
    {
        var vm = NewVm();
        var appearance = new Appearance
        {
            Id = 2,
            Flags = new AppearanceFlags
            {
                Hook = new AppearanceFlagHook { Direction = HOOK_TYPE.East },
            },
        };

        vm.Selected = new AppearanceListItem(appearance);

        vm.FlagHook.Should().BeTrue();
        vm.HookMode.Should().Be(HookMode.East);
        vm.FlagHookEast.Should().BeTrue();
        vm.FlagHookSouth.Should().BeFalse();
    }

    [Fact]
    public void Load_OtclientStyle_HookEastOnly_ResolvesToHookModeEast()
    {
        var vm = NewVm();
        var appearance = new Appearance
        {
            Id = 3,
            Flags = new AppearanceFlags
            {
                HookEast = true,
                // Hook (message) celowo null — symuluje plik z OTClient mehah
            },
        };

        vm.Selected = new AppearanceListItem(appearance);

        vm.FlagHook.Should().BeTrue();
        vm.HookMode.Should().Be(HookMode.East);
    }

    [Fact]
    public void Load_OtclientStyle_HookSouthOnly_ResolvesToHookModeSouth()
    {
        var vm = NewVm();
        var appearance = new Appearance
        {
            Id = 4,
            Flags = new AppearanceFlags
            {
                HookSouth = true,
            },
        };

        vm.Selected = new AppearanceListItem(appearance);

        vm.FlagHook.Should().BeTrue();
        vm.HookMode.Should().Be(HookMode.South);
    }

    [Fact]
    public void Load_NoHookData_LeavesHookModeNone()
    {
        var vm = NewVm();
        var appearance = new Appearance { Id = 5, Flags = new AppearanceFlags() };

        vm.Selected = new AppearanceListItem(appearance);

        vm.FlagHook.Should().BeFalse();
        vm.HookMode.Should().Be(HookMode.None);
    }

    [Fact]
    public void Toggle_FlagHookOn_DefaultsToHookModeSouth()
    {
        var vm = NewVm();

        vm.FlagHook = true;

        vm.HookMode.Should().Be(HookMode.South);
        vm.FlagHookSouth.Should().BeTrue();
    }

    [Fact]
    public void Toggle_FlagHookOff_ResetsHookModeToNone()
    {
        var vm = NewVm();
        vm.FlagHook = true;
        vm.HookMode = HookMode.East;

        vm.FlagHook = false;

        vm.HookMode.Should().Be(HookMode.None);
        vm.FlagHookSouth.Should().BeFalse();
        vm.FlagHookEast.Should().BeFalse();
    }

    private static AssetsEditorViewModel NewVm() =>
        new(Substitute.For<IAssetsService>());
}
