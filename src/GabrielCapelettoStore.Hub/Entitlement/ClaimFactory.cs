namespace GabrielCapelettoStore.Hub.Entitlement;

/// <summary>
/// Builds the `{ claim, capabilities }` a device presents to /v1/authorize and /v1/delivery —
/// one place so both calls send the identical shape.
/// </summary>
public static class ClaimFactory
{
    public static DeviceClaim Claim(DeviceIdentity device) => new()
    {
        DeviceId = device.DeviceId,
        License = device.License, // optional — omitted when null
        Fingerprint = device.FingerprintClaim(),
    };

    public static Capabilities Capabilities() => new()
    {
        // Clear phase: we can unwrap a soft envelope; TPM sealing not wired yet.
        Schemes = ["fingerprint-wrap-v1"],
        CanSealTpm = false,
        HasTpmEk = false,
        TrustedKeyIds = TrustAnchors.KeyIds,
    };
}
