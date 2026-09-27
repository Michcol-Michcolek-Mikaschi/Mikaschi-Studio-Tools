using FluentAssertions;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Narzedzia.Core.Tests;

public class SprParserTests
{
    [Fact]
    public void SprParser_ThrowsFileNotFoundException_WhenFileNotFound()
    {
        var parser = new SprParser();
        var act = () => parser.Parse("nonexistent.spr");
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void SprFile_HasEmptySpritesList_ByDefault()
    {
        var file = new SprFile();
        file.Sprites.Should().BeEmpty();
    }

    [Fact]
    public void SprParser_SaveAndReload_RoundTrip()
    {
        var path = Path.GetTempFileName();
        try
        {
            var original = new SprFile
            {
                Signature = 0x12345678,
                ExtendedSprites = false,
                Sprites = new List<SpriteEntry>
                {
                    new SpriteEntry { IsEmpty = true },
                    new SpriteEntry { IsEmpty = false, CompressedData = new byte[] { 0x20,0x00, 0x01,0x00, 0xFF,0x00,0x80 } }
                }
            };

            var parser = new SprParser();
            parser.Save(original, path);

            var loaded = parser.Parse(path);
            loaded.Signature.Should().Be(0x12345678);
            loaded.Sprites.Should().HaveCount(2);
            loaded.Sprites[0].IsEmpty.Should().BeTrue();
            loaded.Sprites[1].IsEmpty.Should().BeFalse();
            loaded.Sprites[1].CompressedData.Should().NotBeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DecompressSprite_EmptyEntry_ReturnsMagentaPixels()
    {
        var entry = new SpriteEntry { IsEmpty = true };
        var rgba = SprParser.DecompressSprite(entry);
        rgba.Should().HaveCount(SprParser.SpriteDataSize);
        // First pixel should be magenta (255,0,255,255)
        rgba[0].Should().Be(255); // R
        rgba[1].Should().Be(0);   // G
        rgba[2].Should().Be(255); // B
        rgba[3].Should().Be(255); // A
    }

    [Fact]
    public void DecompressSprite_WithTransparency_ReadsRgbaWithoutChannelShift()
    {
        var entry = new SpriteEntry
        {
            CompressedData = [
                0x00, 0x00, 0x02, 0x00,
                0x11, 0x22, 0x33, 0x44,
                0x55, 0x66, 0x77, 0x88]
        };

        var rgba = SprParser.DecompressSprite(entry, transparentSprites: true);

        rgba[..8].Should().Equal(0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompressSpriteFromBgra_RoundTripsPixelChannels(bool transparentSprites)
    {
        var bgra = new byte[SprParser.SpriteDataSize];
        bgra[0] = 0x33;
        bgra[1] = 0x22;
        bgra[2] = 0x11;
        bgra[3] = transparentSprites ? (byte)0x44 : (byte)0xFF;

        var entry = SprParser.CompressSpriteFromBgra(bgra, transparentSprites);
        var rgba = SprParser.DecompressSprite(entry, transparentSprites);

        rgba[..4].Should().Equal(0x11, 0x22, 0x33, transparentSprites ? (byte)0x44 : (byte)0xFF);
    }

    [Fact]
    public void Parse_AutoDetectsRgbaSpritePayload()
    {
        var path = Path.GetTempFileName();
        try
        {
            var bgra = new byte[SprParser.SpriteDataSize];
            bgra[0] = 0x33;
            bgra[1] = 0x22;
            bgra[2] = 0x11;
            bgra[3] = 0x44;
            var source = new SprFile
            {
                Signature = 0x12345678,
                ExtendedSprites = false,
                TransparentSprites = true,
                Sprites = [SprParser.CompressSpriteFromBgra(bgra, transparentSprites: true)]
            };
            var parser = new SprParser();
            parser.Save(source, path);

            var loaded = parser.Parse(path);
            var rgba = SprParser.DecompressSprite(loaded.Sprites[0], loaded.TransparentSprites);

            loaded.TransparentSprites.Should().BeTrue();
            rgba[..4].Should().Equal(0x11, 0x22, 0x33, 0x44);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_AcceptsObjectBuilderEndOfFileSentinelOffset()
    {
        var path = Path.GetTempFileName();
        try
        {
            var parser = new SprParser();
            parser.Save(new SprFile
            {
                Signature = 0x4C220594,
                ExtendedSprites = false,
                Sprites =
                [
                    new SpriteEntry { CompressedData = [0, 0, 1, 0, 255, 0, 0] },
                    new SpriteEntry { CompressedData = [0, 0, 1, 0, 0, 255, 0] }
                ]
            }, path);

            var bytes = File.ReadAllBytes(path);
            BitConverter.GetBytes((uint)bytes.Length).CopyTo(bytes, 10);
            File.WriteAllBytes(path, bytes);

            var loaded = parser.Parse(path);

            loaded.Sprites.Should().HaveCount(2);
            loaded.Sprites[0].IsEmpty.Should().BeFalse();
            loaded.Sprites[1].IsEmpty.Should().BeTrue();
            loaded.ParseWarning.Should().BeNull();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_LoadsValidSpritesFromTruncatedObjectBuilderFileWithWarning()
    {
        var path = Path.GetTempFileName();
        try
        {
            var parser = new SprParser();
            parser.Save(new SprFile
            {
                Signature = 0,
                ExtendedSprites = true,
                Sprites = Enumerable.Range(0, 10)
                    .Select(index => new SpriteEntry
                    {
                        CompressedData = [0, 0, 1, 0, (byte)index, 0, 0]
                    })
                    .ToList()
            }, path);

            var bytes = File.ReadAllBytes(path);
            var ninthSpriteOffset = BitConverter.ToUInt32(bytes, 8 + 8 * 4);
            using (var stream = File.Open(path, FileMode.Open, FileAccess.Write))
            {
                stream.SetLength(ninthSpriteOffset);
            }

            var loaded = parser.Parse(path);

            loaded.ExtendedSprites.Should().BeTrue();
            loaded.Sprites.Should().HaveCount(10);
            loaded.Sprites.Take(8).Should().OnlyContain(sprite => !sprite.IsEmpty);
            loaded.Sprites[8].IsEmpty.Should().BeTrue();
            loaded.Sprites[9].IsEmpty.Should().BeTrue();
            loaded.ParseWarning.Should().Contain("1 nieprawidłowych");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Parse_ReportsEncryptedEnc3FileClearly()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [(byte)'E', (byte)'N', (byte)'C', (byte)'3', 1, 2, 3, 4]);

            var act = () => new SprParser().Parse(path);

            act.Should().Throw<InvalidDataException>()
                .WithMessage("*zaszyfrowany (ENC3)*klucz*");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
