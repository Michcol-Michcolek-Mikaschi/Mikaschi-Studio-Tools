using FluentAssertions;
using Modules.AssetsEditor.Localization;

namespace Modules.AssetsEditor.Tests;

public sealed class AssetsEditorLabelsTests
{
    [Fact]
    public void PropertyLabels_MatchOriginalDatEditorNames()
    {
        AssetsEditorLabels.Flags.Should().NotBeEmpty();

        var expected = new Dictionary<string, string>
        {
            ["Transparency"] = "Transparency",
            ["Bank"] = "Ground",
            ["Clip"] = "Clip",
            ["Bottom"] = "Bottom",
            ["Top"] = "Top",
            ["Container"] = "Container",
            ["Cumulative"] = "Cumulative",
            ["Usable"] = "Usable",
            ["Forceuse"] = "Forceuse",
            ["Multiuse"] = "Multiuse",
            ["Write"] = "Write",
            ["WriteOnce"] = "Write Once",
            ["Liquidpool"] = "Liquid Pool",
            ["Unpass"] = "Unpass",
            ["Unmove"] = "Unmove",
            ["Unsight"] = "Unsight",
            ["Avoid"] = "Avoid",
            ["NoMovementAnimation"] = "No Move Animation",
            ["Take"] = "Take",
            ["Liquidcontainer"] = "Liquid Container",
            ["Hang"] = "Hang",
            ["Hook"] = "Hook",
            ["HookSouth"] = "Hook South",
            ["HookEast"] = "Hook East",
            ["Rotate"] = "Rotate",
            ["Light"] = "Light",
            ["DontHide"] = "Dont Hide",
            ["Translucent"] = "Translucent",
            ["Shift"] = "Shift",
            ["Height"] = "Height",
            ["ReverseAddons"] = "Reverse addons",
            ["LyingObject"] = "Lying Object",
            ["AnimateAlways"] = "Animate Always",
            ["Automap"] = "Auto Map",
            ["Lenshelp"] = "Lens help",
            ["Fullbank"] = "Full Ground",
            ["IgnoreLook"] = "Ignore Look",
            ["Clothes"] = "Clothes",
            ["DefaultAction"] = "Default Action",
            ["Market"] = "Market",
            ["Wrap"] = "Wrap",
            ["Unwrap"] = "Unwrap",
            ["Topeffect"] = "Top Effect",
            ["DecoItemKit"] = "Deco Item Kit",
            ["Changedtoexpire"] = "Changed To Expire",
            ["Corpse"] = "Corpse",
            ["PlayerCorpse"] = "Player Corpse",
            ["Npcsaledata"] = "Npc Sale Data",
            ["ShowOffSocket"] = "Podium",
            ["Reportable"] = "Reportable",
            ["Upgradeclassification"] = "Upgrade classification",
            ["Wearout"] = "Wearout",
            ["Clockexpire"] = "Clock expire",
            ["Expire"] = "Expire",
            ["Expirestop"] = "Expire stop",
            ["Cyclopediaitem"] = "Cyclopedia",
            ["Ammo"] = "Ammo",
            ["SkillwheelGem"] = "WheelGem",
            ["DualWielding"] = "Dual Wielding",
            ["Imbueable"] = "Imbueable",
            ["Proficiency"] = "Proficiency",
            ["MinimumLevel"] = "Minimum Level",
            ["WeaponType"] = "Weapon Type",
            ["RestrictToVocation"] = "Restrict Vocation"
        };

        foreach (var (key, label) in expected)
        {
            AssetsEditorLabels.Flags[key].Label.Should().Be(label);
            AssetsEditorLabels.Flags[key].Tip.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Labels_ContainAllRequiredTechnicalFields()
    {
        var requiredKeys = new[]
        {
            "Transparency", "Bank", "Clip", "Bottom", "Top", "Container", "Cumulative",
            "Usable", "Forceuse", "Multiuse", "Write", "WriteOnce", "Liquidpool", "Unpass",
            "Unmove", "Unsight", "Avoid", "NoMovementAnimation", "Take", "Liquidcontainer",
            "Hang", "Hook", "HookSouth", "HookEast", "Rotate", "Light", "DontHide",
            "Translucent", "Shift", "Height", "ReverseAddons", "LyingObject", "AnimateAlways",
            "Automap", "Lenshelp", "Fullbank", "IgnoreLook", "Clothes", "DefaultAction",
            "Market", "Wrap", "Unwrap", "Topeffect", "DecoItemKit", "Changedtoexpire", "Corpse",
            "PlayerCorpse", "Npcsaledata", "ShowOffSocket", "Reportable", "Upgradeclassification",
            "Wearout", "Clockexpire", "Expire", "Expirestop", "Cyclopediaitem", "Ammo",
            "SkillwheelGem", "DualWielding", "Imbueable", "Proficiency", "MinimumLevel",
            "WeaponType", "RestrictToVocation"
        };

        foreach (var key in requiredKeys)
        {
            AssetsEditorLabels.Flags.ContainsKey(key).Should().BeTrue($"brakuje wpisu {key}");
        }
    }
}
