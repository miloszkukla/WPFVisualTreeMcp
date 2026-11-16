using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Server.Services;

/// <summary>
/// Implementation of IPC bridge using named pipes.
/// Communicates with the Inspector DLL injected into the target WPF process.
/// </summary>
public partial class NamedPipeBridge : IIpcBridge
{
    private readonly ILogger<NamedPipeBridge> _logger;
    private readonly IProcessManager _processManager;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly TimeSpan _connectionTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(30);

    public NamedPipeBridge(ILogger<NamedPipeBridge> logger, IProcessManager processManager)
    {
        _logger = logger;
        _processManager = processManager;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task<VisualTreeResult> GetVisualTreeAsync(string? rootHandle, int maxDepth)
    {
        var session = EnsureConnected();

        var request = new GetVisualTreeRequest
        {
            RootHandle = rootHandle,
            MaxDepth = maxDepth
        };

        var response = await SendRequestAsync<GetVisualTreeRequest, GetVisualTreeResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to get visual tree");
        }

        return ParseVisualTreeResponse(response);
    }

    public async Task<ElementPropertiesResult> GetElementPropertiesAsync(string elementHandle)
    {
        var session = EnsureConnected();

        var request = new GetElementPropertiesRequest
        {
            ElementHandle = elementHandle
        };

        var response = await SendRequestAsync<GetElementPropertiesRequest, GetElementPropertiesResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to get element properties");
        }

