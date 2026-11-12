using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class BindingResponseTests
{
    [Theory]
    [InlineData("true", "True")]
    [InlineData("false", "False")]
    [InlineData("0.75", "0.75")]
    [InlineData("\"caption\"", "caption")]
    [InlineData("null", null)]
    public void TypedCurrentValuesPreserveBindingIdentityAndFollowingBindings(string jsonValue, string? expected)
    {
        var bridge = new NamedPipeBridge(NullLogger<NamedPipeBridge>.Instance, Mock.Of<IProcessManager>());
        var response = new GetBindingsResponse
        {
            BindingsJson = "{\"bindings\":[{\"property\":\"IsChecked\",\"path\":\"ShowDx\",\"currentValue\":" + jsonValue
                + "},{\"property\":\"IsEnabled\",\"path\":\"CanEdit\",\"currentValue\":true}]}"
        };
        var parse = typeof(NamedPipeBridge).GetMethod("ParseBindingsResponse", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var result = (BindingsResult)parse.Invoke(bridge, new object[] { response, "checkbox" })!;

        Assert.Equal(2, result.Bindings.Count);
        Assert.Equal("IsChecked", result.Bindings[0].Property);
        Assert.Equal("ShowDx", result.Bindings[0].Path);
        Assert.Equal(expected, result.Bindings[0].CurrentValue);
        Assert.Equal("CanEdit", result.Bindings[1].Path);
        Assert.Equal("True", result.Bindings[1].CurrentValue);
    }
}
