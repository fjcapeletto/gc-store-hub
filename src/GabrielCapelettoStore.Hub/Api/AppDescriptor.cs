using System.Collections.Generic;
using System.Text.Json;

namespace GabrielCapelettoStore.Hub.Api;

/// <summary>
/// The L1 descriptor an api app's backend serves at GET {apiBaseUrl}/.well-known/gcstore-descriptor.json.
/// The hub renders a generic UI from it — actions → typed forms → results. See
/// contract/type-api-descriptor.md.
/// </summary>
public sealed record AppDescriptor
{
    public int DescriptorVersion { get; init; }
    public DescriptorApp? App { get; init; }
    public IReadOnlyList<DescriptorAction> Actions { get; init; } = [];
}

public sealed record DescriptorApp
{
    public string? Id { get; init; }
    public string? Title { get; init; }
    public string? Summary { get; init; }
}

/// <summary>One callable endpoint the app exposes: a titled action with typed inputs and a result hint.</summary>
public sealed record DescriptorAction
{
    public string Id { get; init; } = "";
    public string? Title { get; init; }
    public string Method { get; init; } = "GET";
    public string Path { get; init; } = "/";

    /// <summary>On/off toggle: false hides this action from the hub's UI. Absent = on (default true).</summary>
    public bool Enabled { get; init; } = true;

    public IReadOnlyList<ActionInput> Inputs { get; init; } = [];
    public ActionResult? Result { get; init; }
}

/// <summary>A typed form field. `In` = query|path|body|header (defaults by method when absent).</summary>
public sealed record ActionInput
{
    public string Name { get; init; } = "";
    public string? Label { get; init; }
    public string Type { get; init; } = "string";   // string|number|bool|enum|date
    public string? In { get; init; }                 // query|path|body|header
    public bool Required { get; init; }
    public JsonElement? Default { get; init; }
    public IReadOnlyList<string> Options { get; init; } = [];
}

/// <summary>How the response renders: a container hint, an optional per-field map, and (for lists)
/// the key holding the array inside a paginated envelope.</summary>
public sealed record ActionResult
{
    public string Render { get; init; } = "json";    // json|text|keyValue|table|link|html
    public string? ItemsPath { get; init; }           // e.g. "items"
    public IReadOnlyDictionary<string, string> Fields { get; init; }
        = new Dictionary<string, string>();           // fieldName -> text|number|bool|date|datetime|link|html|json
}
