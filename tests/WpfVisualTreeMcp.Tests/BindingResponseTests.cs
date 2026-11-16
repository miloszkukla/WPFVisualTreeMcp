using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WpfVisualTreeMcp.Server.Services;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class BindingResponseTests
{
    [Fact]
    public void PropertyValueSourceIsTypedAndUnknownValuesFail()
    {
        var bridge = new NamedPipeBridge(NullLogger<NamedPipeBridge>.Instance, Mock.Of<IProcessManager>());
        var parse = typeof(NamedPipeBridge).GetMethod("ParseElementPropertiesResponse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var response = new GetElementPropertiesResponse { PropertiesJson = "{\"properties\":[{\"name\":\"Visibility\",\"source\":\"ImplicitStyle\"}]}" };
        var result = (ElementPropertiesResult)parse.Invoke(bridge, new object[] { response, "element" })!;
        Assert.Equal(PropertyValueSource.ImplicitStyle, Assert.Single(result.Properties).Source);

        response.PropertiesJson = "{\"properties\":[{\"name\":\"Visibility\",\"source\":\"Unexpected\"}]}";
        var error = Assert.Throws<TargetInvocationException>(() => parse.Invoke(bridge, new object[] { response, "element" }));
        Assert.IsType<JsonException>(error.InnerException);
    }

    [Fact]
    public void UnknownBindingAndLayoutValuesAreNotSilentlyIgnored()
    {
        var bridge = new NamedPipeBridge(NullLogger<NamedPipeBridge>.Instance, Mock.Of<IProcessManager>());
        var parseBindings = typeof(NamedPipeBridge).GetMethod("ParseBindingsResponse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var binding = Assert.Throws<TargetInvocationException>(() => parseBindings.Invoke(bridge, new object[]
            { new GetBindingsResponse { BindingsJson = "{\"bindings\":[{\"mode\":\"Unexpected\"}]}" }, "element" }));
        Assert.IsType<JsonException>(binding.InnerException);

        var parseLayout = typeof(NamedPipeBridge).GetMethod("ParseLayoutInfoResponse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var layout = Assert.Throws<TargetInvocationException>(() => parseLayout.Invoke(bridge, new object[]
            { new GetLayoutInfoResponse { LayoutJson = "{\"layout\":{\"visibility\":\"Unexpected\"}}" }, "element" }));
        Assert.IsType<JsonException>(layout.InnerException);
    }

    [Fact]
    public void BindingAndLayoutValuesAreDecodedAsEnumsAndMissingFieldsStayNull()
    {
        var bridge = new NamedPipeBridge(NullLogger<NamedPipeBridge>.Instance, Mock.Of<IProcessManager>());
        var parseBindings = typeof(NamedPipeBridge).GetMethod("ParseBindingsResponse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var bindingResponse = new GetBindingsResponse { BindingsJson = "{\"bindings\":[{\"mode\":\"TwoWay\",\"updateTrigger\":\"PropertyChanged\",\"status\":\"AsyncPending\"},{}]}" };
        var bindings = (BindingsResult)parseBindings.Invoke(bridge, new object[] { bindingResponse, "element" })!;
        Assert.Equal(BindingMode.TwoWay, bindings.Bindings[0].Mode);
        Assert.Equal(UpdateSourceTrigger.PropertyChanged, bindings.Bindings[0].UpdateTrigger);
        Assert.Equal(BindingStatus.AsyncPending, bindings.Bindings[0].Status);
        Assert.Null(bindings.Bindings[1].Mode);
        Assert.Null(bindings.Bindings[1].Status);

        var parseLayout = typeof(NamedPipeBridge).GetMethod("ParseLayoutInfoResponse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var layoutResponse = new GetLayoutInfoResponse { LayoutJson = "{\"layout\":{\"horizontalAlignment\":\"Left\",\"verticalAlignment\":\"Bottom\",\"visibility\":\"Collapsed\"}}" };
        var layout = (LayoutInfoResult)parseLayout.Invoke(bridge, new object[] { layoutResponse, "element" })!;
        Assert.Equal(HorizontalAlignment.Left, layout.Layout.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Bottom, layout.Layout.VerticalAlignment);
        Assert.Equal(Visibility.Collapsed, layout.Layout.Visibility);
    }

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
