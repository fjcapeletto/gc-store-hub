using System;
using System.Collections.Generic;
using System.Linq;

namespace GabrielCapelettoStore.Hub.Identity;

/// <summary>
/// The raw hardware signals collected from this device. This is COLLECTION only —
/// no scoring, no baseline, no verdict yet (those come in a later step). Every field
/// is a firmware-reported string that any local tool can already read; none is a secret.
/// </summary>
public sealed record DeviceFingerprint
{
    public string? SystemUuid { get; init; }
    public string? BaseboardSerial { get; init; }
    public string? PermanentMac { get; init; }
    public string? ProcessorId { get; init; }
    public string? Manufacturer { get; init; }
    public string? Model { get; init; }
    public bool TpmPresent { get; init; }
    public string? TpmVersion { get; init; }

    /// <summary>Flat label/value rows for display and for "copy diagnostics".</summary>
    public IEnumerable<SignalRow> ToRows()
    {
        yield return new SignalRow("System UUID", Display(SystemUuid));
        yield return new SignalRow("Baseboard serial", Display(BaseboardSerial));
        yield return new SignalRow("Network MAC (permanent)", Display(PermanentMac));
        yield return new SignalRow("Processor ID", Display(ProcessorId));
        yield return new SignalRow("Manufacturer", Display(Manufacturer));
        yield return new SignalRow("Model", Display(Model));
        yield return new SignalRow("TPM", TpmPresent ? $"Present ({TpmVersion})" : "Not detected");
    }

    public string ToDiagnosticsText()
        => string.Join(Environment.NewLine, ToRows().Select(r => $"{r.Label}: {r.Value}"));

    private static string Display(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}

/// <summary>A single label/value line shown on the device page.</summary>
public sealed record SignalRow(string Label, string Value);
