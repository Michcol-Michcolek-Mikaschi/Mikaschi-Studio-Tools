using FluentAssertions;
using Modules.AssetsEditor.Services;
using Modules.AssetsEditor.ViewModels;
using Narzedzia.Core.Assets;
using Narzedzia.Core.Tibia12;
using NSubstitute;

namespace Modules.AssetsEditor.Tests;

public class AssetsEditorViewModelNavigationTests
{
    [Fact]
    public void CurrentDirection_ShouldUpdateDirectionFlagsAndLabel()
    {
        var service = Substitute.For<IAssetsService>();
        var vm = new AssetsEditorViewModel(service);

        vm.CurrentDirection = 2;

        vm.IsDirectionDown.Should().BeTrue();
        vm.IsDirectionUp.Should().BeFalse();
        vm.IsDirectionRight.Should().BeFalse();
        vm.IsDirectionLeft.Should().BeFalse();
        vm.CurrentDirectionName.Should().Be("Dół");
    }

    [Fact]
    public void SetDirectionCommand_ShouldClampToMaxDirectionIndex()
    {
        var service = Substitute.For<IAssetsService>();
        var vm = new AssetsEditorViewModel(service)
        {
            PropPatternX = 2
        };

        vm.SetDirectionCommand.Execute("3");

        vm.CurrentDirection.Should().Be(1);
        vm.IsDirectionRight.Should().BeTrue();
        vm.CurrentDirectionName.Should().Be("Prawo");
    }

    [Fact]
    public void BuildAppearanceEditState_ShouldPreserveSignedShiftValues()
    {
        var service = Substitute.For<IAssetsService>();
        var vm = new AssetsEditorViewModel(service)
        {
            FlagShift = true,
            FlagShiftX = -7,
            FlagShiftY = 12
        };

        var state = vm.BuildAppearanceEditStateFromViewModel();

        state.HasShift.Should().BeTrue();
        state.ShiftX.Should().Be(-7);
        state.ShiftY.Should().Be(12);
    }

    [Fact]
    public void BuildAppearanceEditState_ShouldIncludeCyclopediaNpcDataAndMonk()
    {
        var service = Substitute.For<IAssetsService>();
        var vm = new AssetsEditorViewModel(service)
        {
            FlagCyclopedia = true,
            FlagCyclopediaType = 9,
            FlagNpcSaleData = true,
            FlagRestrictVocMonk = true
        };
        vm.NpcSaleEntries.Add(new NpcSaleEntryViewModel
        {
            Name = "Rashid",
            Location = "Carlin",
            BuyPrice = 500,
            SalePrice = 250,
            CurrencyObjectTypeId = 3031,
            CurrencyQuestFlagDisplayName = "gold coin"
        });

        var state = vm.BuildAppearanceEditStateFromViewModel();

        state.HasCyclopedia.Should().BeTrue();
        state.CyclopediaType.Should().Be(9);
        state.RestrictToVocations.Should().Equal(VOCATION.Monk);
        state.NpcSaleData.Should().ContainSingle().Which.Name.Should().Be("Rashid");
        state.NpcSaleData[0].CurrencyObjectTypeId.Should().Be(3031);
    }

    [Fact]
    public void AssignSpriteFromPreviewDrop_ShouldUseCurrentDirectionAndFrame()
    {
        var appearances = new Appearances();
        var service = Substitute.For<IAssetsService>();
        service.AppearancesData.Returns(appearances);
        service.IsLoaded.Returns(true);
        service.GetSpriteSize(777).Returns((64, 64));
        service.GetSpriteSlots(Arg.Any<Appearance>(), Arg.Any<int>())
            .Returns(call => AppearanceTextureLayout.EnumerateSpriteSlots(call.ArgAt<Appearance>(0), call.ArgAt<int>(1)));

        var vm = new AssetsEditorViewModel(service)
        {
            ActiveCategory = 1,
            PreviewPixelWidth = 64,
            PreviewPixelHeight = 64
        };

        vm.NewAppearanceCommand.Execute(null);
        vm.SetDirectionCommand.Execute("1");
        vm.AssignSpriteFromPreviewDrop(777, pointerX: 120, pointerY: 120, surfaceWidth: 240, surfaceHeight: 240);

        var spriteInfo = vm.Selected!.Source.FrameGroup[0].SpriteInfo;
        spriteInfo.SpriteId.Should().HaveCount(4);
        spriteInfo.SpriteId[1].Should().Be(777);
    }

