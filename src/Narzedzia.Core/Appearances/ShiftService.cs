using Narzedzia.Core.Tibia12;

namespace Narzedzia.Core.Appearances;

/// <summary>
/// Operacje na shift [przesuniecie sprite'a] zapisanym w appearance.
/// Proto przechowuje X/Y jako uint32 (kompatybilne z OTClient mehah); UI używa
/// ze znakiem int i konwertuje przez unchecked cast (bitowy round-trip).
/// </summary>
public static class ShiftService
{
    /// <summary>Domyślne bounds odpowiadające klientowi Tibia 12+/13+.</summary>
    public const int DefaultMin = -32;
    public const int DefaultMax = 32;

    public static (int x, int y) GetShift(Appearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        var shift = appearance.Flags?.Shift;
        return shift is null ? (0, 0) : (unchecked((int)shift.X), unchecked((int)shift.Y));
    }

    public static void SetShift(Appearance appearance, int x, int y, int min = DefaultMin, int max = DefaultMax)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        if (min > max)
        {
            throw new ArgumentException("Zakres shift jest niepoprawny (min > max).", nameof(min));
        }

        if (x < min || x > max)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Shift X musi byc w zakresie {min}..{max}.");
        }

        if (y < min || y > max)
        {
            throw new ArgumentOutOfRangeException(nameof(y), $"Shift Y musi byc w zakresie {min}..{max}.");
        }

        appearance.Flags ??= new AppearanceFlags();
        appearance.Flags.Shift = new AppearanceFlagShift
        {
            X = unchecked((uint)x),
            Y = unchecked((uint)y)
        };
    }

    public static bool HasNegativeShift(Appearance appearance)
    {
        var (x, y) = GetShift(appearance);
        return x < 0 || y < 0;
    }

    public static void Clear(Appearance appearance)
    {
        ArgumentNullException.ThrowIfNull(appearance);

        if (appearance.Flags is null)
        {
            return;
        }

        appearance.Flags.Shift = null;
    }
}
