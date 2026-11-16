using System.Text.Json;
using WpfVisualTreeMcp.Shared.Ipc;
using WpfVisualTreeMcp.Shared.Models;
using Xunit;

namespace WpfVisualTreeMcp.Tests;

public class TypedResponsePayloadTests
{
    [Fact]
    public void TreeAndFindPayloadsRetainTreeDepthAndSearchMetadata()
    {
        var node = "{\"handle\":\"leaf\",\"typeName\":\"Leaf\",\"depth\":80,\"children\":[]}";
        for (var i = 79; i >= 0; i--) node = "{\"handle\":\"n\",\"typeName\":\"Node\",\"depth\":" + i + ",\"children\":[" + node + "]}";
        var tree = IpcSerializer.Deserialize<VisualTreePayload>("{\"root\":" + node + ",\"totalElements\":81,\"maxDepthReached\":true}")!;
        var roundTrip = IpcSerializer.DeserializeResponse<GetVisualTreeResponse>(IpcSerializer.SerializeResponse(new GetVisualTreeResponse { Tree = tree, TotalElements = 81 }))!;
        var leaf = roundTrip.Tree!.Root!;
        for (var i = 0; i < 80; i++) leaf = Assert.Single(leaf.Children);
        Assert.Equal(80, leaf.Depth);
        var found = IpcSerializer.Deserialize<FindElementsPayload>("{\"elements\":[{\"handle\":\"h\",\"isVisible\":true,\"screenBounds\":{\"x\":1,\"y\":2,\"width\":3,\"height\":4}}],\"count\":1,\"truncated\":true}")!;
        Assert.Equal(4, Assert.Single(found.Elements).ScreenBounds!.Height);
        Assert.True(found.Truncated);
    }

    [Fact]
    public void PropertyResourceAndStylePayloadsRetainArbitraryValues()
    {
        var properties = IpcSerializer.Deserialize<PropertiesPayload>("{\"element\":{\"typeName\":\"Button\"},\"properties\":[{\"name\":\"Tag\",\"source\":\"Local\",\"value\":{\"nested\":[1,true]}}]}")!;
        var property = Assert.Single(properties.Properties);
        Assert.Equal(PropertyValueSource.Local, property.Source);
        Assert.True(property.Value.GetProperty("nested")[1].GetBoolean());
        var resources = IpcSerializer.Deserialize<List<ResourcePayload>>("[{\"key\":\"k\",\"value\":[1,2]}]")!;
        Assert.Equal(2, Assert.Single(resources).Value[1].GetInt32());
        var styles = IpcSerializer.Deserialize<StylesPayload>("{\"hasStyle\":true,\"style\":{\"setters\":[{\"property\":\"Tag\",\"value\":{\"custom\":1}}],\"triggers\":[{\"type\":\"custom\",\"value\":false}]}}")!;
        Assert.Equal(1, Assert.Single(styles.Style!.Setters).Value.GetProperty("custom").GetInt32());
        Assert.False(Assert.Single(styles.Style.Triggers!).Value.GetBoolean());
    }

    [Fact]
    public void NullDiagnosticValuesRemainPresentAcrossIpc()
    {
        var payload = IpcSerializer.Deserialize<ExplainTriggersPayload>("{\"styleTriggers\":[{\"conditions\":[{\"expected\":null,\"current\":null}],\"setters\":[{\"property\":\"Tag\",\"value\":null}]}],\"templateTriggers\":[],\"attribution\":null}")!;
        using var wire = JsonDocument.Parse(IpcSerializer.SerializeResponse(new ExplainTriggersResponse { Triggers = payload }));
        var trigger = wire.RootElement.GetProperty("triggers").GetProperty("styleTriggers")[0];
        Assert.Equal(JsonValueKind.Null, trigger.GetProperty("conditions")[0].GetProperty("expected").ValueKind);
        Assert.Equal(JsonValueKind.Null, trigger.GetProperty("conditions")[0].GetProperty("current").ValueKind);
        Assert.Equal(JsonValueKind.Null, trigger.GetProperty("setters")[0].GetProperty("value").ValueKind);
    }

    [Fact]
    public void DiagnosticPayloadsRetainNullAndOpenEndedValuesAcrossIpc()
    {
        var triggers = IpcSerializer.Deserialize<ExplainTriggersPayload>("{\"styleTriggers\":[{\"custom\":42}],\"templateTriggers\":[],\"attribution\":null}")!;
        var triggerResponse = IpcSerializer.DeserializeResponse<ExplainTriggersResponse>(IpcSerializer.SerializeResponse(new ExplainTriggersResponse { Triggers = triggers }))!;
        Assert.Null(triggerResponse.Triggers!.Attribution);
        Assert.Equal(42, Assert.Single(triggerResponse.Triggers.StyleTriggers!).AdditionalFields!["custom"].GetInt32());
        var evaluation = IpcSerializer.Deserialize<BindingEvaluationPayload>("{\"property\":\"Text\",\"valueSource\":\"Local\",\"binding\":null,\"extra\":{\"x\":1}}")!;
        var evaluated = IpcSerializer.DeserializeResponse<EvaluateBindingResponse>(IpcSerializer.SerializeResponse(new EvaluateBindingResponse { Evaluation = evaluation }))!;
        Assert.Equal(WpfBaseValueSource.Local, evaluated.Evaluation!.ValueSource);
        Assert.Null(evaluated.Evaluation.Binding);
        Assert.Equal(1, evaluated.Evaluation.AdditionalFields!["extra"].GetProperty("x").GetInt32());
        var context = IpcSerializer.Deserialize<DataContextPayload>("{\"dataContext\":null,\"dataContextSource\":\"N/A\",\"inheritanceChain\":[]}")!;
        var contextResponse = IpcSerializer.DeserializeResponse<GetDataContextResponse>(IpcSerializer.SerializeResponse(new GetDataContextResponse { DataContext = context }))!;
        Assert.Equal(DataContextSource.NotApplicable, contextResponse.DataContext!.DataContextSource);
        Assert.Null(contextResponse.DataContext.DataContext);
        Assert.Contains("\"N/A\"", IpcSerializer.Serialize(contextResponse.DataContext));
    }

    [Fact]
    public void DiffAndErrorPayloadsRetainTypedContents()
    {
        var diff = IpcSerializer.Deserialize<DiffPayload>("{\"summary\":{\"changed\":1,\"added\":0,\"removed\":0},\"changed\":[{\"handle\":\"h\",\"changes\":{\"Text\":{\"from\":\"a\",\"to\":\"b\"}}}],\"added\":[],\"removed\":[]}")!;
        var result = IpcSerializer.DeserializeResponse<DiffResponse>(IpcSerializer.SerializeResponse(new DiffResponse { Diff = diff, ChangedCount = 1 }))!;
        Assert.Equal("b", Assert.Single(result.Diff!.Changed!).Changes!["Text"].To);
        var errors = IpcSerializer.Deserialize<BindingErrorsPayload>("{\"errors\":[{\"errorType\":\"PathError\",\"message\":\"failed\"}],\"count\":1}")!;
        Assert.Equal(BindingErrorType.PathError, Assert.Single(errors.Errors).ErrorType);
        Assert.Throws<JsonException>(() => IpcSerializer.Deserialize<DataContextPayload>("{\"dataContextSource\":\"unrecognized\"}"));
    }
}
