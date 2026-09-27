using FluentAssertions;
using Modules.MapEditor.Services;

namespace Modules.MapEditor.Tests;

public sealed class RmeCreatureCatalogServiceTests
{
    [Fact]
    public void ImportFromOtFiles_LoadsMonstersIndexAndDirectNpcLikeRme()
    {
        var directory = Directory.CreateTempSubdirectory();
        File.WriteAllText(Path.Combine(directory.FullName, "dragon.xml"),
            "<monster name=\"Dragon\"><look type=\"34\"/></monster>");
        File.WriteAllText(Path.Combine(directory.FullName, "monsters.xml"),
            "<monsters><monster name=\"ignored\" file=\"dragon.xml\"/></monsters>");
        File.WriteAllText(Path.Combine(directory.FullName, "Captain Bluebear.xml"),
            "<npc name=\"ignored by RME\"><look type=\"128\" item=\"2160\"/></npc>");

        var result = new RmeCreatureCatalogService().ImportFromOtFiles([
            Path.Combine(directory.FullName, "monsters.xml"),
            Path.Combine(directory.FullName, "Captain Bluebear.xml")
        ]);

        result.Warnings.Should().BeEmpty();
        result.Creatures["Dragon"].LookType.Should().Be(34);
        result.Creatures["Captain Bluebear"].IsNpc.Should().BeTrue();
        result.Creatures["Captain Bluebear"].LookItem.Should().Be(2160);
    }

    [Fact]
    public void ImportFromOtFiles_CancelledToken_StopsBeforeReadingCreatureFiles()
    {
        var directory = Directory.CreateTempSubdirectory();
        var creatureFile = Path.Combine(directory.FullName, "dragon.xml");
        File.WriteAllText(creatureFile,
            "<monster name=\"Dragon\"><look type=\"34\"/></monster>");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => new RmeCreatureCatalogService().ImportFromOtFiles(
            [creatureFile],
            cancellation.Token);

        act.Should().Throw<OperationCanceledException>();
    }
}
