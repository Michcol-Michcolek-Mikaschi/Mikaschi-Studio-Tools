using FluentAssertions;
using Narzedzia.Core.Assets;

namespace Narzedzia.Core.Tests;

public sealed class CatalogReaderWriterTests
{
    [Fact]
    public void CatalogRoundTrip_PreservesAreaVersionAndUnknownFields()
    {
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "catalog-content.json");
        File.WriteAllText(path, """
        [
          {
            "type": "sprite",
            "file": "sprites-1.bmp.lzma",
            "spritetype": 3,
            "firstspriteid": 1000,
            "lastspriteid": 1035,
            "area": 0,
            "version": 13,
            "custom": "keep"
          }
        ]
        """);

        var entries = CatalogReader.Read(path);
        CatalogWriter.Write(path, entries);
        var text = File.ReadAllText(path);

        text.Should().Contain("\"area\"");
        text.Should().Contain("\"version\"");
        text.Should().Contain("\"custom\"");
    }
}
