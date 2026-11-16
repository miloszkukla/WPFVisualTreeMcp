namespace WpfVisualTreeMcp.Shared.Models;

public sealed class InspectionException(InspectionErrorCode code, string message) : InvalidOperationException(message)
{
    public InspectionErrorCode Code { get; } = code;
}
