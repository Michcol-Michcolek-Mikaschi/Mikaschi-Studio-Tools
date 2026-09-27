using FluentAssertions;
using Narzedzia.Core.Assets;

namespace Narzedzia.Core.Tests;

public sealed class LzmaImageCodecTests
{
    [Fact]
    public void EncodeCipSoftImage_DecodesPayloadBytes()
    {
        var original = Enumerable.Range(0, 4096).Select(i => (byte)(i % 251)).ToArray();

        var encoded = LzmaImageCodec.EncodeCipSoftImage(original);
        var decoded = LzmaImageCodec.DecodeImageBytes(encoded);

        decoded.Should().Equal(original);
    }

    /// <summary>
    /// Realistyczny rozmiar arkusza CipSoft Tibia (384x384 = 147456 pikseli * 4 bajty = 589824 + nagłówek BMP).
    /// Weryfikuje, że LZMA z dictionarySize=1<<25 round-tripuje bez utraty.
    /// </summary>
    [Fact]
    public void EncodeCipSoftImage_LargePayload_RoundTrips()
    {
        var rng = new Random(42);
        var original = new byte[600_000];
        rng.NextBytes(original);

        var encoded = LzmaImageCodec.EncodeCipSoftImage(original);
        var decoded = LzmaImageCodec.DecodeImageBytes(encoded);

        decoded.Should().Equal(original);
    }
}
