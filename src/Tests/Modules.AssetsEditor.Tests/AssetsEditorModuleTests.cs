using FluentAssertions;
using Modules.AssetsEditor;

namespace Modules.AssetsEditor.Tests;

public class AssetsEditorModuleTests
{
    [Fact]
    public void AssetsEditorModule_HasCorrectName()
    {
        var module = new AssetsEditorModule();
        module.Name.Should().Be("Edytor Assetów");
    }

    [Fact]
    public void AssetsEditorModule_HasDescription()
    {
        var module = new AssetsEditorModule();
        module.Description.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AssetsEditorModule_OnLoad_DoesNotThrow()
    {
        var module = new AssetsEditorModule();
        var act = () => module.OnLoad();
        act.Should().NotThrow();
    }
}
