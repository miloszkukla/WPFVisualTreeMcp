# Typed WPF control API

`WpfVisualTreeMcp.Server.Controls` provides session-bound proxies for WPF controls. The target remains in its own process; reads and actions go through the inspector bridge. Create the output directory before attaching, and attach from the same interactive Windows session as the target.

```csharp
using WpfVisualTreeMcp.Server.Controls;

var session = new WpfSession(processId, outputDirectory);
await session.AttachAsync();

var window = await session.FindAsync<Window>(name: "MainWindow");
var save = await window.FindAsync<Button>(name: "SaveButton");
if (await save.IsEnabledAsync())
    await save.ClickAsync();
```

Lookups are scoped when invoked on an element. They require a unique visible match by default and use the proxy's `[WpfType]` to constrain the target type. Set `visibleOnly: false` when the selector must include hidden controls. `matchType: false` preserves a selector that did not originally include a type: it first finds the unique element without a type constraint and then verifies the selected element is compatible with the requested proxy.

Application and third-party proxies can live in the consuming project; the target assembly does not have to be loaded into the client:

```csharp
[WpfType("MyCustomControl")]
public sealed class MyCustomControl(WpfElement element) : UserControl(element)
{
    public Task<bool> IsReadyAsync() => PropertyAsync<bool>("IsReady");
}
```

Use the actual target type name and appropriate WPF base class. Every concrete proxy must declare its own `[WpfType]` and expose a public constructor accepting a `WpfElement`. A proxy belongs to the session that found it and cannot be used as a search root in another session. Inspector-generation and process identity checks still apply to each transport operation; reacquire elements after an application or inspector restart. Application model properties can be read with `ReadAsync` when needed, while control-specific methods expose common WPF operations.
