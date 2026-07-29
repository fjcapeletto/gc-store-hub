using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GabrielCapelettoStore.Hub.Api;

/// <summary>Result of loading an api app's descriptor.</summary>
public sealed record DescriptorLoad
{
    public required ApiTokenOutcome TokenOutcome { get; init; }
    public AppDescriptor? Descriptor { get; init; }   // null → fall back to the raw request view (L0)
    public string? ApiBaseUrl { get; init; }
    public bool IsGranted => TokenOutcome.Status == ApiTokenStatus.Granted;
}

/// <summary>Outcome of running one descriptor action against the app backend.</summary>
public sealed record ApiCallResult
{
    public bool Ok { get; init; }
    public int Status { get; init; }
    public string Body { get; init; } = "";
    public string? Error { get; init; }
}

/// <summary>
/// Loads an api app's L1 descriptor and runs its actions against the app's own backend, authenticated
/// with the brokered token. The hub never verifies the token (the backend does); this just carries it.
/// See contract/type-api-descriptor.md.
/// </summary>
public sealed class ApiDescriptorClient
{
    private const string DescriptorPath = "/.well-known/gcstore-descriptor.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApiTokenBroker _broker;

    // App backends are third-party and some endpoints are slow — a generous timeout, unlike the hub's
    // 15s client for its own server.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    public ApiDescriptorClient(ApiTokenBroker broker) => _broker = broker;

    public async Task<DescriptorLoad> LoadAsync(string appId, CancellationToken cancellationToken = default)
    {
        var outcome = await _broker.GetAsync(appId, cancellationToken).ConfigureAwait(false);
        if (outcome.Status != ApiTokenStatus.Granted || outcome.Token is not { } token)
        {
            return new DescriptorLoad { TokenOutcome = outcome };
        }

        var baseUrl = token.ApiBaseUrl.TrimEnd('/');
        AppDescriptor? descriptor = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl + DescriptorPath);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token.Token);
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                descriptor = JsonSerializer.Deserialize<AppDescriptor>(text, Json);
            }
        }
        catch
        {
            descriptor = null; // unreachable / malformed → caller falls back to the raw request view (L0)
        }

        return new DescriptorLoad { TokenOutcome = outcome, Descriptor = descriptor, ApiBaseUrl = baseUrl };
    }

    /// <summary>Compose and send an action's request (path/query/body/header + Bearer) and return the raw
    /// status + body. Caller renders per the action's <see cref="ActionResult"/>.</summary>
    public async Task<ApiCallResult> ExecuteAsync(
        string appId, DescriptorAction action, IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        var outcome = await _broker.GetAsync(appId, cancellationToken).ConfigureAwait(false);
        if (outcome.Status != ApiTokenStatus.Granted || outcome.Token is not { } token)
        {
            return new ApiCallResult { Ok = false, Error = $"token: {outcome.Status} {outcome.Reason}" };
        }

        try
        {
            var baseUrl = token.ApiBaseUrl.TrimEnd('/');
            var path = action.Path.StartsWith('/') ? action.Path : "/" + action.Path;

            var query = new List<string>();
            var body = new Dictionary<string, object?>();
            var headers = new List<(string, string)>();

            foreach (var input in action.Inputs)
            {
                var slot = (input.In ?? DefaultSlot(action.Method)).ToLowerInvariant();
                values.TryGetValue(input.Name, out var raw);
                raw ??= "";

                switch (slot)
                {
                    case "path":
                        path = path.Replace("{" + input.Name + "}", Uri.EscapeDataString(raw));
                        break;
                    case "header":
                        if (raw.Length > 0) headers.Add((input.Name, raw));
                        break;
                    case "body":
                        if (raw.Length > 0) body[input.Name] = Coerce(input.Type, raw);
                        break;
                    default: // query
                        if (raw.Length > 0)
                        {
                            query.Add($"{Uri.EscapeDataString(input.Name)}={Uri.EscapeDataString(raw)}");
                        }
                        break;
                }
            }

            var url = baseUrl + path + (query.Count > 0 ? "?" + string.Join("&", query) : "");
            using var request = new HttpRequestMessage(new HttpMethod(action.Method.ToUpperInvariant()), url);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token.Token);
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }

            if (body.Count > 0)
            {
                request.Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return new ApiCallResult { Ok = response.IsSuccessStatusCode, Status = (int)response.StatusCode, Body = text };
        }
        catch (Exception ex)
        {
            return new ApiCallResult { Ok = false, Error = ex.Message };
        }
    }

    private static string DefaultSlot(string method)
        => string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) ? "query" : "body";

    private static object? Coerce(string type, string raw) => type.ToLowerInvariant() switch
    {
        "number" => double.TryParse(raw, out var d) ? d : raw,
        "bool" => bool.TryParse(raw, out var b) ? b : raw,
        _ => raw,
    };
}
