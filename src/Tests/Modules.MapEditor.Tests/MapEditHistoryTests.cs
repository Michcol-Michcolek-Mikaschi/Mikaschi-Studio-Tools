using FluentAssertions;
using Modules.MapEditor.Services;
using Narzedzia.Core.Models;

namespace Modules.MapEditor.Tests;

public sealed class MapEditHistoryTests
{
    [Fact]
    public void SpawnEdit_UndoRedoKeepsIndependentCreatureSnapshots()
    {
        var history = new MapEditHistory();
        var before = new[] { SpawnWithCreature("Rat") };
        var after = new[] { SpawnWithCreature("Dragon") };

        history.PushSpawns("Creature Dragon", before, after);
        after[0].Creatures[0].Name = "Mutated outside history";

        history.Undo()!.SpawnChange!.Before[0].Creatures[0].Name.Should().Be("Rat");
        history.Redo()!.SpawnChange!.After[0].Creatures[0].Name.Should().Be("Dragon");
    }

    private static OtbmSpawn SpawnWithCreature(string name)
    {
        var spawn = new OtbmSpawn { CenterX = 100, CenterY = 100, CenterZ = 7, Radius = 1 };
        spawn.Creatures.Add(new OtbmCreature
        {
            Name = name, X = 100, Y = 100, Z = 7, SpawnTime = 60
        });
        return spawn;
    }
}
