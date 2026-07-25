namespace GabrielCapelettoStore.Hub.Entitlement;

/// <summary>
/// Where the hub authorizes the device and what license it presents. Bound from the
/// "Entitlement" / "Device" sections of appsettings.json; overridable via env
/// (GCSTORE_Entitlement__BaseUrl, GCSTORE_Device__License) for prod / provisioning.
/// </summary>
public sealed class EntitlementOptions
{
    public const string EntitlementSection = "Entitlement";
    public const string DeviceSection = "Device";

    /// <summary>Absolute base URL of the entitlement server, e.g. https://gcstore.gabrielcapeletto.com.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The device license key — the identity anchor sent in the claim. In production this is
    /// pre-provisioned per device; for testing it is set here (and surfaced in Settings) so the
    /// same value can be provisioned server-side. Any value returns 403 until provisioned.
    /// </summary>
    public string? License { get; set; }
}