    [Fact]
    public void SelectingOutfit_ShouldOpenIdleGroupAndExposeRealFrameCounts()
    {
        var service = Substitute.For<IAssetsService>();
        service.GetSpriteSlots(Arg.Any<Appearance>(), Arg.Any<int>())
            .Returns(call => AppearanceTextureLayout.EnumerateSpriteSlots(
                call.ArgAt<Appearance>(0),
                call.ArgAt<int>(1)));
        var appearance = new Appearance
        {
            Id = 5,
            AppearanceType = APPEARANCE_TYPE.AppearanceOutfit,
            FrameGroup =
            {
                new FrameGroup
                {
                    FixedFrameGroup = FIXED_FRAME_GROUP.OutfitIdle,
                    SpriteInfo = new SpriteInfo
                    {
                        PatternWidth = 4,
                        PatternHeight = 1,
                        PatternDepth = 1,
                        Layers = 1,
                        SpriteId = { 1u, 2u, 3u, 4u }
                    }
                },
                new FrameGroup
                {
                    FixedFrameGroup = FIXED_FRAME_GROUP.OutfitMoving,
                    SpriteInfo = new SpriteInfo
                    {
                        PatternWidth = 4,
                        PatternHeight = 1,
                        PatternDepth = 1,
                        Layers = 1,
                        SpriteId = { Enumerable.Range(5, 12).Select(id => (uint)id) },
                        Animation = AnimationWithFrames(3)
                    }
                }
            }
        };
        var vm = new AssetsEditorViewModel(service);
        var frameGroupCollection = vm.TextureFrameGroups;

        vm.Selected = new AppearanceListItem(appearance, APPEARANCE_TYPE.AppearanceOutfit);

        vm.SelectedGroupIndex.Should().Be(0);
        vm.TextureFrameGroups.Should().BeSameAs(frameGroupCollection);
        vm.PropFrames.Should().Be(1);
        vm.TextureFrameGroups.Select(group => group.DisplayName).Should().Equal(
            "1. Idle / bezczynność · 1 klatka",
            "2. Walking / ruch · 3 klatki");

        vm.NextFrameCommand.Execute(null);
        vm.CurrentFrameIndex.Should().Be(0);

        vm.SelectedGroupIndex = 1;
        vm.PropFrames.Should().Be(3);
        vm.NextFrameCommand.Execute(null);
        vm.CurrentFrameIndex.Should().Be(1);
        vm.FrameLabel.Should().Be("2/3");
    }

    [Fact]
    public void ApplyAppearanceId_ShouldRejectDuplicateAndAcceptUniqueId()
    {
        var first = new Appearance { Id = 100, Flags = new AppearanceFlags() };
        var second = new Appearance { Id = 101, Flags = new AppearanceFlags() };
        var appearances = new Appearances { Object = { first, second } };
        var service = Substitute.For<IAssetsService>();
        service.AppearancesData.Returns(appearances);
        var vm = new AssetsEditorViewModel(service)
        {
            Selected = new AppearanceListItem(first, APPEARANCE_TYPE.AppearanceObject),
            AppearanceId = 101
        };

        vm.ApplyAppearanceIdCommand.Execute(null);

        first.Id.Should().Be(100);
        vm.AppearanceId.Should().Be(100);
        vm.StatusText.Should().Contain("jest już zajęte");

        vm.AppearanceId = 150;
        vm.ApplyAppearanceIdCommand.Execute(null);

        first.Id.Should().Be(150);
        vm.Selected!.Source.Should().BeSameAs(first);
        vm.StatusText.Should().Contain("z #100 na #150");
    }

    [Fact]
    public void SelectingAppearance_ShouldLoadAndAutoPersistNestedPropertyFields()
    {
        var appearance = new Appearance
        {
            Id = 200,
            Flags = new AppearanceFlags
            {
                Lenshelp = new AppearanceFlagLenshelp { Id = 1104 },
                Clothes = new AppearanceFlagClothes { Slot = 6 },
                DefaultAction = new AppearanceFlagDefaultAction { Action = PLAYER_ACTION.Use },
                Cyclopediaitem = new AppearanceFlagCyclopedia { CyclopediaType = 12 },
                WeaponType = WEAPON_TYPE.Crossbow,
                RestrictToVocation = { VOCATION.Monk },
                Npcsaledata =
                {
                    new AppearanceFlagNPC
                    {
                        Name = "Yasir",
                        Location = "Carlin",
                        BuyPrice = 40,
                        SalePrice = 25
                    }
                }
            }
        };
        var service = Substitute.For<IAssetsService>();
        var vm = new AssetsEditorViewModel(service);

        vm.Selected = new AppearanceListItem(appearance, APPEARANCE_TYPE.AppearanceObject);

        vm.FlagLenshelpSelectionIndex.Should().Be(4);
        vm.FlagClothesSlot.Should().Be(6);
        vm.FlagDefaultActionIndex.Should().Be((int)PLAYER_ACTION.Use);
        vm.FlagCyclopediaType.Should().Be(12);
        vm.FlagWeaponTypeIndex.Should().Be((int)WEAPON_TYPE.Crossbow);
        vm.FlagRestrictVocMonk.Should().BeTrue();
        vm.NpcSaleEntries.Should().ContainSingle();
        vm.SelectedNpcSaleEntry.Should().BeSameAs(vm.NpcSaleEntries[0]);

        vm.SelectedNpcSaleEntry!.BuyPrice = 75;

        appearance.Flags.Npcsaledata.Should().ContainSingle();
        appearance.Flags.Npcsaledata[0].BuyPrice.Should().Be(75);
    }

    private static SpriteAnimation AnimationWithFrames(int count)
    {
        var animation = new SpriteAnimation();
        for (var index = 0; index < count; index++)
        {
            animation.SpritePhase.Add(new SpritePhase { DurationMin = 100, DurationMax = 100 });
        }

        return animation;
    }
}
