using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace GabrielCapelettoStore.Hub.Api;

/// <summary>
/// The hub's token broker for `type: api` apps: fetches a scoped API token from delivery, caches it per
/// app, and re-fetches when it nears expiry. This is the common tokenization for BOTH shapes of api app
/// — one embedded in the hub (called directly) and a standalone binary (served over the local vending
/// channel) — because the hub holds the device license and is the broker either way. The hub never
/// verifies the token (the app's own backend does). See contract/type-api.draft.md.
/// </summary>
public sealed class ApiTokenBroker
{
    // Refresh a little before real expiry so a caller never gets a token about to die in flight.
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(30);

    private readonly IApiTokenService _service;
    private readonly ConcurrentDictionary<string, ApiToken> _cache = new(StringComparer.Ordinal);

    public ApiTokenBroker(IApiTokenService service) => _service = service;

    /// <summary>A valid token for the app — cached if still fresh, otherwise freshly minted. Revocation
    /// (403 / 404) surfaces here at the next fetch and drops the cache entry.</summary>
    public async Task<ApiTokenOutcome> GetAsync(string appId, CancellationToken cancellationToken = default)
    {
        if (_cache.TryGetValue(appId, out var cached)
            && DateTimeOffset.UtcNow < cached.ExpiresAt - RefreshSkew)
        {
            return ApiTokenOutcome.Granted(cached);
        }

        var outcome = await _service.FetchAsync(appId, cancellationToken).ConfigureAwait(false);
        if (outcome.Status == ApiTokenStatus.Granted && outcome.Token is not null)
        {
            _cache[appId] = outcome.Token;
        }
        else
        {
            _cache.TryRemove(appId, out _); // revoked / expired / unreachable → don't serve a stale token
        }

        return outcome;
    }

    /// <summary>Forget a cached token (e.g. the app was uninstalled / unsubscribed).</summary>
    public void Forget(string appId) => _cache.TryRemove(appId, out _);
}
