using FluentAssertions;
using Modules.AssetsEditor.Services;
using Narzedzia.Core.Tibia12;

namespace Modules.AssetsEditor.Tests;

/// <summary>
/// Weryfikuje round-trip konwersji Shift: int (UI) ↔ uint (proto wire format).
/// Proto używa uint32 (kompatybilne z OTClient mehah), UI używa int ze znakiem.
/// Konwersja: unchecked((uint)int) / unchecked((int)uint) — zachowuje wzorzec bitowy.
/// </summary>
public sealed class ShiftConversionTests
{
    [Theory]
    [InlineData(0,    0)]
    [InlineData(1,    1)]
    [InlineData(100,  100)]
    [InlineData(512,  512)]
    [InlineData(-1,   4294967295u)]   // 0xFFFFFFFF
    [InlineData(-100, 4294967196u)]   // 0xFFFFFF9C
    [InlineData(-512, 4294966784u)]   // 0xFFFFFE00
    public void ShiftX_SignedToUint_MatchesTwosComplement(int signedValue, uint expectedUint)
    {
        unchecked((uint)signedValue).Should().Be(expectedUint);
    }

    [Theory]
    [InlineData(0u,          0)]
    [InlineData(1u,          1)]
    [InlineData(100u,        100)]
    [InlineData(512u,        512)]
    [InlineData(4294967295u, -1)]
    [InlineData(4294967196u, -100)]
    [InlineData(4294966784u, -512)]
    public void ShiftX_UintToSigned_MatchesTwosComplement(uint uintValue, int expectedSigned)
    {
        unchecked((int)uintValue).Should().Be(expectedSigned);
    }

    [Theory]
    [InlineData(0,    0)]
    [InlineData(128,  128)]
    [InlineData(-100, -100)]
    [InlineData(-512, -512)]
    [InlineData(512,  512)]
    public void Apply_ShiftXY_RoundTrip_PreservesSignedValue(int shiftX, int shiftY)
    {
        var appearance = new Appearance { Id = 1, Flags = new AppearanceFlags() };
        var state = new AppearanceEditState
        {
            HasShift = true,
            ShiftX   = shiftX,
            ShiftY   = shiftY,
            ShiftMin = -512,
            ShiftMax = 512,
        };

        AppearanceEditorService.Apply(appearance, state);

        // Proto przechowuje uint32 — sprawdzamy że wzorzec bitowy jest poprawny
        appearance.Flags.Shift.Should().NotBeNull();
        var storedX = appearance.Flags.Shift!.X;  // uint32
        var storedY = appearance.Flags.Shift!.Y;  // uint32

        unchecked((int)storedX).Should().Be(shiftX,  "round-trip X musi odtworzyć signed int");
        unchecked((int)storedY).Should().Be(shiftY,  "round-trip Y musi odtworzyć signed int");
    }

    [Fact]
    public void Apply_ShiftDisabled_ClearsProtoField()
    {
        var appearance = new Appearance
        {
            Id    = 2,
            Flags = new AppearanceFlags { Shift = new AppearanceFlagShift { X = 50, Y = 50 } },
        };
        var state = new AppearanceEditState { HasShift = false };

        AppearanceEditorService.Apply(appearance, state);

        appearance.Flags.Shift.Should().BeNull("wyłączona flaga shift musi usunąć pole z proto");
    }
}
