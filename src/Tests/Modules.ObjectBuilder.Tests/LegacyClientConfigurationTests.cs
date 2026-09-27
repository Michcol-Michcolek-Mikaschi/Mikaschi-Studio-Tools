using FluentAssertions;
using Modules.ObjectBuilder.Services;
using Xunit;

namespace Modules.ObjectBuilder.Tests;

public sealed class LegacyClientConfigurationTests
{
    [Fact]
    public void LoadFromDirectory_ReadsOtclientSpriteOptions()
    {
        var directory = Path.Combine(Path.GetTempPath(), "otfi-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "tibia.otfi"), """
                DatSpr
                  extended: true
                  transparency: true
                  frame-durations: false
                  frame-groups: false
                """);

            var configuration = LegacyClientConfiguration.LoadFromDirectory(directory);

            configuration.ExtendedSprites.Should().BeTrue();
            configuration.Transparency.Should().BeTrue();
            configuration.ImprovedAnimations.Should().BeFalse();
            configuration.FrameGroups.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
