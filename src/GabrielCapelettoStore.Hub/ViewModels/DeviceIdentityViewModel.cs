using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GabrielCapelettoStore.Hub.Identity;

namespace GabrielCapelettoStore.Hub.ViewModels;

/// <summary>
/// Backs the "This device" section of Settings: collects the raw fingerprint on demand
/// and exposes it for display. No scoring/verdict yet — that is a later step.
/// </summary>
public partial class DeviceIdentityViewModel : ViewModelBase
{
    private readonly IDeviceFingerprintCollector _collector;
    private DeviceFingerprint? _fingerprint;

    public DeviceIdentityViewModel(IDeviceFingerprintCollector collector)
    {
        _collector = collector;
    }

    /// <summary>Design-time constructor: shows the sample device.</summary>
    public DeviceIdentityViewModel() : this(new NullFingerprintCollector())
    {
        Load();
    }

    public ObservableCollection<SignalRow> Signals { get; } = [];

    [ObservableProperty]
    public partial string Summary { get; set; } = "";

    public string DiagnosticsText => _fingerprint?.ToDiagnosticsText() ?? "";

    /// <summary>Collects the fingerprint (once) and populates the display rows.</summary>
    public void Load()
    {
        _fingerprint ??= _collector.Collect();

        Signals.Clear();
        foreach (var row in _fingerprint.ToRows())
        {
            Signals.Add(row);
        }

        Summary = _fingerprint.TpmPresent
            ? $"{_fingerprint.TpmVersion} present — hardware-backed identity available."
            : "No TPM detected — identity relies on firmware signals only.";
    }
}
