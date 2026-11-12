namespace WpfVisualTreeMcp.Shared.Ipc;

public class ClrReadRequest : IpcRequest
{
    public override string RequestType => "ReadClr";
    public string ElementHandle { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string[] Fields { get; set; } = new string[0];
    public int Skip { get; set; }
    public int Take { get; set; } = 50;
    public string? Generation { get; set; }
    public bool MembersOnly { get; set; }
}

public class ClrReadResponse : IpcResponse
{
    public string? Json { get; set; }
    public string Generation { get; set; } = string.Empty;
    public int ProcessId { get; set; }
}

public class InspectionIdentityRequest : IpcRequest
{
    public override string RequestType => "InspectionIdentity";
}
