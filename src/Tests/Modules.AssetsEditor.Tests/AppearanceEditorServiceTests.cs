using FluentAssertions;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Tests;

public sealed class AppearanceEditorServiceTests
{
    [Fact]
    public void ApplyFlags_WritesAndClearsOptionalFlags()
    {
        var appearance = new Appearance { Id = 100, Flags = new AppearanceFlags() };
        var input = new AppearanceEditState
        {
            Name = "Kunai",
            Description = "Bron dystansowa",
            IsGround = true,
            GroundSpeed = 220,
            IsUnpassable = true,
            HasLight = true,
            LightBrightness = 8,
            LightColor = 215
        };

        AppearanceEditorService.Apply(appearance, input);

        appearance.Name.Should().Be("Kunai");
        appearance.Description.Should().Be("Bron dystansowa");
        appearance.Flags.Bank!.Waypoints.Should().Be(220);
        appearance.Flags.Unpass.Should().BeTrue();
        appearance.Flags.Light!.Brightness.Should().Be(8);

        input.IsGround = false;
        input.HasLight = false;
        input.IsUnpassable = false;
        AppearanceEditorService.Apply(appearance, input);

        appearance.Flags.Bank.Should().BeNull();
        appearance.Flags.Light.Should().BeNull();
        appearance.Flags.HasUnpass.Should().BeFalse();
    }

    [Fact]
    public void ApplyFlags_WritesComplexUiFlags()
    {
        var appearance = new Appearance { Id = 101, Flags = new AppearanceFlags() };
        var input = new AppearanceEditState
        {
            HasTransparency = true,
            TransparencyLevel = 3,
            IsWriteable = true,
            WriteMaxLength = 128,
            HasShift = true,
            ShiftX = -4,
            ShiftY = 6,
            HasMarket = true,
            MarketCategoryIndex = 4,
            MarketTradeAsObjectId = 100,
            MarketShowAsObjectId = 101,
            MarketMinimumLevel = 20,
            MarketVocations = { VOCATION.Knight, VOCATION.Promoted },
            RestrictToVocations = { VOCATION.Paladin },
            MinimumLevel = 15,
            WeaponType = WEAPON_TYPE.Sword
        };

        AppearanceEditorService.Apply(appearance, input);

        appearance.Flags.Transparencylevel!.Level.Should().Be(3);
        appearance.Flags.Write!.MaxTextLength.Should().Be(128);
        unchecked((int)appearance.Flags.Shift!.X).Should().Be(-4);
        appearance.Flags.Market!.Category.Should().Be(ITEM_CATEGORY.Decoration);
        appearance.Flags.Market.RestrictToVocation.Should().Equal(VOCATION.Knight, VOCATION.Promoted);
        appearance.Flags.RestrictToVocation.Should().Equal(VOCATION.Paladin);
        appearance.Flags.MinimumLevel.Should().Be(15);
        appearance.Flags.WeaponType.Should().Be(WEAPON_TYPE.Sword);

        input.HasTransparency = false;
        input.IsWriteable = false;
        input.HasShift = false;
        input.HasMarket = false;
        input.MinimumLevel = 0;
        input.WeaponType = WEAPON_TYPE.Noweapon;
        input.RestrictToVocations.Clear();
        AppearanceEditorService.Apply(appearance, input);

        appearance.Flags.Transparencylevel.Should().BeNull();
        appearance.Flags.Write.Should().BeNull();
        appearance.Flags.Shift.Should().BeNull();
        appearance.Flags.Market.Should().BeNull();
        appearance.Flags.HasMinimumLevel.Should().BeFalse();
        appearance.Flags.HasWeaponType.Should().BeFalse();
        appearance.Flags.RestrictToVocation.Should().BeEmpty();
    }

    [Fact]
    public void ApplyFlags_WritesAndClearsNestedReferenceEditorFields()
    {
        var appearance = new Appearance { Id = 102, Flags = new AppearanceFlags() };
        var input = new AppearanceEditState
        {
            HasCyclopedia = true,
            CyclopediaType = 7,
            HasNpcSaleData = true,
            NpcSaleData =
            {
                new NpcSaleDataEditState
                {
                    Name = "Alesar",
                    Location = "Darashia",
                    BuyPrice = 120,
                    SalePrice = 80,
                    CurrencyObjectTypeId = 3031,
                    CurrencyQuestFlagDisplayName = "gold coin"
                }
            },
            RestrictToVocations = { VOCATION.Monk }
        };

        AppearanceEditorService.Apply(appearance, input);

        appearance.Flags.Cyclopediaitem!.CyclopediaType.Should().Be(7);
        appearance.Flags.Npcsaledata.Should().ContainSingle();
        appearance.Flags.Npcsaledata[0].Name.Should().Be("Alesar");
        appearance.Flags.Npcsaledata[0].Location.Should().Be("Darashia");
        appearance.Flags.Npcsaledata[0].BuyPrice.Should().Be(120);
        appearance.Flags.Npcsaledata[0].SalePrice.Should().Be(80);
        appearance.Flags.Npcsaledata[0].CurrencyObjectTypeId.Should().Be(3031);
        appearance.Flags.Npcsaledata[0].CurrencyQuestFlagDisplayName.Should().Be("gold coin");
        appearance.Flags.RestrictToVocation.Should().Equal(VOCATION.Monk);

        input.HasCyclopedia = false;
        input.HasNpcSaleData = false;
        input.RestrictToVocations.Clear();
        AppearanceEditorService.Apply(appearance, input);

        appearance.Flags.Cyclopediaitem.Should().BeNull();
        appearance.Flags.Npcsaledata.Should().BeEmpty();
        appearance.Flags.RestrictToVocation.Should().BeEmpty();
    }
}
