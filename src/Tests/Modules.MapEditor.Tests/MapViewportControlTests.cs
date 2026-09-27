using System.Diagnostics;
using FluentAssertions;
using Modules.MapEditor.Views;

namespace Modules.MapEditor.Tests;

public sealed class MapViewportControlTests
{
    [Fact]
    public void RecordRenderMetrics_DoesNotNotifySubscribersInsideRenderPass()
    {
        var viewport = new MapViewportControl();
        var notifications = 0;
        viewport.RenderMetricsUpdated += (_, _) => notifications++;

        viewport.RecordRenderMetrics(
            Stopwatch.GetTimestamp() - Stopwatch.Frequency);

        notifications.Should().Be(0,
            "zmiana bindingu podczas Render powoduje w Avalonia wyjątek " +
            "'Visual was invalidated during the render pass'");
    }
}
