using FluentAssertions;
using Narzedzia.Core.Appearances;
using Narzedzia.Core.Tibia12;

namespace Narzedzia.Core.Tests;

public sealed class ShiftServiceTests
{
    [Theory]
    [InlineData(-32, -32)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(15, -7)]
    [InlineData(32, 32)]
    public void SetShift_WithDefaultRange_AcceptsValuesInsideMinus32To32(int x, int y)
    {
        var appearance = new Appearance { Id = 1 };

        ShiftService.SetShift(appearance, x, y);

        appearance.Flags!.Shift.Should().NotBeNull();
        var (sx, sy) = ShiftService.GetShift(appearance);
        sx.Should().Be(x);
        sy.Should().Be(y);
    }

    [Theory]
    [InlineData(-33, 0)]
    [InlineData(33, 0)]
    [InlineData(0, -33)]
    [InlineData(0, 33)]
    [InlineData(-512, 0)]
    [InlineData(512, 0)]
    public void SetShift_WithDefaultRange_RejectsValuesOutsideMinus32To32(int x, int y)
    {
        var appearance = new Appearance { Id = 1 };

        var act = () => ShiftService.SetShift(appearance, x, y);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-512, 0, -512, 512)]
    [InlineData(-100, 100, -512, 512)]
    [InlineData(512, -512, -512, 512)]
    public void SetShift_WithCustomRange_AcceptsValuesInsideRange(int x, int y, int min, int max)
    {
        var appearance = new Appearance { Id = 1 };

        ShiftService.SetShift(appearance, x, y, min, max);

        var (sx, sy) = ShiftService.GetShift(appearance);
        sx.Should().Be(x);
        sy.Should().Be(y);
    }

    [Fact]
    public void SetShift_InvalidRange_Throws()
    {
        var appearance = new Appearance { Id = 1 };

        var act = () => ShiftService.SetShift(appearance, 0, 0, min: 10, max: 5);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(-32)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(32)]
    public void SetShift_NegativeRoundTripsAsBitPattern(int value)
    {
        var appearance = new Appearance { Id = 1 };

        ShiftService.SetShift(appearance, value, value);

        // Proto field is uint32; bit pattern of negative values is preserved via unchecked cast
        var storedX = appearance.Flags!.Shift!.X;
        unchecked((int)storedX).Should().Be(value);
    }

    [Fact]
    public void Clear_RemovesShiftFromFlags()
    {
        var appearance = new Appearance { Id = 1 };
        ShiftService.SetShift(appearance, 5, 5);

        ShiftService.Clear(appearance);

        appearance.Flags!.Shift.Should().BeNull();
    }

    [Theory]
    [InlineData(-1, 0, true)]
    [InlineData(0, -1, true)]
    [InlineData(-5, -5, true)]
    [InlineData(0, 0, false)]
    [InlineData(5, 10, false)]
    public void HasNegativeShift_DetectsNegativeAxis(int x, int y, bool expected)
    {
        var appearance = new Appearance { Id = 1 };
        ShiftService.SetShift(appearance, x, y);

        ShiftService.HasNegativeShift(appearance).Should().Be(expected);
    }
}
