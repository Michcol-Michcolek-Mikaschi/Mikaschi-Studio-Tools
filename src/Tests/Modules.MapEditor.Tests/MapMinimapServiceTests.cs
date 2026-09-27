using System.Collections.Immutable;
using FluentAssertions;
using Modules.MapEditor.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Modules.MapEditor.Tests;

public sealed class MapMinimapServiceTests
{
    [Fact]
    public async Task BuildAsync_ScalesLargeFloorToMaximum256PixelsAndReturnsPng()
    {
        var request = new MapMinimapRequest(
            RequestId: 17,
            Floor: 7,
            Samples:
            [
                new MapMinimapTileSample(1000, 2000, 1),
                new MapMinimapTileSample(3047, 4047, 215)
            ]);

        var result = await new MapMinimapService().BuildAsync(request);

        result.RequestId.Should().Be(17);
        result.Floor.Should().Be(7);
        result.HasTiles.Should().BeTrue();
        result.MinX.Should().Be(1000);
        result.MinY.Should().Be(2000);
        result.Scale.Should().Be(8);
        result.PixelWidth.Should().Be(256);
        result.PixelHeight.Should().Be(256);
        result.PngBytes.Should().StartWith([137, 80, 78, 71, 13, 10, 26, 10]);

        using var image = Image.Load<Rgba32>(result.PngBytes);
        image.Width.Should().Be(result.PixelWidth);
        image.Height.Should().Be(result.PixelHeight);
    }

    [Fact]
    public async Task BuildAsync_SameFloorSnapshotProducesDeterministicStaticRaster()
    {
        var samples = ImmutableArray.CreateBuilder<MapMinimapTileSample>();
        for (ushort y = 200; y < 204; y++)
        for (ushort x = 100; x < 104; x++)
            samples.Add(new MapMinimapTileSample(x, y, 1));
        var firstRequest = new MapMinimapRequest(
            RequestId: 3,
            Floor: 6,
            Samples: samples.MoveToImmutable());
        var secondRequest = firstRequest with
        {
            RequestId = 4
        };

        var service = new MapMinimapService();
        var first = await service.BuildAsync(firstRequest);
        var second = await service.BuildAsync(secondRequest);

        first.PngBytes.Should().Equal(second.PngBytes,
            "ramka viewportu jest lekką nakładką UI i pan nie może przebudowywać PNG");
        using var image = Image.Load<Rgba32>(first.PngBytes);
        var marker = new Rgba32(34, 211, 238, 255);
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
            image[x, y].Should().NotBe(marker);
    }

    [Fact]
    public async Task BuildAsync_EmptySnapshotReturnsMetadataWithoutPng()
    {
        var request = new MapMinimapRequest(
            RequestId: 91,
            Floor: 12,
            Samples: ImmutableArray<MapMinimapTileSample>.Empty);

        var result = await new MapMinimapService().BuildAsync(request);

        result.RequestId.Should().Be(91);
        result.Floor.Should().Be(12);
        result.HasTiles.Should().BeFalse();
        result.TileCount.Should().Be(0);
        result.Scale.Should().Be(1);
        result.PixelWidth.Should().Be(0);
        result.PixelHeight.Should().Be(0);
        result.PngBytes.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildAsync_PreCancelledTokenCancelsWorker()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var request = new MapMinimapRequest(
            RequestId: 1,
            Floor: 7,
            Samples: [new MapMinimapTileSample(100, 100, 1)]);

        var action = () => new MapMinimapService().BuildAsync(request, cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }
}
