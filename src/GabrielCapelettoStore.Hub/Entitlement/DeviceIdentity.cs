using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using GabrielCapelettoStore.Hub.Identity;

namespace GabrielCapelettoStore.Hub.Entitlement;

/// <summary>
/// The device's identity as the hub presents it: a generated-once, persisted, opaque
/// <see cref="DeviceId"/>, the license it holds, and the advisory fingerprint it sends for the
/// server to bind at activation. See contract/device-identity-provisioning.md.
/// </summary>
public sealed class DeviceIdentity
{
    private readonly IDeviceStore _store;
    private readonly DeviceFingerprint _fingerprint;
    private readonly string? _fallbackLicense;
    private DeviceState _state;

    public DeviceIdentity(IDeviceStore store, IDeviceFingerprintCollector collector, string? fallbackLicense = null)
    {
        _store = store;
        _state = store.Load();
        _fingerprint = collector.Collect();
        _fallbackLicense = string.IsNullOrWhiteSpace(fallbackLicense) ? null : fallbackLicense;

        // Generate + persist the device id once; reuse it forever after (drift never changes it).
        if (string.IsNullOrWhiteSpace(_state.DeviceId))
        {
            _state.DeviceId = Generate(_fingerprint);
            _store.Save(_state);
        }
    }

    public string DeviceId => _state.DeviceId!;

    /// <summary>The license the hub presents — user-entered/baked, else a dev/config fallback.</summary>
    public string? License => !string.IsNullOrWhiteSpace(_state.License) ? _state.License : _fallbackLicense;

    public bool HasLicense => !string.IsNullOrWhiteSpace(License);
    public bool AccessRequested => _state.AccessRequested;
    public string? RequestName => _state.RequestName;
    public string? RequestEmail => _state.RequestEmail;

    /// <summary>Persist a license the user typed on the licensing screen.</summary>
    public void SetLicense(string license)
    {
        _state.License = license.Trim();
        _store.Save(_state);
    }

    /// <summary>Record that an access request was submitted (drives the "requested" UX).</summary>
    public void MarkRequested(string name, string email)
    {
        _state.RequestName = name.Trim();
        _state.RequestEmail = email.Trim();
        _state.AccessRequested = true;
        _store.Save(_state);
    }

    /// <summary>Advisory, distinctive-only signals for the server to bind/soft-match. No junk defaults.</summary>
    public IReadOnlyDictionary<string, string> FingerprintClaim()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (_fingerprint.SystemUuidDistinctive && _fingerprint.SystemUuid is { } uuid) d["uuid"] = uuid;
        if (_fingerprint.BaseboardSerialDistinctive && _fingerprint.BaseboardSerial is { } board) d["board"] = board;
        if (_fingerprint.ProcessorIdDistinctive && _fingerprint.ProcessorId is { } cpu) d["cpu"] = cpu;
        if (_fingerprint.PermanentMacDistinctive && _fingerprint.PermanentMac is { } mac) d["mac"] = mac;
        if (_fingerprint.TpmEkPublicHash is { } ek && !string.IsNullOrWhiteSpace(ek)) d["ekHash"] = ek;
        return d;
    }

    // seed = high-res timestamp + OS randomness + the strongest available hardware anchor.
    private static string Generate(DeviceFingerprint fp)
    {
        var anchor =
            !string.IsNullOrWhiteSpace(fp.TpmEkPublicHash) ? fp.TpmEkPublicHash!
            : fp.SystemUuidDistinctive && fp.SystemUuid is { } uuid ? uuid
            : fp.BaseboardSerialDistinctive && fp.BaseboardSerial is { } board ? board
            : fp.ProcessorId ?? "none";

        var seed = $"gcstore-dev-v1|{DateTime.UtcNow.Ticks}|{Guid.NewGuid():N}|{anchor}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return "dev_" + Base32.Encode(hash)[..26].ToLowerInvariant();
    }
}

/// <summary>RFC 4648 base32 (no padding) — for a short, opaque, case-insensitive device id.</summary>
internal static class Base32
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string Encode(byte[] data)
    {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                sb.Append(Alphabet[(buffer >> bits) & 31]);
            }
        }

        if (bits > 0)
        {
            sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        }

        return sb.ToString();
    }
}
