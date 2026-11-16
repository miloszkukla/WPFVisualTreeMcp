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
    public void BindingPayloadRoundTripsDetailedAndArbitraryValues()
    {
        const string source = "{\"bindings\":[{\"property\":\"Text\",\"type\":\"MultiBinding\",\"childBindings\":[{\"path\":\"Name\"}],\"converterParameter\":{\"arbitrary\":[1,true]},\"fallbackValue\":12,\"futureDetail\":{\"key\":\"value\"}}]}";
        var response = new GetBindingsResponse { Bindings = IpcSerializer.Deserialize<BindingsPayload>(source) };
        var roundTrip = IpcSerializer.DeserializeResponse<GetBindingsResponse>(IpcSerializer.SerializeResponse(response))!;
        var binding = Assert.Single(roundTrip.Bindings!.Bindings);
        Assert.Equal(BindingKind.MultiBinding, binding.Type);
        Assert.Equal("Name", Assert.Single(binding.ChildBindings!).Path);
        Assert.True(binding.ConverterParameter.GetProperty("arbitrary")[1].GetBoolean());
        Assert.Equal(12, binding.FallbackValue.GetInt32());
        Assert.Equal("value", binding.AdditionalFields!["futureDetail"].GetProperty("key").GetString());
    }

    [Fact]
    public void LayoutPayloadRoundTripsNonFiniteAndErrorRepresentations()
    {
        var payload = IpcSerializer.Deserialize<LayoutPayload>("{\"layout\":{\"actualWidth\":null,\"margin\":{\"left\":3.5},\"visibility\":\"Collapsed\"}}");
        var response = IpcSerializer.DeserializeResponse<GetLayoutInfoResponse>(IpcSerializer.SerializeResponse(new GetLayoutInfoResponse { Layout = payload }))!;
        Assert.Null(response.Layout!.Layout!.ActualWidth);
        Assert.Equal(3.5, response.Layout.Layout.Margin!.Left);
        Assert.Equal(Visibility.Collapsed, response.Layout.Layout.Visibility);
        var error = IpcSerializer.Deserialize<LayoutPayload>("{\"error\":\"Element is not a UIElement\"}");
        Assert.Equal("Element is not a UIElement", error!.Error);
    }

    [Fact]
    public void PropertyValueSourceIsTypedAndUnknownValuesFail()
    {
        var bridge = new NamedPipeBridge(NullLogger<NamedPipeBridge>.Instance, Mock.Of<IProcessManager>());
        var parse = typeof(NamedPipeBridge).GetMethod("ParseElementPropertiesResponse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var response = new GetElementPropertiesResponse { Properties = IpcSerializer.Deserialize<PropertiesPayload>("{\"properties\":[{\"name\":\"Visibility\",\"source\":\"ImplicitStyle\"}]}") };
        var result = (ElementPropertiesResult)parse.Invoke(bridge, new object[] { response, "element" })!;
        Assert.Equal(PropertyValueSource.ImplicitStyle, Assert.Single(result.Properties).Source);

        Assert.Throws<JsonException>(() => IpcSerializer.Deserialize<PropertiesPayload>("{\"properties\":[{\"name\":\"Visibility\",\"source\":\"Unexpected\"}]}"));
    }

    [Fact]
    public void UnknownBindingAndLayoutValuesAreNotSilentlyIgnored()
    {
        Assert.Throws<JsonException>(() => IpcSerializer.Deserialize<BindingsPayload>("{\"bindings\":[{\"mode\":\"Unexpected\"}]}"));
        Assert.Throws<JsonException>(() => IpcSerializer.Deserialize<LayoutPayload>("{\"layout\":{\"visibility\":\"Unexpected\"}}"));
    }

    [Fact]
    public void BindingAndLayoutValuesAreDecodedAsEnumsAndMissingFieldsStayNull()
    {
        var bridge = new NamedPipeBridge(NullLogger<NamedPipeBridge>.Instance, Mock.Of<IProcessManager>());
        var parseBindings = typeof(NamedPipeBridge).GetMethod("ParseBindingsResponse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var bindingResponse = new GetBindingsResponse { Bindings = IpcSerializer.Deserialize<BindingsPayload>("{\"bindings\":[{\"mode\":\"TwoWay\",\"updateTrigger\":\"PropertyChanged\",\"status\":\"AsyncPending\"},{}]}") };
        var bindings = (BindingsResult)parseBindings.Invoke(bridge, new object[] { bindingResponse, "element" })!;
        Assert.Equal(BindingMode.TwoWay, bindings.Bindings[0].Mode);
        Assert.Equal(UpdateSourceTrigger.PropertyChanged, bindings.Bindings[0].UpdateTrigger);
        Assert.Equal(BindingStatus.AsyncPending, bindings.Bindings[0].Status);
        Assert.Null(bindings.Bindings[1].Mode);
        Assert.Null(bindings.Bindings[1].Status);

        var parseLayout = typeof(NamedPipeBridge).GetMethod("ParseLayoutInfoResponse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var layoutResponse = new GetLayoutInfoResponse { Layout = IpcSerializer.Deserialize<LayoutPayload>("{\"layout\":{\"horizontalAlignment\":\"Left\",\"verticalAlignment\":\"Bottom\",\"visibility\":\"Collapsed\"}}") };
        var layout = (LayoutInfoResult)parseLayout.Invoke(bridge, new object[] { layoutResponse, "element" })!;
        Assert.Equal(HorizontalAlignment.Left, layout.Layout.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Bottom, layout.Layout.VerticalAlignment);
        Assert.Equal(Visibility.Collapsed, layout.Layout.Visibility);
    }

    [Fact]
    public void BindingSourceIsDecodedWithoutCombiningTheElementNameAndKind()
    {
        var bridge = new NamedPipeBridge(NullLogger<NamedPipeBridge>.Instance, Mock.Of<IProcessManager>());
        var parse = typeof(NamedPipeBridge).GetMethod("ParseBindingsResponse", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var response = new GetBindingsResponse { Bindings = IpcSerializer.Deserialize<BindingsPayload>("{\"bindings\":[{\"type\":\"Binding\",\"sourceKind\":\"ElementName\",\"elementName\":\"target\"}]}") };
        var result = (BindingsResult)parse.Invoke(bridge, new object[] { response, "element" })!;
        var binding = Assert.Single(result.Bindings);
        Assert.Equal(BindingKind.Binding, binding.Kind);
        Assert.Equal(BindingSourceKind.ElementName, binding.SourceKind);
        Assert.Equal("target", binding.ElementName);
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
            Bindings = IpcSerializer.Deserialize<BindingsPayload>("{\"bindings\":[{\"property\":\"IsChecked\",\"path\":\"ShowDx\",\"currentValue\":" + jsonValue
                + "},{\"property\":\"IsEnabled\",\"path\":\"CanEdit\",\"currentValue\":true}]}")
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
