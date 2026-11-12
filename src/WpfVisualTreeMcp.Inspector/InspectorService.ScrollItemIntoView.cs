using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Inspector;

public partial class InspectorService
{
    private async Task<IpcResponse> HandleScrollItemIntoViewAsync(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<ScrollItemIntoViewRequest>(data);
        if (request == null)
            return new ScrollViewerResponse { Success = false, Error = "Invalid ScrollItemIntoView request." };
        if (string.IsNullOrEmpty(request.ListHandle))
            return ScrollItemIntoViewError(request, "ListHandle required");

        var remainingTicks = request.DeadlineUtcTicks - DateTime.UtcNow.Ticks;
        if (remainingTicks <= 0)
            return ScrollItemIntoViewError(request, "ScrollItemIntoView request deadline expired; action was not issued.");
        var timeout = TimeSpan.FromTicks(Math.Min(remainingTicks, TimeSpan.FromSeconds(10).Ticks));
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await Task.Run(() => Application.Current.Dispatcher.Invoke(() =>
            {
                cts.Token.ThrowIfCancellationRequested();
                if (DateTime.UtcNow.Ticks >= request.DeadlineUtcTicks)
                    return ScrollItemIntoViewError(request, "ScrollItemIntoView request deadline expired; action was not issued.");
                if (request.Generation != null && request.Generation != _inspectionGeneration)
                    return ScrollItemIntoViewError(request, "Stale inspection generation. Read identity and reacquire the element.");
                var element = _treeWalker.ResolveHandle(request.ListHandle);
                if (element == null)
                    return ScrollItemIntoViewError(request, StaleHandleError(request.ListHandle));
                if (element is not ListBox list)
                    return ScrollItemIntoViewError(request, WrongElementTypeError(request.ListHandle, element, "ListBox"));
                if (request.Index < 0 || request.Index >= list.Items.Count)
                    return ScrollItemIntoViewError(request, $"ListBox item index {request.Index} is out of range (count {list.Items.Count}).");

                var outcome = _interactor.ScrollItemIntoView(list, request.Index, cts.Token);
                return new ScrollViewerResponse
                {
                    RequestId = request.RequestId,
                    Method = outcome.Method,
                    ElementType = list.GetType().FullName,
                    Detail = outcome.Detail
                };
            }, DispatcherPriority.Normal, cts.Token, timeout));
        }
        catch (OperationCanceledException)
        {
            return ScrollItemIntoViewError(request, "ScrollItemIntoView request deadline expired; action was not replayed.");
        }
        catch (TimeoutException)
        {
            cts.Cancel();
            return ScrollItemIntoViewError(request, "ScrollItemIntoView request timeout: UI thread is busy; action was not replayed.");
        }
        catch (Exception error)
        {
            return ScrollItemIntoViewError(request, error.GetBaseException().Message);
        }
    }

    private static ScrollViewerResponse ScrollItemIntoViewError(ScrollItemIntoViewRequest request, string error) =>
        new() { RequestId = request.RequestId, Success = false, Error = error };
}