        return ParseElementPropertiesResponse(response, elementHandle);
    }

    public async Task<FindElementsResult> FindElementsAsync(string? rootHandle, string? typeName, string? elementName,
        string? text, Dictionary<string, string>? propertyFilter, bool visibleOnly = false, int maxResults = 50)
    {
        var session = EnsureConnected();

        var request = new FindElementsRequest
        {
            RootHandle = rootHandle,
            TypeName = typeName,
            ElementName = elementName,
            Text = text,
            PropertyFilter = propertyFilter,
            VisibleOnly = visibleOnly,
            MaxResults = maxResults
        };

        var response = await SendRequestAsync<FindElementsRequest, FindElementsResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to find elements");
        }

        return ParseFindElementsResponse(response);
    }

    public async Task<FindElementsResult> FindElementsDeepAsync(string? rootHandle, string? typeName, string? elementName,
        string? text = null, Dictionary<string, string>? propertyFilter = null, bool visibleOnly = false)
    {
        var session = EnsureConnected();

        var request = new FindElementsDeepRequest
        {
            RootHandle = rootHandle,
            TypeName = typeName,
            ElementName = elementName,
            Text = text,
            PropertyFilter = propertyFilter,
            VisibleOnly = visibleOnly
        };

        var response = await SendRequestAsync<FindElementsDeepRequest, FindElementsDeepResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to find elements (deep search)");
        }

        return ParseFindElementsDeepResponse(response);
    }

    public async Task<BindingsResult> GetBindingsAsync(string elementHandle)
    {
        var session = EnsureConnected();

        var request = new GetBindingsRequest
        {
            ElementHandle = elementHandle
        };

        var response = await SendRequestAsync<GetBindingsRequest, GetBindingsResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to get bindings");
        }

        return ParseBindingsResponse(response, elementHandle);
    }

    public async Task<BindingErrorsResult> GetBindingErrorsAsync()
    {
        var session = EnsureConnected();

        var request = new GetBindingErrorsRequest();

        var response = await SendRequestAsync<GetBindingErrorsRequest, GetBindingErrorsResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to get binding errors");
        }

        return ParseBindingErrorsResponse(response);
    }

    public async Task<ResourcesResult> GetResourcesAsync(ResourceScope scope, string? elementHandle)
    {
        var session = EnsureConnected();

        var request = new GetResourcesRequest
        {
            Scope = scope,
            ElementHandle = elementHandle
        };

        var response = await SendRequestAsync<GetResourcesRequest, GetResourcesResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to get resources");
        }

        return ParseResourcesResponse(response);
    }

    public async Task<StylesResult> GetStylesAsync(string elementHandle)
    {
        var session = EnsureConnected();

        var request = new GetStylesRequest
        {
            ElementHandle = elementHandle
        };

        var response = await SendRequestAsync<GetStylesRequest, GetStylesResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to get styles");
        }

        return ParseStylesResponse(response, elementHandle);
    }

    public async Task<string> WatchPropertyAsync(string elementHandle, string propertyName)
    {
        var session = EnsureConnected();

        var request = new WatchPropertyRequest
        {
            ElementHandle = elementHandle,
            PropertyName = propertyName
        };

        var response = await SendRequestAsync<WatchPropertyRequest, WatchPropertyResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to watch property");
        }

        return response.WatchId;
    }

    public async Task HighlightElementAsync(string elementHandle, int durationMs)
    {
        var session = EnsureConnected();

        var request = new HighlightElementRequest
        {
            ElementHandle = elementHandle,
            DurationMs = durationMs
        };

        var response = await SendRequestAsync<HighlightElementRequest, HighlightElementResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to highlight element");
        }
    }

    public async Task<LayoutInfoResult> GetLayoutInfoAsync(string elementHandle)
    {
        var session = EnsureConnected();

        var request = new GetLayoutInfoRequest
        {
            ElementHandle = elementHandle
        };

        var response = await SendRequestAsync<GetLayoutInfoRequest, GetLayoutInfoResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to get layout info");
        }

        return ParseLayoutInfoResponse(response, elementHandle);
    }

    public async Task<ExplainTriggersResult> ExplainTriggersAsync(string elementHandle, string? propertyName)
    {
        var session = EnsureConnected();

        var request = new ExplainTriggersRequest
        {
            ElementHandle = elementHandle,
            PropertyName = propertyName
        };

        var response = await SendRequestAsync<ExplainTriggersRequest, ExplainTriggersResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to explain triggers");
        }

        return new ExplainTriggersResult { Payload = response.Triggers };
    }

    public async Task<EvaluateBindingResult> EvaluateBindingAsync(string elementHandle, string propertyName)
    {
        var session = EnsureConnected();

        var request = new EvaluateBindingRequest
        {
            ElementHandle = elementHandle,
            PropertyName = propertyName
        };

        var response = await SendRequestAsync<EvaluateBindingRequest, EvaluateBindingResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to evaluate binding");
        }

        return new EvaluateBindingResult { Payload = response.Evaluation };
    }

    public async Task<SnapshotResult> SnapshotAsync(string? elementHandle, string? label, int maxDepth)
    {
        var session = EnsureConnected();

        var request = new SnapshotRequest
        {
            ElementHandle = elementHandle,
            Label = label,
            MaxDepth = maxDepth
        };

        var response = await SendRequestAsync<SnapshotRequest, SnapshotResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to capture snapshot");
        }

        return new SnapshotResult
        {
            Label = response.Label ?? "",
            ElementCount = response.ElementCount
        };
    }

    public async Task<DiffResult> DiffAsync(string before, string after)
    {
        var session = EnsureConnected();

        var request = new DiffRequest { Before = before, After = after };

        var response = await SendRequestAsync<DiffRequest, DiffResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to diff snapshots");
        }

        return new DiffResult
        {
            ChangedCount = response.ChangedCount,
            AddedCount = response.AddedCount,
            RemovedCount = response.RemovedCount,
            Payload = response.Diff
        };
    }

    public async Task<SetPropertyResult> SetPropertyAsync(string elementHandle, string propertyName, string value)
    {
        var session = EnsureConnected();

        var request = new SetPropertyRequest
        {
            ElementHandle = elementHandle,
            PropertyName = propertyName,
            Value = value
        };

        var response = await SendRequestAsync<SetPropertyRequest, SetPropertyResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to set property");
        }

        return new SetPropertyResult
        {
            ElementType = response.ElementType,
            AppliedValue = response.AppliedValue,
            ValueType = response.ValueType,
            PreviousSource = response.PreviousSource
        };
    }

    public async Task<RevertPropertyResult> RevertPropertyAsync(bool all, string? elementHandle, string? propertyName)
    {
        var session = EnsureConnected();

        var request = new RevertPropertyRequest
        {
            All = all,
            ElementHandle = elementHandle,
            PropertyName = propertyName
        };

        var response = await SendRequestAsync<RevertPropertyRequest, RevertPropertyResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to revert property");
        }

        return new RevertPropertyResult
        {
            RevertedCount = response.RevertedCount,
            RevertedHandle = response.RevertedHandle,
            RevertedProperty = response.RevertedProperty,
            PendingCount = response.PendingCount
        };
    }

    public async Task<WaitForResult> WaitForElementAsync(string? rootHandle, string? typeName, string? elementName,
        string? text, WaitCondition condition, int timeoutMs, int pollIntervalMs)
    {
        var session = EnsureConnected();

        var request = new WaitForElementRequest
        {
            RootHandle = rootHandle,
            TypeName = typeName,
            ElementName = elementName,
            Text = text,
            Condition = condition,
            TimeoutMs = timeoutMs,
            PollIntervalMs = pollIntervalMs
        };

        var response = await SendRequestAsync<WaitForElementRequest, WaitForElementResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to wait for element");
        }

        return new WaitForResult
        {
            Matched = response.Matched,
            MatchedHandle = response.MatchedHandle,
            ElementType = response.ElementType,
            WaitedMs = response.WaitedMs
        };
    }

    public async Task<ExportResult> ExportTreeAsync(string? elementHandle, TreeExportFormat format)
    {
        var session = EnsureConnected();

        var request = new ExportTreeRequest
        {
            ElementHandle = elementHandle,
            Format = format
        };

        var response = await SendRequestAsync<ExportTreeRequest, ExportTreeResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to export tree");
        }

        return new ExportResult
        {
            Format = format,
            Content = response.Content ?? "",
            ElementCount = response.ElementCount
        };
    }

    public async Task<ScreenshotResult> CaptureScreenshotAsync(string? elementHandle, int maxWidth, int maxHeight, ScreenshotMode mode = ScreenshotMode.Render, bool fullContent = false)
    {
        var session = EnsureConnected();

        var request = new CaptureScreenshotRequest
        {
            ElementHandle = elementHandle,
            MaxWidth = maxWidth,
            MaxHeight = maxHeight,
            Mode = mode,
            FullContent = fullContent
        };

        var response = await SendRequestAsync<CaptureScreenshotRequest, CaptureScreenshotResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to capture screenshot");
        }

        return new ScreenshotResult
        {
            ImageBase64 = response.ImageBase64 ?? string.Empty,
            MimeType = "image/png",
            Width = response.Width,
            Height = response.Height,
            ElementType = response.ElementType
        };
    }

    public async Task<DataContextResult> GetDataContextAsync(string elementHandle)
    {
        var session = EnsureConnected();

        var request = new GetDataContextRequest
        {
            ElementHandle = elementHandle
        };

        var response = await SendRequestAsync<GetDataContextRequest, GetDataContextResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to get DataContext");
        }

        return new DataContextResult
        {
            Element = new ElementInfo { Handle = elementHandle },
            Payload = response.DataContext
        };
    }

    public async Task ClearBindingErrorsAsync()
    {
        var session = EnsureConnected();

        var request = new ClearBindingErrorsRequest();

        var response = await SendRequestAsync<ClearBindingErrorsRequest, ClearBindingErrorsResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to clear binding errors");
        }
    }

    public async Task<ClickResult> ClickElementAsync(string elementHandle, bool physical, ClickType? clickType = null)
    {
        var session = EnsureConnected();

        var request = new ClickElementRequest
        {
            ElementHandle = elementHandle,
            Physical = physical,
            ClickType = clickType
        };

        var response = await SendRequestAsync<ClickElementRequest, ClickElementResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to click element");
        }

        return new ClickResult
        {
            Method = response.Method ?? InteractionMethod.Unknown,
            ElementType = response.ElementType,
            Detail = response.Detail
        };
    }

    public async Task<SelectItemResult> SelectItemAsync(string elementHandle, string? itemText, int? index)
    {
        var session = EnsureConnected();

        var request = new SelectItemRequest
        {
            ElementHandle = elementHandle,
            ItemText = itemText,
            Index = index
        };

        var response = await SendRequestAsync<SelectItemRequest, SelectItemResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to select item");
        }

        return new SelectItemResult
        {
            Method = response.Method ?? InteractionMethod.Unknown,
            ElementType = response.ElementType,
            Detail = response.Detail
        };
    }

    public async Task<SetTextResult> SetTextAsync(string elementHandle, string text, bool physical)
    {
        var session = EnsureConnected();

        var request = new SetTextRequest
        {
            ElementHandle = elementHandle,
            Text = text ?? string.Empty,
            Physical = physical
        };

        var response = await SendRequestAsync<SetTextRequest, SetTextResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to set element text");
        }

        return new SetTextResult
        {
            Method = response.Method ?? InteractionMethod.Unknown,
            ElementType = response.ElementType,
            Detail = response.Detail
        };
    }

    public async Task<SendKeysResult> SendKeysAsync(string? elementHandle, string keys)
    {
        var session = EnsureConnected();

        var request = new SendKeysRequest
        {
            ElementHandle = elementHandle,
            Keys = keys
        };

        var response = await SendRequestAsync<SendKeysRequest, SendKeysResponse>(
            session.ProcessId, request);

        if (!response.Success)
        {
            throw ResponseError(response, "Failed to send keys");
        }

        return new SendKeysResult
        {
            Method = response.Method ?? InteractionMethod.Unknown,
            ElementType = response.ElementType,
            Detail = response.Detail
        };
    }

    private async Task<TResponse> SendRequestAsync<TRequest, TResponse>(int processId, TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : IpcRequest
        where TResponse : IpcResponse, new()
    {
        var pipeName = $"wpf_inspector_{processId}";

        // Check if the target process still exists
        try
        {
            var process = System.Diagnostics.Process.GetProcessById(processId);
            if (process.HasExited)
            {
                var errorMsg = $"Process {processId} has exited. Use wpf_list_processes() to see available WPF applications, then wpf_attach(process_id=<new_pid>) to connect.";
                _logger.LogWarning(errorMsg);
                return new TResponse { Success = false, Error = errorMsg };
            }
        }
        catch (ArgumentException)
        {
            // Process doesn't exist
            var errorMsg = $"Process {processId} no longer exists. The application may have been closed or restarted. Use wpf_list_processes() to see available WPF applications, then wpf_attach(process_id=<new_pid>) to connect to the current instance.";
            _logger.LogWarning(errorMsg);
            return new TResponse { Success = false, Error = errorMsg };
        }

        _logger.LogDebug("Sending {RequestType} request to pipe {PipeName}",
            request.RequestType, pipeName);

        try
        {
            using var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_connectionTimeout);
            await pipeClient.ConnectAsync(cts.Token);

            using var reader = new StreamReader(pipeClient, Encoding.UTF8, leaveOpen: true);
            await using var writer = new StreamWriter(pipeClient, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

            var requestJson = IpcSerializer.SerializeRequest(request);
            await writer.WriteLineAsync(requestJson.AsMemory(), cancellationToken);

            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readCts.CancelAfter(_requestTimeout);
            var responseJson = await reader.ReadLineAsync(readCts.Token);

            if (string.IsNullOrEmpty(responseJson))
            {
                return new TResponse { Success = false, Error = "Empty response from inspector", ErrorCode = InspectionErrorCode.EmptyResponse };
            }

            var response = IpcSerializer.DeserializeResponse<TResponse>(responseJson);
            return response ?? new TResponse { Success = false, Error = "Failed to parse response" };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TimeoutException)
        {
            var errorMsg = $"Connection to process {processId} timed out. The Inspector may not be loaded. Try restarting the application or use wpf_list_processes() and wpf_attach() to reconnect.";
            _logger.LogWarning(errorMsg);
            return new TResponse { Success = false, Error = errorMsg };
        }
        catch (IOException ex)
        {
            var errorMsg = $"Cannot connect to process {processId}: {ex.Message}. The named pipe may not exist. Use wpf_list_processes() to see available WPF applications, then wpf_attach(process_id=<new_pid>) to connect.";
            _logger.LogWarning(ex, errorMsg);
            return new TResponse { Success = false, Error = errorMsg };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending request to inspector");
            return new TResponse { Success = false, Error = ex.Message };
        }
    }

    private static InvalidOperationException ResponseError(IpcResponse response, string fallback) => response.ErrorCode is { } code
        ? new InspectionException(code, response.Error ?? fallback) : new InvalidOperationException(response.Error ?? fallback);

    private InspectionSession EnsureConnected()
    {
        var session = _processManager.CurrentSession;
        if (session == null)
        {
            throw new InvalidOperationException("Not attached to any WPF process. Use wpf_attach first.");
        }
        return session;
    }

    #region Response Parsers

    private VisualTreeResult ParseVisualTreeResponse(GetVisualTreeResponse response)
    {
        return new VisualTreeResult
        {
            Root = response.Tree?.Root ?? new VisualTreeNode { Handle = "empty", TypeName = "Unknown" },
            TotalElements = response.Tree == null ? 0 : response.TotalElements,
            MaxDepthReached = response.Tree != null && response.MaxDepthReached
        };
    }

    private ElementPropertiesResult ParseElementPropertiesResponse(GetElementPropertiesResponse response, string handle)
    {
        return new ElementPropertiesResult
        {
            Element = new ElementInfo { Handle = handle, TypeName = response.Properties?.Element?.TypeName ?? "Unknown" },
            Properties = response.Properties?.Properties.Select(property => new PropertyInfo
            {
                Name = property.Name ?? "",
                TypeName = property.TypeName ?? "",
                Value = property.Value is { } value ? GetJsonValueAsString(value) : null,
                Source = property.Source ?? PropertyValueSource.Unknown
            }).ToList() ?? new List<PropertyInfo>()
        };
    }

    private static string? GetJsonValueAsString(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => value.GetString(),
            _ => value.GetRawText()
        };
    }

    private FindElementsResult ParseFindElementsResponse(FindElementsResponse response) => new()
    {
        Elements = response.Elements?.Elements ?? new List<FoundElement>(), Count = response.Count
    };

    private FindElementsResult ParseFindElementsDeepResponse(FindElementsDeepResponse response) => new()
    {
        Elements = response.Elements?.Elements ?? new List<FoundElement>(), Count = response.Count
    };

    private BindingsResult ParseBindingsResponse(GetBindingsResponse response, string handle)
    {
        return new BindingsResult
        {
            Element = new ElementInfo { Handle = handle, TypeName = "Unknown" },
            Bindings = response.Bindings?.Bindings.Select(binding => new BindingInfo
            {
                Property = binding.Property ?? "",
                Path = binding.Path ?? "",
                Kind = binding.Type,
                SourceKind = binding.SourceKind,
                RelativeSourceMode = binding.RelativeSourceMode,
                SourceType = binding.SourceType,
                ElementName = binding.ElementName,
                AncestorType = binding.AncestorType,
                AncestorLevel = binding.AncestorLevel,
                Mode = binding.Mode,
                UpdateTrigger = binding.UpdateTrigger,
                Status = binding.Status,
                CurrentValue = binding.CurrentValue is { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } value ? value.ToString() : null
            }).ToList() ?? new List<BindingInfo>()
        };
    }

    private BindingErrorsResult ParseBindingErrorsResponse(GetBindingErrorsResponse response) => new()
    {
        Errors = response.Errors?.Errors ?? new List<BindingError>(), Count = response.Count
    };

    private ResourcesResult ParseResourcesResponse(GetResourcesResponse response) => new()
    {
        Resources = response.Resources?.Select(resource => new ResourceInfo
        {
            Key = resource.Key ?? "", TypeName = resource.TypeName ?? "",
            Value = resource.Value is { } value ? GetJsonValueAsString(value) : null, Source = resource.Source ?? ""
        }).ToList() ?? new List<ResourceInfo>()
    };

    private StylesResult ParseStylesResponse(GetStylesResponse response, string handle)
    {
        var style = response.Styles?.Style;
        return new StylesResult
        {
            Element = new ElementInfo { Handle = handle, TypeName = "Unknown" },
            Style = new StyleInfo
            {
                Key = style?.Key ?? "", TargetType = style?.TargetType ?? "",
                Setters = style?.Setters.Select(setter => new SetterInfo
                {
                    Property = setter.Property ?? "", Value = setter.Value is { } value ? GetJsonValueAsString(value) : null
                }).ToList() ?? new List<SetterInfo>()
            }
        };
    }

    private LayoutInfoResult ParseLayoutInfoResponse(GetLayoutInfoResponse response, string handle)
    {
        var layout = response.Layout?.Layout;
        return new LayoutInfoResult
        {
            Element = new ElementInfo { Handle = handle, TypeName = "Unknown" },
            Layout = layout == null ? new LayoutInfo() : new LayoutInfo
            {
                ActualWidth = layout.ActualWidth ?? 0,
                ActualHeight = layout.ActualHeight ?? 0,
                HorizontalAlignment = layout.HorizontalAlignment,
                VerticalAlignment = layout.VerticalAlignment,
                Visibility = layout.Visibility,
                DesiredSize = new SizeInfo { Width = layout.DesiredSize?.Width ?? 0, Height = layout.DesiredSize?.Height ?? 0 },
                RenderSize = new SizeInfo { Width = layout.RenderSize?.Width ?? 0, Height = layout.RenderSize?.Height ?? 0 },
                Margin = Thickness(layout.Margin),
                Padding = layout.Padding == null ? null : Thickness(layout.Padding)
            }
        };
    }

    private static ThicknessInfo Thickness(InspectedThickness? value) => new()
    {
        Left = value?.Left ?? 0, Top = value?.Top ?? 0, Right = value?.Right ?? 0, Bottom = value?.Bottom ?? 0
    };

    #endregion
}
