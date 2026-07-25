using System;
using System.Collections.Generic;

namespace GabrielCapelettoStore.Hub.Entitlement;

/// <summary>
/// The lease-verification public keys embedded in this hub release (keyId → Ed25519 public key).
/// The verification key is PUBLIC — only the server's private signing key is secret. Rotation
/// ships in a hub update; this build advertises <see cref="KeyIds"/> in its capabilities, and the
/// server signs with a key in that set (contract/device-authorization.md §7).
/// </summary>
public static class TrustAnchors
{
    // keyId → raw 32-byte Ed25519 public key (base64). Add new keys here as they are minted;
    // keep retired-but-still-deployed keys until every device has updated past them.
    private static readonly IReadOnlyDictionary<string, string> Keys = new Dictionary<string, string>
    {
        ["key202607241842PST"] = "9dI9N51av+OXYyWay42qjaTBOT5RTBfHimEtd20AU2U=",
    };

    /// <summary>The keyIds this build trusts, advertised to the server for self-negotiating rotation.</summary>
    public static IReadOnlyList<string> KeyIds { get; } = new List<string>(Keys.Keys);

    /// <summary>Raw Ed25519 public key bytes for a keyId, or null if this build doesn't trust it.</summary>
    public static byte[]? PublicKey(string? keyId)
        => keyId is not null && Keys.TryGetValue(keyId, out var b64) ? Convert.FromBase64String(b64) : null;
}
