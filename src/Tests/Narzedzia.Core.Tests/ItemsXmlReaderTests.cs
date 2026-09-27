using FluentAssertions;
using Narzedzia.Core.Parsers;

namespace Narzedzia.Core.Tests;

public sealed class ItemsXmlReaderTests
{
    [Fact]
    public void Read_ParsesSingleIdItemWithAttributes()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "items.xml");
        File.WriteAllText(path, """
        <?xml version="1.0" encoding="UTF-8"?>
        <items>
          <item id="2160" article="a" name="crystal coin" plural="crystal coins">
            <attribute key="weight" value="10"/>
            <attribute key="worth" value="10000"/>
          </item>
        </items>
        """);

        var entries = ItemsXmlReader.Read(path);

        entries.Should().HaveCount(1);
        var entry = entries[0];
        entry.Id.Should().Be(2160);
        entry.Article.Should().Be("a");
        entry.Name.Should().Be("crystal coin");
        entry.Plural.Should().Be("crystal coins");
        entry.GetAttribute("weight").Should().Be("10");
        entry.GetAttribute("WORTH").Should().Be("10000", "klucz case-insensitive");
    }

    [Fact]
    public void Read_ParsesRangeItem()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "items.xml");
        File.WriteAllText(path, """
        <items>
          <item fromid="2000" toid="2003" article="a" name="splash">
            <attribute key="type" value="splash"/>
          </item>
        </items>
        """);

        var entries = ItemsXmlReader.Read(path);

        entries.Should().HaveCount(1);
        entries[0].IsRange.Should().BeTrue();
        entries[0].FromId.Should().Be(2000);
        entries[0].ToId.Should().Be(2003);
        entries[0].Matches(2002).Should().BeTrue();
        entries[0].Matches(1999).Should().BeFalse();
    }

    [Fact]
    public void BuildLookup_ExpandsRangesIntoSingleIds()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "items.xml");
        File.WriteAllText(path, """
        <items>
          <item id="100" name="alpha"/>
          <item fromid="200" toid="202" name="beta"/>
        </items>
        """);

        var entries = ItemsXmlReader.Read(path);
        var lookup = ItemsXmlReader.BuildLookup(entries);

        lookup.Should().ContainKey((ushort)100);
        lookup.Should().ContainKey((ushort)200);
        lookup.Should().ContainKey((ushort)201);
        lookup.Should().ContainKey((ushort)202);
        lookup.Should().NotContainKey((ushort)203);
        lookup[(ushort)100].Name.Should().Be("alpha");
        lookup[(ushort)201].Name.Should().Be("beta");
    }
}
