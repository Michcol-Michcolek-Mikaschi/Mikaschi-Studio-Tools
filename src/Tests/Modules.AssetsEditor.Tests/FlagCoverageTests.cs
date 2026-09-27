using FluentAssertions;
using Modules.AssetsEditor.ViewModels;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Tests;

/// <summary>
/// Test reflexyjny: weryfikuje 100% pokrycie pól z `AppearanceFlags` przez właściwości
/// `Flag*` na `AssetsEditorViewModel`. Jakiekolwiek nowe pole w proto będzie wymagać
/// dodania mapowania w `ProtoToVmFlagMap` ORAZ odpowiadającej właściwości w VM.
/// </summary>
public sealed class FlagCoverageTests
{
    private static readonly Dictionary<string, string[]> ProtoToVmFlagMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // proto field name → spodziewane prefiksy VM property (po "Flag")
        ["bank"]                    = ["Ground"],
        ["clip"]                    = ["Clip"],
        ["bottom"]                  = ["Bottom"],
        ["top"]                     = ["Top"],
        ["container"]               = ["Container"],
        ["cumulative"]              = ["Cumulative"],
        ["usable"]                  = ["Usable"],
        ["forceuse"]                = ["Forceuse"],
        ["multiuse"]                = ["Multiuse"],
        ["write"]                   = ["Write"],
        ["write_once"]              = ["WriteOnce"],
        ["liquidpool"]              = ["Liquidpool"],
        ["unpass"]                  = ["Unpass"],
        ["unmove"]                  = ["Unmove"],
        ["unsight"]                 = ["Unsight"],
        ["avoid"]                   = ["Avoid"],
        ["no_movement_animation"]   = ["NoMovementAnimation"],
        ["take"]                    = ["Take"],
        ["liquidcontainer"]         = ["Liquidcontainer"],
        ["hang"]                    = ["Hang"],
        ["hook"]                    = ["Hook"],
        ["rotate"]                  = ["Rotate"],
        ["light"]                   = ["Light"],
        ["dont_hide"]               = ["DontHide"],
        ["translucent"]             = ["Translucent"],
        ["shift"]                   = ["Shift"],
        ["height"]                  = ["Height"],
        ["lying_object"]            = ["LyingObject"],
        ["animate_always"]          = ["AnimateAlways"],
        ["automap"]                 = ["Automap"],
        ["lenshelp"]                = ["Lenshelp"],
        ["fullbank"]                = ["Fullbank"],
        ["ignore_look"]             = ["IgnoreLook"],
        ["clothes"]                 = ["Clothes"],
        ["default_action"]          = ["DefaultAction"],
        ["market"]                  = ["Market"],
        ["wrap"]                    = ["Wrap"],
        ["unwrap"]                  = ["Unwrap"],
        ["topeffect"]               = ["Topeffect"],
        ["npcsaledata"]             = ["NpcSaleData"],
        ["changedtoexpire"]         = ["Changedtoexpire"],
        ["corpse"]                  = ["Corpse"],
        ["player_corpse"]           = ["PlayerCorpse"],
        ["cyclopediaitem"]          = ["Cyclopedia"],
        ["ammo"]                    = ["Ammo"],
        ["show_off_socket"]         = ["ShowOffSocket"],
        ["reportable"]              = ["Reportable"],
        ["upgradeclassification"]   = ["Upgradeclassification"],
        ["reverse_addons_east"]     = ["ReverseAddonsEast"],
        ["reverse_addons_west"]     = ["ReverseAddonsWest"],
        ["reverse_addons_south"]    = ["ReverseAddonsSouth"],
        ["reverse_addons_north"]    = ["ReverseAddonsNorth"],
        ["wearout"]                 = ["Wearout"],
        ["clockexpire"]             = ["Clockexpire"],
        ["expire"]                  = ["Expire"],
        ["expirestop"]              = ["Expirestop"],
        ["deco_item_kit"]           = ["DecoItemKit"],
        ["skillwheel_gem"]          = ["SkillwheelGem"],
        ["dual_wielding"]           = ["DualWielding"],
        ["imbueable"]               = ["Imbueable"],
        ["proficiency"]             = ["Proficiency"],
        ["restrict_to_vocation"]    = ["RestrictVoc"],
        ["minimum_level"]           = ["MinimumLevel"],
        ["weapon_type"]             = ["WeaponType"],
        ["hook_south"]              = ["HookSouth"],
        ["hook_east"]               = ["HookEast"],
        ["transparencylevel"]       = ["Transparency"],
    };

    [Fact]
    public void EveryAppearanceFlagsProtoField_HasMatchingViewModelProperty()
    {
        var protoFields = AppearanceFlags.Descriptor.Fields
            .InDeclarationOrder()
            .Select(f => f.Name)
            .ToList();

        var vmFlagProps = typeof(AssetsEditorViewModel)
            .GetProperties()
            .Where(p => p.Name.StartsWith("Flag", StringComparison.Ordinal))
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var missing = new List<string>();

        foreach (var protoField in protoFields)
        {
            if (!ProtoToVmFlagMap.TryGetValue(protoField, out var expectedAliases))
            {
                missing.Add($"proto field '{protoField}' nie ma mapowania w ProtoToVmFlagMap");
                continue;
            }

            var anyMatch = expectedAliases.Any(alias =>
                vmFlagProps.Any(prop => prop.Equals("Flag" + alias, StringComparison.OrdinalIgnoreCase) ||
                                        prop.StartsWith("Flag" + alias, StringComparison.OrdinalIgnoreCase)));

            if (!anyMatch)
            {
                missing.Add($"proto field '{protoField}' (alias {string.Join("/", expectedAliases)}) " +
                            $"nie ma odpowiadającej właściwości Flag* w AssetsEditorViewModel");
            }
        }

        missing.Should().BeEmpty(
            "każde pole AppearanceFlags powinno mieć odpowiadającą właściwość Flag* w VM " +
            "(jeśli dodano nowe pole proto, dodaj mapowanie w ProtoToVmFlagMap i właściwość w VM)");
    }

    [Fact]
    public void ProtoToVmFlagMap_CoversEveryProtoField()
    {
        var protoFields = AppearanceFlags.Descriptor.Fields
            .InDeclarationOrder()
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        ProtoToVmFlagMap.Keys.Should().BeEquivalentTo(protoFields,
            "mapa ProtoToVmFlagMap musi pokrywać wszystkie pola proto");
    }
}
