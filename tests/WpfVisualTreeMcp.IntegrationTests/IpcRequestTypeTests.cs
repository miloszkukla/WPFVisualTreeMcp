using System.IO.Pipes;
using System.Text;
using WpfVisualTreeMcp.Inspector;
using WpfVisualTreeMcp.Shared.Ipc;
using Xunit;

namespace WpfVisualTreeMcp.IntegrationTests;

public class IpcRequestTypeTests
{
    [Fact]
    public async Task TypedRequestCrossesTheNamedPipeAndReachesItsHandler()
    {
        IpcRequestType? observed = null;
        using var server = new IpcServer(Random.Shared.Next(1, int.MaxValue), (type, data) =>
        {
            observed = type;
            var request = IpcSerializer.DeserializeRequestData<ClrReadRequest>(data)!;
            return Task.FromResult<IpcResponse>(new ClrReadResponse { Data = new ClrReadEnvelope { Kind = WpfVisualTreeMcp.Shared.Models.ClrReadKind.Value, Value = System.Text.Json.JsonSerializer.SerializeToElement(request.ElementHandle) } });
        });
        server.Start();
        using var client = new NamedPipeClientStream(".", server.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var reader = new StreamReader(client, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

        await writer.WriteLineAsync(IpcSerializer.SerializeRequest(new ClrReadRequest { ElementHandle = "element" }));
        var result = IpcSerializer.DeserializeResponse<ClrReadResponse>((await reader.ReadLineAsync())!)!;

        Assert.Equal(IpcRequestType.ReadClr, observed);
        Assert.True(result.Success, result.Error);
        Assert.Equal("element", result.Data!.Value.GetString());
    }

    [Fact]
    public async Task UnknownRequestTypeIsRejectedBeforeReachingTheHandler()
    {
        var invoked = false;
        using var server = new IpcServer(Random.Shared.Next(1, int.MaxValue), (type, data) =>
        {
            invoked = true;
            return Task.FromResult<IpcResponse>(new GetVisualTreeResponse());
        });
        server.Start();
        using var client = new NamedPipeClientStream(".", server.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5000);
        using var reader = new StreamReader(client, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, true) { AutoFlush = true };

        await writer.WriteLineAsync("{\"type\":\"NotARequest\",\"data\":{}}");
        var result = IpcSerializer.DeserializeResponse<GetVisualTreeResponse>((await reader.ReadLineAsync())!)!;

        Assert.False(invoked);
        Assert.False(result.Success);
        Assert.Equal("Invalid request format", result.Error);
    }
}
