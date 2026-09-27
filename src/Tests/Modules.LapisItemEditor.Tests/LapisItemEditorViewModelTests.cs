using FluentAssertions;
using Modules.LapisItemEditor.Services;
using Modules.LapisItemEditor.ViewModels;
using Narzedzia.Core.Models;

namespace Modules.LapisItemEditor.Tests;

public sealed class LapisItemEditorViewModelTests
{
    [Fact]
    public void OpenFile_ShouldLoadItemsAndApplyFilter()
    {
        var path = Path.GetTempFileName();
        try
        {
            var document = new OtbFile
            {
                Items =
                {
                    new OtbItem { ServerId = 100, ClientId = 200, Name = "crystal coin" },
                    new OtbItem { ServerId = 101, ClientId = 201, Name = "rope" }
                }
            };
            new OtbDocumentService().Save(document, path);

            var vm = CreateViewModel();
            vm.OpenFile(path);
            vm.Filter = "coin";

            vm.Items.Should().ContainSingle();
            vm.Items[0].ServerId.Should().Be(100);
            vm.HeaderSummary.Should().Contain("1/2");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ApplySelectedFields_ShouldUpdateSourceItem()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.AddEmptyItemCommand.Execute(null);
        vm.SelectedItem.Should().NotBeNull();

        vm.SelectedServerId = 500;
        vm.SelectedClientId = 600;
        vm.SelectedName = "edited";
        vm.SelectedTypeIndex = (int)OtbItemType.Container;
        vm.ApplySelectedFieldsCommand.Execute(null);

        vm.SelectedItem!.Source.ServerId.Should().Be(500);
        vm.SelectedItem.Source.ClientId.Should().Be(600);
        vm.SelectedItem.Source.Name.Should().Be("edited");
        vm.SelectedItem.Source.ItemType.Should().Be(OtbItemType.Container);
    }

    [Fact]
    public void DuplicateSelected_ShouldCopySelectedItemAndSelectCopy()
    {
        var vm = CreateViewModel();
        vm.NewDocumentCommand.Execute(null);
        vm.AddEmptyItemCommand.Execute(null);
        vm.SelectedName = "base item";
        vm.SelectedClientId = 321;
        vm.ApplySelectedFieldsCommand.Execute(null);

        vm.DuplicateSelectedCommand.Execute(null);

        vm.Items.Should().HaveCount(2);
        vm.SelectedItem!.ServerId.Should().Be(101);
        vm.SelectedItem.ClientId.Should().Be(321);
        vm.SelectedItem.Name.Should().Be("base item copy");
    }

    private static LapisItemEditorViewModel CreateViewModel() =>
        new(
            new OtbDocumentService(),
            new LapisItemSearchService(),
            new LapisItemMutationService(),
            new OtbCompareService(),
            new LapisAssetService());
}
