using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Server.Services;

public partial class NamedPipeBridge
{
    public async Task<ScrollViewerResult> ScrollItemIntoViewAsync(string listHandle, int index,
        string? generation = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = EnsureConnected();
        var request = new ScrollItemIntoViewRequest
        {
            ListHandle = listHandle,
            Index = index,
            Generation = generation,
            DeadlineUtcTicks = DateTime.UtcNow.AddSeconds(10).Ticks
        };
        var response = await SendRequestAsync<ScrollItemIntoViewRequest, ScrollViewerResponse>(
            session.ProcessId, request, cancellationToken);
        if (!response.Success)
            throw new InvalidOperationException(response.Error ?? "Failed to scroll item into view");

        return new ScrollViewerResult
        {
            Method = response.Method ?? "Unknown",
            ElementType = response.ElementType,
            Detail = response.Detail
        };
    }

    public async Task<ScrollViewerResult> ScrollViewerAsync(string elementHandle, ScrollViewerAction action,
        string? generation = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var session = EnsureConnected();
        var request = new ScrollViewerRequest
        {
            ElementHandle = elementHandle,
            Action = action,
            Generation = generation,
            DeadlineUtcTicks = DateTime.UtcNow.AddSeconds(10).Ticks
        };
        var response = await SendRequestAsync<ScrollViewerRequest, ScrollViewerResponse>(
            session.ProcessId, request, cancellationToken);
        if (!response.Success)
            throw new InvalidOperationException(response.Error ?? "Failed to scroll viewer");

        return new ScrollViewerResult
        {
            Method = response.Method ?? "Unknown",
            ElementType = response.ElementType,
            Detail = response.Detail
        };
    }
}
