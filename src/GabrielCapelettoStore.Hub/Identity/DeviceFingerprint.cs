using System;
using System.Collections.Generic;

namespace GabrielCapelettoStore.Hub.Identity;

/// <summary>
/// The raw hardware signals collected from this device, plus whether each is
/// <b>distinctive</b> (a real device-specific value) or a <b>shared default</b>
/// (a factory placeholder — real, but shared across many boards; see
/// <see cref="FingerprintHygiene"/>). Collection + classification only; the scoring
/// verdict lives in <see cref="FingerprintScorer"/>. Every field is a firmware-reported
/// value any local tool can read; none is a secret (the TPM EK public is, by design, public).
/// </summary>
public sealed record DeviceFingerprint
{
    public string? SystemUuid { get; init; }
    public string? BaseboardSerial { get; init; }

    /// <summary>Primary MAC for display (wired preferred). See <see cref="PhysicalMacs"/> for the full set.</summary>
    public string? PermanentMac { get; init; }

    /// <summary>All physical NIC MACs (wired + Wi-Fi), so plug/unplug/network change doesn't break the match.</summary>
    public IReadOnlyList<string> PhysicalMacs { get; init; } = [];

    public string? ProcessorId { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public bool TpmPresent { get; init; }
    public string? TpmVersion { get; init; }

    /// <summary>SHA-256 of the TPM endorsement-key public — the one genuinely UNIQUE TPM signal.</summary>
    public string? TpmEkPublicHash { get; init; }

    public bool SystemUuidDistinctive => FingerprintHygiene.IsDistinctiveUuid(SystemUuid);
    public bool BaseboardSerialDistinctive => FingerprintHygiene.IsDistinctive(BaseboardSerial);
    public bool PermanentMacDistinctive => FingerprintHygiene.IsDistinctiveMac(PermanentMac);
    public bool ProcessorIdDistinctive => FingerprintHygiene.IsDistinctive(ProcessorId);
    public bool ManufacturerDistinctive => FingerprintHygiene.IsDistinctive(Manufacturer);
    public bool ModelDistinctive => FingerprintHygiene.IsDistinctive(Model);

    /// <summary>The EK public is a per-device crypto key — always distinctive when present.</summary>
    public bool TpmEkDistinctive => !string.IsNullOrWhiteSpace(TpmEkPublicHash);

    /// <summary>At least one strong per-device anchor: a distinctive UUID/board serial, or the TPM EK.</summary>
    public bool HasStrongAnchor => SystemUuidDistinctive || BaseboardSerialDistinctive || TpmEkDistinctive;

    /// <summary>Flat label/value/distinctiveness rows for display and "copy diagnostics".</summary>
    public IEnumerable<SignalRow> ToRows()
    {
        yield return new SignalRow("System UUID", Display(SystemUuid), SystemUuidDistinctive);
        yield return new SignalRow("Baseboard serial", Display(BaseboardSerial), BaseboardSerialDistinctive);
        yield return new SignalRow("Network MAC (permanent)", Display(PermanentMac), PermanentMacDistinctive);
        yield return new SignalRow("Processor ID", Display(ProcessorId), ProcessorIdDistinctive);
        yield return new SignalRow("Manufacturer", Display(Manufacturer), ManufacturerDistinctive);
        yield return new SignalRow("Model", Display(Model), ModelDistinctive);
        // TPM rows are status/anchor lines, not identifier strings → no shared-default badge.
        yield return new SignalRow("TPM", TpmPresent ? $"Present ({TpmVersion})" : "Not detected", true);
        yield return new SignalRow("TPM endorsement key",
            TpmEkDistinctive ? Display(TpmEkPublicHash) : "Not available", true);
    }

    public string ToDiagnosticsText()
        => string.Join(Environment.NewLine, ToRowsForText());

    private IEnumerable<string> ToRowsForText()
    {
        foreach (var r in ToRows())
        {
            yield return r.IsDistinctive ? $"{r.Label}: {r.Value}" : $"{r.Label}: {r.Value}  (shared default)";
        }
    }

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}

/// <summary>
/// A single label/value line on the device page. <c>IsDistinctive</c> is false for a
/// shared factory default (a real value, but not usable to identify this specific device).
/// </summary>
public sealed record SignalRow(string Label, string Value, bool IsDistinctive);
