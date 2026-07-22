using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using GabrielCapelettoStore.Hub.Identity;

namespace GabrielCapelettoStore.Hub.ViewModels;

/// <summary>
/// Backs the "This device" section of Settings: collects the fingerprint, assesses it
/// against the stored baseline, and exposes the verdict + raw signals for display.
/// </summary>
public partial class DeviceIdentityViewModel : ViewModelBase
{
    private readonly IDeviceFingerprintCollector _collector;
    private readonly IIdentityBaselineStore _baselineStore;
    private readonly FingerprintScorer _scorer = new();

    private DeviceFingerprint? _fingerprint;

    public DeviceIdentityViewModel(IDeviceFingerprintCollector collector, IIdentityBaselineStore baselineStore)
    {
        _collector = collector;
        _baselineStore = baselineStore;
    }

    /// <summary>Design-time constructor: shows the sample device.</summary>
    public DeviceIdentityViewModel() : this(new NullFingerprintCollector(), new NullIdentityBaselineStore())
    {
        Load();
    }

    public ObservableCollection<SignalRow> Signals { get; } = [];

    public ObservableCollection<string> Anomalies { get; } = [];

    [ObservableProperty]
    public partial string VerdictHeadline { get; set; } = "";

    [ObservableProperty]
    public partial string VerdictDetail { get; set; } = "";

    [ObservableProperty]
    public partial bool HasAnomalies { get; set; }

    [ObservableProperty]
    public partial string DiagnosticsLine { get; set; } = "";

#if DEBUG
    public bool ShowDiagnostics => true;
#else
    public bool ShowDiagnostics => false;
#endif

    public string DiagnosticsText => _fingerprint?.ToDiagnosticsText() ?? "";

    /// <summary>Collects the fingerprint (once), assesses it, and populates the view.</summary>
    public void Load()
    {
        _fingerprint ??= _collector.Collect();

        Signals.Clear();
        foreach (var row in _fingerprint.ToRows())
        {
            Signals.Add(row);
        }

        var baseline = _baselineStore.Load();
        var assessment = _scorer.Assess(_fingerprint, baseline);
        if (assessment.ShouldRebind)
        {
            _baselineStore.Save(DeviceBaseline.From(_fingerprint));
        }

        ApplyAssessment(assessment);
    }

    private void ApplyAssessment(IdentityAssessment assessment)
    {
        (VerdictHeadline, VerdictDetail) = assessment.Verdict switch
        {
            IdentityVerdict.BaselineEstablished => (
                "Identity baseline established",
                "First run on this device — the current signals were saved as the reference for future checks."),
            IdentityVerdict.Recognized => (
                "This device is recognized",
                assessment.TpmMatch
                    ? "Verified by this device's TPM security chip."
                    : "Consistent with the signals saved for this device."),
            IdentityVerdict.Review => (
                "Checking this device",
                "Some hardware signals have changed since this device was registered."),
            IdentityVerdict.Unrecognized => (
                "Device not recognized",
                "This device's hardware doesn't match what was registered."),
            _ => ("", ""),
        };

        Anomalies.Clear();
        foreach (var anomaly in assessment.Anomalies)
        {
            Anomalies.Add(anomaly);
        }

        HasAnomalies = Anomalies.Count > 0;

        var parts = assessment.Contributions.Select(c => $"{Short(c.Field)}({c.Weight}):{c.Awarded}");
        DiagnosticsLine = $"{assessment.Verdict} · {assessment.Confidence} confidence · EK:{assessment.EkStatus}"
            + $" · firmware {assessment.Score}/100 · " + string.Join("  ", parts);
    }

    private static string Short(string field) => field.Split(' ')[0];
}
