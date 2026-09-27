using FluentAssertions;
using Narzedzia.Core.Models;
using Narzedzia.Core.Parsers;

namespace Narzedzia.Core.Tests;

public class OtbParserTests
{
    [Fact]
    public void OtbParser_ThrowsFileNotFoundException_WhenFileNotFound()
    {
        var parser = new OtbParser();
        var act = () => parser.Parse("nonexistent.otb");
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void OtbFile_HasEmptyItemsList_ByDefault()
    {
        var file = new OtbFile();
        file.Items.Should().BeEmpty();
    }

    [Fact]
    public void OtbParser_SaveAndReload_RoundTrip()
    {
        var path = Path.GetTempFileName();
        try
        {
            var original = new OtbFile
            {
                MajorVersion = 1,
                MinorVersion = 2,
                BuildNumber  = 3,
                Description  = "Test OTB",
                Items = new List<OtbItem>
                {
                    new OtbItem
                    {
                        ServerId = 100,
                        ClientId = 200,
                        Name = "TestItem",
                        ItemType = OtbItemType.Ground,
                        Flags = 0x06A0C0DF,
                        Speed = 140,
                        SpriteHash = [0xFD, 0xFE, 0xFF, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
                        MinimapColor = 129,
                        MaxReadWriteChars = 512,
                        MaxReadChars = 1024,
                        LightLevel = 3,
                        LightColor = 156,
                        StackOrder = 2,
                        TradeAs = 3031,
                        RawAttributes = new Dictionary<byte, byte[]> { [0x31] = [0xFD, 0xFE, 0xFF] }
                    }
                }
            };

            var parser = new OtbParser();
            parser.Save(original, path);

            var loaded = parser.Parse(path);
            loaded.MajorVersion.Should().Be(1);
            loaded.MinorVersion.Should().Be(2);
            loaded.BuildNumber.Should().Be(3);
            loaded.Items.Should().HaveCount(1);
            loaded.Items[0].ServerId.Should().Be(100);
            loaded.Items[0].ClientId.Should().Be(200);
            loaded.Items[0].Name.Should().Be("TestItem");
            loaded.Items[0].ItemType.Should().Be(OtbItemType.Ground);
            loaded.Items[0].Flags.Should().Be(0x06A0C0DF);
            loaded.Items[0].Speed.Should().Be(140);
            loaded.Items[0].SpriteHash.Should().Equal(0xFD, 0xFE, 0xFF, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15);
            loaded.Items[0].MinimapColor.Should().Be(129);
            loaded.Items[0].MaxReadWriteChars.Should().Be(512);
            loaded.Items[0].MaxReadChars.Should().Be(1024);
            loaded.Items[0].LightLevel.Should().Be(3);
            loaded.Items[0].LightColor.Should().Be(156);
            loaded.Items[0].StackOrder.Should().Be(2);
            loaded.Items[0].TradeAs.Should().Be(3031);
            loaded.Items[0].RawAttributes[0x31].Should().Equal(0xFD, 0xFE, 0xFF);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void OtbItem_DefaultValues_AreCorrect()
    {
        var item = new OtbItem();
        item.ServerId.Should().Be(0);
        item.ClientId.Should().Be(0);
        item.Name.Should().BeEmpty();
    }

    [Fact]
    public void OtbParser_ReadsFilesWrittenByPreviousMikaschiOtbWriter()
    {
        var path = Path.GetTempFileName();
        try
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(0u);
            writer.Write((byte)0xFE);
            writer.Write((byte)0);

            var root = new byte[144];
            BitConverter.TryWriteBytes(root.AsSpan(4), 3u);
            BitConverter.TryWriteBytes(root.AsSpan(8), 49u);
            BitConverter.TryWriteBytes(root.AsSpan(12), 1u);
            WriteEscaped(writer, root);

            writer.Write((byte)0xFE);
            writer.Write((byte)3); // Fluid w niekanonicznym zapisie starego modułu.
            var body = new List<byte>();
            body.AddRange(BitConverter.GetBytes(0u));
            AddLegacyAttribute(body, 0x10, BitConverter.GetBytes((ushort)100));
            AddLegacyAttribute(body, 0x11, BitConverter.GetBytes((ushort)200));
            AddLegacyAttribute(body, 0x16, System.Text.Encoding.Latin1.GetBytes("legacy item"));
            AddLegacyAttribute(body, 0x1A, BitConverter.GetBytes((ushort)150));
            WriteEscaped(writer, body);
            writer.Write((byte)0xFF);
            writer.Write((byte)0xFF);
            writer.Flush();
            File.WriteAllBytes(path, stream.ToArray());

            var file = new OtbParser().Parse(path);

            file.MinorVersion.Should().Be(49);
            file.Items.Should().ContainSingle();
            file.Items[0].ItemType.Should().Be(OtbItemType.Fluid);
            file.Items[0].Name.Should().Be("legacy item");
            file.Items[0].Speed.Should().Be(150);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("800")]
    [InlineData("810")]
    [InlineData("820")]
    [InlineData("840")]
    [InlineData("850")]
    [InlineData("854")]
    [InlineData("860")]
    [InlineData("870")]
    [InlineData("910")]
    [InlineData("920")]
    [InlineData("946")]
    [InlineData("954")]
    [InlineData("960")]
    [InlineData("970")]
    [InlineData("986")]
    [InlineData("1010")]
    [InlineData("1020")]
    [InlineData("1031")]
    [InlineData("1041")]
    [InlineData("1077")]
    [InlineData("1098")]
    [InlineData("1310")]
    public void OtbParser_ReadsClassicObjectBuilderFixtures(string version)
    {
        var repository = FindRepositoryRoot();
        var path = Path.Combine(repository, "rme-data", version, "items.otb");

        var file = new OtbParser().Parse(path);

        file.MajorVersion.Should().BeGreaterThan(0);
        file.Items.Should().NotBeEmpty();
        file.Items.Should().OnlyContain(item => item.ServerId > 0);
        file.Items.Where(item => item.ItemType != OtbItemType.Deprecated)
            .Should().OnlyContain(item => item.SpriteHash.Length > 0);
    }

    [Fact]
    public void OtbParser_UsesRmeServerToClientMappingFor1310()
    {
        var repository = FindRepositoryRoot();
        var path = Path.Combine(repository, "rme-data", "1310", "items.otb");

        var file = new OtbParser().Parse(path);

        file.Items.Single(item => item.ServerId == 420).ClientId.Should().Be(423,
            "OTBM przechowuje Server ID, a renderer RME musi pobrać grafikę przez Client ID z items.otb");
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }

        throw new DirectoryNotFoundException("Nie znaleziono katalogu głównego repozytorium.");
    }

    private static void AddLegacyAttribute(List<byte> body, byte type, byte[] value)
    {
        body.Add(type);
        body.AddRange(BitConverter.GetBytes((ushort)value.Length));
        body.AddRange(value);
    }

    private static void WriteEscaped(BinaryWriter writer, IEnumerable<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value is 0xFD or 0xFE or 0xFF) writer.Write((byte)0xFD);
            writer.Write(value);
        }
    }
}
