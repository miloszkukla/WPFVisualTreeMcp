using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Inspector;

public partial class InspectorService
{
    private async Task<IpcResponse> HandleScrollViewerAsync(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<ScrollViewerRequest>(data);
        if (request == null)
            return new ScrollViewerResponse { Success = false, Error = "Invalid ScrollViewer request." };
        if (string.IsNullOrEmpty(request.ElementHandle))
            return ScrollViewerError(request, "ElementHandle required");
        if (!Enum.IsDefined(typeof(ScrollViewerAction), request.Action))
            return ScrollViewerError(request, $"Invalid ScrollViewer action: {request.Action}.");

        var remainingTicks = request.DeadlineUtcTicks - DateTime.UtcNow.Ticks;
        if (remainingTicks <= 0)
            return ScrollViewerError(request, "ScrollViewer request deadline expired; action was not issued.");
        var timeout = TimeSpan.FromTicks(Math.Min(remainingTicks, TimeSpan.FromSeconds(10).Ticks));
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await Task.Run(() => Application.Current.Dispatcher.Invoke(() =>
            {
                cts.Token.ThrowIfCancellationRequested();
                if (DateTime.UtcNow.Ticks >= request.DeadlineUtcTicks)
                    return ScrollViewerError(request, "ScrollViewer request deadline expired; action was not issued.");
                if (request.Generation != null && request.Generation != _inspectionGeneration)
                    return ScrollViewerError(request, "Stale inspection generation. Read identity and reacquire the element.");
                var element = _treeWalker.ResolveHandle(request.ElementHandle);
                if (element == null)
                    return ScrollViewerError(request, StaleHandleError(request.ElementHandle));
                if (element is not ScrollViewer viewer)
                    return ScrollViewerError(request, WrongElementTypeError(request.ElementHandle, element, "ScrollViewer"));

                var outcome = _interactor.ScrollViewer(viewer, request.Action, cts.Token);
                return new ScrollViewerResponse
                {
                    RequestId = request.RequestId,
                    Method = outcome.Method,
                    ElementType = viewer.GetType().FullName,
                    Detail = outcome.Detail
                };
            }, DispatcherPriority.Normal, cts.Token, timeout));
        }
        catch (OperationCanceledException)
        {
            return ScrollViewerError(request, "ScrollViewer request deadline expired; action was not replayed.");
        }
        catch (TimeoutException)
        {
            cts.Cancel();
            return ScrollViewerError(request, "ScrollViewer request timeout: UI thread is busy; action was not replayed.");
        }
        catch (Exception error)
        {
            return ScrollViewerError(request, error.GetBaseException().Message);
        }
    }

    private static ScrollViewerResponse ScrollViewerError(ScrollViewerRequest request, string error) =>
        new() { RequestId = request.RequestId, Success = false, Error = error };
}
