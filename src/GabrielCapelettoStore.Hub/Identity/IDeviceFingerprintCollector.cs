namespace GabrielCapelettoStore.Hub.Identity;

/// <summary>
/// Collects this device's hardware fingerprint. Per-OS behind this seam (Windows now,
/// Linux DMI later); the UI and any future scorer depend only on this interface.
/// </summary>
public interface IDeviceFingerprintCollector
{
    DeviceFingerprint Collect();
}
