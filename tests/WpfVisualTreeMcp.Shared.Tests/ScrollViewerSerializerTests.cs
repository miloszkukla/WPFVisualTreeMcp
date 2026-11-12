using FluentAssertions;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class ScrollViewerSerializerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(39)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Item_request_round_trip_preserves_exact_list_index_generation_and_deadline(int index)
    {
        var request = new ScrollItemIntoViewRequest
        {
            ListHandle = "elem_list_42",
            Index = index,
            Generation = "current-inspector",
            DeadlineUtcTicks = 638970000000000000
        };

        var envelope = IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(request));

        envelope.Should().NotBeNull();
        envelope!.Value.type.Should().Be("ScrollItemIntoView");
        var restored = IpcSerializer.DeserializeRequestData<ScrollItemIntoViewRequest>(envelope.Value.data);
        restored.Should().BeEquivalentTo(request);
    }

    [Fact]
    public void Missing_item_index_and_deadline_do_not_default_to_an_executable_request()
    {
        var envelope = IpcSerializer.DeserializeRequest("{\"type\":\"ScrollItemIntoView\",\"data\":{\"listHandle\":\"elem_1\"}}");
        var request = IpcSerializer.DeserializeRequestData<ScrollItemIntoViewRequest>(envelope!.Value.data)!;

        request.Index.Should().Be(-1);
        request.DeadlineUtcTicks.Should().Be(0);
    }

    [Theory]
    [InlineData(ScrollViewerAction.Home)]
    [InlineData(ScrollViewerAction.PageDown)]
    [InlineData(ScrollViewerAction.PageUp)]
    [InlineData(ScrollViewerAction.End)]
    [InlineData((ScrollViewerAction)999)]
    public void Request_round_trip_preserves_target_action_generation_and_deadline(ScrollViewerAction action)
    {
        var request = new ScrollViewerRequest
        {
            ElementHandle = "elem_00000042",
            Action = action,
            Generation = "current-inspector",
            DeadlineUtcTicks = 638970000000000000
        };

        var envelope = IpcSerializer.DeserializeRequest(IpcSerializer.SerializeRequest(request));

        envelope.Should().NotBeNull();
        envelope!.Value.type.Should().Be("ScrollViewer");
        var restored = IpcSerializer.DeserializeRequestData<ScrollViewerRequest>(envelope.Value.data);
        restored.Should().BeEquivalentTo(request);
    }

    [Fact]
    public void Missing_action_and_deadline_do_not_default_to_an_executable_request()
    {
        var envelope = IpcSerializer.DeserializeRequest("{\"type\":\"ScrollViewer\",\"data\":{\"elementHandle\":\"elem_1\"}}");
        var request = IpcSerializer.DeserializeRequestData<ScrollViewerRequest>(envelope!.Value.data)!;

        Enum.IsDefined(typeof(ScrollViewerAction), request.Action).Should().BeFalse();
        request.DeadlineUtcTicks.Should().Be(0);
    }

    [Fact]
    public void Response_round_trip_preserves_typed_outcome_and_failure()
    {
        var response = new ScrollViewerResponse
        {
            RequestId = "request-1",
            Success = false,
            Error = "Stale inspection generation.",
            Method = "ScrollViewer.PageDown",
            ElementType = "System.Windows.Controls.ScrollViewer",
            Detail = "Issued once"
        };

        var restored = IpcSerializer.DeserializeResponse<ScrollViewerResponse>(IpcSerializer.SerializeResponse(response));

        restored.Should().BeEquivalentTo(response);
    }
}
