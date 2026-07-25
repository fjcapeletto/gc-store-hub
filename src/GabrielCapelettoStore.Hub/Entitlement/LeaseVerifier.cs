using System;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace GabrielCapelettoStore.Hub.Entitlement;

/// <summary>
/// Verifies an entitlement lease: a JWS Compact string (RFC 7515) signed with EdDSA/Ed25519
/// (RFC 8037). The signature is checked over the exact <c>header.payload</c> ASCII bytes,
/// offline, without re-serializing — no JSON canonicalization. Returns the decoded claims only
/// when the signature verifies against an embedded trust-anchor key selected by the header kid.
/// </summary>
public static class LeaseVerifier
{
    private static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web);

    /// <summary>Verify + decode, or null if the token is malformed, wrongly signed, or untrusted.</summary>
    public static LeasePayload? Verify(string compactJws)
    {
        if (string.IsNullOrWhiteSpace(compactJws))
        {
            return null;
        }

        var parts = compactJws.Trim().Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var header = JsonSerializer.Deserialize<JwsHeader>(Base64Url.Decode(parts[0]), PayloadJson);
            if (header is null || !string.Equals(header.Alg, "EdDSA", StringComparison.Ordinal))
            {
                return null;
            }

            var publicKey = TrustAnchors.PublicKey(header.Kid);
            if (publicKey is null)
            {
                return null; // kid we don't trust — caller treats a total miss as "must update".
            }

            // Signing input is the ASCII of "header.payload" exactly as received.
            var signingInput = Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]);
            var signature = Base64Url.Decode(parts[2]);

            var verifier = new Ed25519Signer();
            verifier.Init(forSigning: false, new Ed25519PublicKeyParameters(publicKey, 0));
            verifier.BlockUpdate(signingInput, 0, signingInput.Length);
            if (!verifier.VerifySignature(signature))
            {
                return null;
            }

            return JsonSerializer.Deserialize<LeasePayload>(Base64Url.Decode(parts[1]), PayloadJson);
        }
        catch
        {
            return null; // any parse/crypto failure ⇒ not a valid lease.
        }
    }

    private sealed record JwsHeader
    {
        [System.Text.Json.Serialization.JsonPropertyName("alg")] public string? Alg { get; init; }
        [System.Text.Json.Serialization.JsonPropertyName("kid")] public string? Kid { get; init; }
    }
}

/// <summary>base64url (RFC 4648 §5) decode, no padding — as used by JWS.</summary>
internal static class Base64Url
{
    public static byte[] Decode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }

        return Convert.FromBase64String(s);
    }
}
