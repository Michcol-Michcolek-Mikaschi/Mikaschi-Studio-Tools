using FluentAssertions;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Narzedzia.Core.Tests;

public sealed class SprParserRoundTripTests
{
    [Fact]
    public void SaveThenParse_PreservesSpritePayloads()
    {
        var parser = new SprParser();
        var file = new SprFile
        {
            Signature = 0x00000000,
            ExtendedSprites = true,
            Sprites =
            {
                new SpriteEntry { IsEmpty = true },
                new SpriteEntry
                {
                    IsEmpty = false,
                    CompressedData = [0x00, 0x00, 0x01, 0x00, 0x10, 0x20, 0x30]
                }
            }
        };

        var path = Path.GetTempFileName();
        try
        {
            parser.Save(file, path);
            var loaded = parser.Parse(path);

            loaded.ExtendedSprites.Should().BeTrue();
            loaded.Sprites.Should().HaveCount(2);
            loaded.Sprites[0].IsEmpty.Should().BeTrue();
            loaded.Sprites[1].IsEmpty.Should().BeFalse();
            loaded.Sprites[1].CompressedData.Should().Equal(file.Sprites[1].CompressedData!);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_AutoDetectsExtendedSpritesWithNormalSignature()
    {
        var parser = new SprParser();
        var file = new SprFile
        {
            Signature = 0x12345678,
            ExtendedSprites = true,
            Sprites =
            {
                new SpriteEntry
                {
                    IsEmpty = false,
                    CompressedData = [0x00, 0x00, 0x01, 0x00, 0x10, 0x20, 0x30]
                },
                new SpriteEntry { IsEmpty = true }
            }
        };

        var path = Path.GetTempFileName();
        try
        {
            parser.Save(file, path);
            var loaded = parser.Parse(path);

            loaded.ExtendedSprites.Should().BeTrue();
            loaded.Sprites.Should().HaveCount(2);
            loaded.Sprites[0].CompressedData.Should().Equal(file.Sprites[0].CompressedData!);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
