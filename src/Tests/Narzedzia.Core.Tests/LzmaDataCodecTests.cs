using FluentAssertions;
using Narzedzia.Core.Compression;

namespace Narzedzia.Core.Tests;

public sealed class LzmaDataCodecTests
{
    [Fact]
    public void CompressAndDecompress_RoundTripsBinaryPayload()
    {
        var source = Enumerable.Range(0, 8192).Select(index => (byte)(index * 31)).ToArray();

        var compressed = LzmaDataCodec.Compress(source);
        var restored = LzmaDataCodec.Decompress(compressed);

        restored.Should().Equal(source);
    }

    [Fact]
    public void Decompress_RejectsTruncatedHeader()
    {
        var act = () => LzmaDataCodec.Decompress([1, 2, 3]);

        act.Should().Throw<InvalidDataException>();
    }
}
