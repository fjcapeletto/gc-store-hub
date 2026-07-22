using System.Collections.Generic;
using System.Linq;

namespace GabrielCapelettoStore.Hub.Identity;

/// <summary>
/// The stored snapshot of a device's fingerprint — the baseline a later check compares
/// against. Serializable (plain settable members). NOTE: while there is no backend, this
/// lives locally as a prototype; the authoritative baseline moves server-side later.
/// </summary>
public sealed class DeviceBaseline
{
    public string? SystemUuid { get; set; }
    public string? BaseboardSerial { get; set; }
    public List<string> PhysicalMacs { get; set; } = [];
    public string? ProcessorId { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public bool TpmPresent { get; set; }
    public string? TpmVersion { get; set; }
    public string? TpmEkPublicHash { get; set; }

    public static DeviceBaseline From(DeviceFingerprint fingerprint) => new()
    {
        SystemUuid = fingerprint.SystemUuid,
        BaseboardSerial = fingerprint.BaseboardSerial,
        PhysicalMacs = fingerprint.PhysicalMacs.ToList(),
        ProcessorId = fingerprint.ProcessorId,
        Manufacturer = fingerprint.Manufacturer,
        Model = fingerprint.Model,
        TpmPresent = fingerprint.TpmPresent,
        TpmVersion = fingerprint.TpmVersion,
        TpmEkPublicHash = fingerprint.TpmEkPublicHash,
    };
}

/// <summary>Persists and retrieves the local device baseline.</summary>
public interface IIdentityBaselineStore
{
    DeviceBaseline? Load();

    void Save(DeviceBaseline baseline);
}
