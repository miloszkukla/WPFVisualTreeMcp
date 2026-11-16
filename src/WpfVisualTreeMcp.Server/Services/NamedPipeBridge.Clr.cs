using WpfVisualTreeMcp.Shared.Ipc;

namespace WpfVisualTreeMcp.Server.Services;

public partial class NamedPipeBridge
{
    public Task<ClrReadResponse> GetInspectionIdentityAsync() => SendRequestAsync<InspectionIdentityRequest, ClrReadResponse>(
        EnsureConnected().ProcessId, new InspectionIdentityRequest());

    public async Task<ClrReadResponse> ReadClrAsync(string elementHandle, string path, string[] fields, int skip, int take, string? generation, bool membersOnly = false)
    {
        var response = await SendRequestAsync<ClrReadRequest, ClrReadResponse>(EnsureConnected().ProcessId,
            new ClrReadRequest { ElementHandle = elementHandle, Path = path, Fields = fields, Skip = skip, Take = take, Generation = generation, MembersOnly = membersOnly });
        if (!response.Success) throw ResponseError(response, "Inspector request failed");
        return response;
    }
}
