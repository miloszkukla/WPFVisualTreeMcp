using System;
using System.Diagnostics;
using System.Text.Json;
using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Inspector;

public partial class InspectorService
{
    private readonly string _inspectionGeneration = Guid.NewGuid().ToString("N");
    private readonly ClrReader _clrReader = new();

    private IpcResponse HandleInspectionIdentity()
    {
        using var process = Process.GetCurrentProcess();
        return new InspectionIdentityResponse { Generation = _inspectionGeneration, ProcessId = process.Id,
            Data = new InspectionIdentity { Generation = _inspectionGeneration, ProcessId = process.Id, ProcessStartUtc = process.StartTime.ToUniversalTime() } };
    }

    private IpcResponse HandleReadClr(JsonElement data)
    {
        var request = IpcSerializer.DeserializeRequestData<ClrReadRequest>(data);
        if (request == null) return new ClrReadResponse { Success = false, Error = "Invalid CLR request." };
        if (request.Generation != null && request.Generation != _inspectionGeneration)
            return new ClrReadResponse { Success = false, Error = "Stale inspection generation. Read identity and reacquire the element.", Generation = _inspectionGeneration };
        var element = _treeWalker.ResolveHandle(request.ElementHandle);
        if (element == null) return new ClrReadResponse { Success = false, Error = StaleHandleError(request.ElementHandle), Generation = _inspectionGeneration };
        try
        {
            var projection = IpcSerializer.Deserialize<ClrReadEnvelope>(_clrReader.Read(element, request.Path, request.Fields, request.Skip, request.Take, request.MembersOnly));
            return new ClrReadResponse { Data = projection,
                Generation = _inspectionGeneration, ProcessId = Process.GetCurrentProcess().Id };
        }
        catch (Exception error) { return new ClrReadResponse { Success = false, Error = error.GetBaseException().Message, Generation = _inspectionGeneration }; }
    }
}
