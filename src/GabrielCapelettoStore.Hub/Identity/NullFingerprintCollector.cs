namespace GabrielCapelettoStore.Hub.Identity;

/// <summary>
/// Fallback collector used off-Windows and by the XAML previewer. Returns representative
/// sample values so the device page renders without touching real hardware.
/// </summary>
public sealed class NullFingerprintCollector : IDeviceFingerprintCollector
{
    public DeviceFingerprint Collect() => new()
    {
        SystemUuid = "4C4C4544-0043-4210-8034-C6C04F303233",
        BaseboardSerial = "/8X9PL2/CN129000",
        PermanentMac = "A1:B2:C3:D4:E5:F6",
        PhysicalMacs = ["A1:B2:C3:D4:E5:F6", "A1:B2:C3:D4:E5:F7"],
        ProcessorId = "BFEBFBFF000906EA",
        Manufacturer = "Sample Manufacturer",
        Model = "Sample Model 14",
        TpmPresent = true,
        TpmVersion = "TPM 2.0",
        TpmEkPublicHash = "C7874B84B3A9EFB0C8EAAAD005BF13F078A52673A0D7C27342722D86C933A8ED",
    };
}
