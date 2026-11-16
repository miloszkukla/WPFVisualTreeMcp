using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class ScrollViewerBridgeTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(39)]
    public async Task Item_request_preserves_exact_list_index_and_maps_the_outcome(int index)
    {
        using var server = CreateServer();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var before = DateTime.UtcNow;
        var received = Task.Run(() => ReplyItemOnceAsync(server, request => new ScrollViewerResponse
        {
            RequestId = request.RequestId,
            Method = "ListBox.ScrollIntoView",
            ElementType = "System.Windows.Controls.ListBox",
            Detail = $"Issued once for item {request.Index}"
        }, timeout.Token));

        var result = await Task.Run(() => CreateBridge().ScrollItemIntoViewAsync(
            "elem_list_42", index, "generation-1", timeout.Token)).WaitAsync(TimeSpan.FromSeconds(10));
        var request = await received;

        request.ListHandle.Should().Be("elem_list_42");
        request.Index.Should().Be(index);
        request.Generation.Should().Be("generation-1");
        request.DeadlineUtcTicks.Should().BeInRange(before.Ticks, before.AddSeconds(11).Ticks);
        result.Method.Should().Be("ListBox.ScrollIntoView");
        result.ElementType.Should().Be("System.Windows.Controls.ListBox");
        result.Detail.Should().Be($"Issued once for item {index}");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(40)]
    public async Task Item_request_preserves_inspector_bounds_rejection(int index)
    {
        using var server = CreateServer();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = Task.Run(() => ReplyItemOnceAsync(server, request => new ScrollViewerResponse
        {
            RequestId = request.RequestId,
            Success = false,
            Error = $"ListBox item index {request.Index} is out of range (count 40)."
        }, timeout.Token));

        var action = () => Task.Run(() => CreateBridge().ScrollItemIntoViewAsync(
            "elem_list_42", index, cancellationToken: timeout.Token)).WaitAsync(TimeSpan.FromSeconds(10));

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage($"ListBox item index {index} is out of range*");
        (await received).Index.Should().Be(index);
    }

    [Fact]
    public async Task Sends_one_typed_request_and_maps_the_outcome()
    {
        using var server = CreateServer();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var before = DateTime.UtcNow;
        var received = Task.Run(() => ReplyOnceAsync(server, request => new ScrollViewerResponse
        {
            RequestId = request.RequestId,
            Method = "ScrollViewer.PageDown",
            ElementType = "System.Windows.Controls.ScrollViewer",
            Detail = "Issued once"
        }, timeout.Token));

        var result = await Task.Run(() => CreateBridge().ScrollViewerAsync(
            "elem_42", ScrollViewerAction.PageDown, "generation-1", timeout.Token)).WaitAsync(TimeSpan.FromSeconds(10));
        var request = await received;

        request.ElementHandle.Should().Be("elem_42");
        request.Action.Should().Be(ScrollViewerAction.PageDown);
        request.Generation.Should().Be("generation-1");
        request.DeadlineUtcTicks.Should().BeInRange(before.Ticks, before.AddSeconds(11).Ticks);
        result.Method.Should().Be("ScrollViewer.PageDown");
        result.ElementType.Should().Be("System.Windows.Controls.ScrollViewer");
        result.Detail.Should().Be("Issued once");
    }

    [Fact]
    public async Task Inspector_rejection_is_preserved_without_replaying_the_action()
    {
        using var server = CreateServer();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = Task.Run(() => ReplyOnceAsync(server, request => new ScrollViewerResponse
        {
            RequestId = request.RequestId,
            Success = false,
            Error = "Element 'elem_42' is a Button, which is not a ScrollViewer."
        }, timeout.Token));

        var action = () => Task.Run(() => CreateBridge().ScrollViewerAsync(
            "elem_42", ScrollViewerAction.PageDown, cancellationToken: timeout.Token)).WaitAsync(TimeSpan.FromSeconds(10));

        await action.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Button*not a ScrollViewer*");
        (await received).Action.Should().Be(ScrollViewerAction.PageDown);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Caller_can_cancel_a_pending_response(bool itemRequest)
    {
        using var server = CreateServer();
        using var cancellation = new CancellationTokenSource();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var connected = server.WaitForConnectionAsync(timeout.Token);
        var received = Task.Run(async () =>
        {
            await connected;
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            return await reader.ReadLineAsync(timeout.Token);
        });
        var pending = Task.Run(() => itemRequest
            ? CreateBridge().ScrollItemIntoViewAsync("elem_42", 0, cancellationToken: cancellation.Token)
            : CreateBridge().ScrollViewerAsync("elem_42", ScrollViewerAction.Home, cancellationToken: cancellation.Token));
        (await received.WaitAsync(TimeSpan.FromSeconds(10))).Should().Contain(itemRequest ? "ScrollItemIntoView" : "ScrollViewer");

        cancellation.Cancel();

        var action = () => pending.WaitAsync(TimeSpan.FromSeconds(10));
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Precancelled_request_does_not_connect_or_issue_an_action(bool itemRequest)
    {
        using var server = CreateServer();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => itemRequest
            ? CreateBridge().ScrollItemIntoViewAsync("elem_42", 0, cancellationToken: cancellation.Token)
            : CreateBridge().ScrollViewerAsync("elem_42", ScrollViewerAction.Home, cancellationToken: cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        server.IsConnected.Should().BeFalse();
    }

    private static NamedPipeServerStream CreateServer() => new(
        $"wpf_inspector_{Environment.ProcessId}", PipeDirection.InOut, 1,
        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private static NamedPipeBridge CreateBridge()
    {
        var manager = new Mock<IProcessManager>();
        manager.SetupGet(value => value.CurrentSession).Returns(new InspectionSession
        {
            ProcessId = Process.GetCurrentProcess().Id
        });
        return new NamedPipeBridge(NullLogger<NamedPipeBridge>.Instance, manager.Object);
    }

    private static async Task<ScrollItemIntoViewRequest> ReplyItemOnceAsync(NamedPipeServerStream server,
        Func<ScrollItemIntoViewRequest, ScrollViewerResponse> response, CancellationToken cancellationToken)
    {
        await server.WaitForConnectionAsync(cancellationToken);
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        var json = await reader.ReadLineAsync(cancellationToken);
        var envelope = IpcSerializer.DeserializeRequest(json!);
        envelope.Should().NotBeNull();
        envelope!.Value.type.Should().Be(IpcRequestType.ScrollItemIntoView);
        var request = IpcSerializer.DeserializeRequestData<ScrollItemIntoViewRequest>(envelope.Value.data)!;
        var bytes = Encoding.UTF8.GetBytes(IpcSerializer.SerializeResponse(response(request)) + "\n");
        await server.WriteAsync(bytes, cancellationToken);
        await server.FlushAsync(cancellationToken);
        return request;
    }

    private static async Task<ScrollViewerRequest> ReplyOnceAsync(NamedPipeServerStream server,
        Func<ScrollViewerRequest, ScrollViewerResponse> response, CancellationToken cancellationToken)
    {
        await server.WaitForConnectionAsync(cancellationToken);
        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        var json = await reader.ReadLineAsync(cancellationToken);
        var envelope = IpcSerializer.DeserializeRequest(json!);
        envelope.Should().NotBeNull();
        envelope!.Value.type.Should().Be(IpcRequestType.ScrollViewer);
        var request = IpcSerializer.DeserializeRequestData<ScrollViewerRequest>(envelope.Value.data)!;
        var bytes = Encoding.UTF8.GetBytes(IpcSerializer.SerializeResponse(response(request)) + "\n");
        await server.WriteAsync(bytes, cancellationToken);
        await server.FlushAsync(cancellationToken);
        return request;
    }
}
