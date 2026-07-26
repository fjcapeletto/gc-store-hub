using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GabrielCapelettoStore.Hub.Delivery;

/// <summary>
/// The `app.json` at the root of an app package. Self-describing: it tells the hub how to launch
/// the app after extraction. See docs/authoring-apps.md.
/// </summary>
public sealed record AppManifest
{
    [JsonPropertyName("appId")] public required string AppId { get; init; }
    [JsonPropertyName("version")] public required string Version { get; init; }
    [JsonPropertyName("entryExe")] public required string EntryExe { get; init; }
    [JsonPropertyName("args")] public IReadOnlyList<string>? Args { get; init; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; init; }
}
