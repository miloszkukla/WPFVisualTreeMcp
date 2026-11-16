using System.Text.Json;
using System.Text.Json.Serialization;
using WpfVisualTreeMcp.Shared.Models;

namespace WpfVisualTreeMcp.Shared.Ipc;

public class ClrReadRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.ReadClr;
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
    public ClrReadEnvelope? Data { get; set; }
    public string Generation { get; set; } = string.Empty;
    public int ProcessId { get; set; }
}

public class InspectionIdentityRequest : IpcRequest
{
    public override IpcRequestType RequestType => IpcRequestType.InspectionIdentity;
}

public sealed class ClrReadEnvelope
{
    [JsonRequired] public ClrReadKind Kind { get; set; }
    public string? Type { get; set; }
    public int? TotalCount { get; set; }
    public int? Skip { get; set; }
    public int? Returned { get; set; }
    public bool? Truncated { get; set; }
    public bool? DeadlineReached { get; set; }
    public List<JsonElement>? Items { get; set; }
    public List<ClrMember>? Members { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Value { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class ClrMember
{
    public string? Name { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))] public System.Reflection.MemberTypes Kind { get; set; }
    public string? Signature { get; set; }
}
public sealed class InspectionIdentityResponse : IpcResponse
{
    public string Generation { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public InspectionIdentity? Data { get; set; }
}
public sealed class InspectionIdentity
{
    public string Generation { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public DateTime ProcessStartUtc { get; set; }
}

/// <summary>A projected member and its arbitrary CLR value.</summary>
public sealed class ClrProjectedField
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public JsonElement Value { get; set; }
    public bool? Missing { get; set; }
    public string? Error { get; set; }
}

/// <summary>A projected member successfully read by the inspector.</summary>
public sealed class ClrProjectedValue
{
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public object? Value { get; set; }
}

public sealed class ClrProjectedMissing
{
    public bool Missing { get; set; }
    public string? Error { get; set; }
}

public sealed class ClrProjectedError
{
    public string? Error { get; set; }
}

public sealed class ClrTruncatedText
{
    public string Text { get; set; } = string.Empty;
    public bool Truncated { get; set; }
}

public sealed class ClrObjectSummary
{
    public string? Type { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public int? Count { get; set; }
}
