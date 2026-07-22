using System;
using System.Collections.Generic;
using System.Linq;

namespace GabrielCapelettoStore.Hub.Identity;

/// <summary>
/// Prototype LOCAL scorer. The TPM endorsement key is treated as a HARDWARE-ATTESTATION
/// TIER, not just another weight: it is a per-device cryptographic key fused in the TPM
/// (hard to forge), so an EK match is dispositive → High confidence on its own. The
/// weighted sum of firmware signals (UUID/board/MAC/CPU/model — all rewritable strings)
/// is only the FALLBACK for machines with no TPM/EK. Uniqueness still comes only from
/// DISTINCTIVE fields (shared defaults weight 0); shared defaults are checked for
/// consistency. All of this is advisory and moves server-side once the backend exists;
/// the numbers here are heuristic placeholders, not a validated scheme.
/// </summary>
public sealed class FingerprintScorer
{
    public const int WeightUuid = 50;
    public const int WeightBoard = 30;
    public const int WeightMac = 15;
    public const int WeightCpu = 3;
    public const int WeightModel = 2;

    private const int HighConfidence = 70;
    private const int MediumConfidence = 40;
    private const int StrongContradiction = 30; // a durable anchor showing a different real value

    public IdentityAssessment Assess(DeviceFingerprint current, DeviceBaseline? baseline)
    {
        if (baseline is null)
        {
            return new IdentityAssessment
            {
                Verdict = IdentityVerdict.BaselineEstablished,
                Confidence = IdentityConfidence.Low,
                EkStatus = current.TpmEkDistinctive ? "new" : "absent",
                ShouldRebind = true,
            };
        }

        // --- Firmware signals → additive fallback score (EK is handled as a tier, below). ---
        var rows = new List<(FieldContribution Contribution, int Contradiction, string? Anomaly)>
        {
            ClassifyValue("System UUID", WeightUuid,
                current.SystemUuidDistinctive, current.SystemUuid,
                FingerprintHygiene.IsDistinctiveUuid(baseline.SystemUuid), baseline.SystemUuid),
            ClassifyValue("Baseboard serial", WeightBoard,
                current.BaseboardSerialDistinctive, current.BaseboardSerial,
                FingerprintHygiene.IsDistinctive(baseline.BaseboardSerial), baseline.BaseboardSerial),
            ClassifyMac(current, baseline),
            ClassifyValue("Processor ID", WeightCpu,
                current.ProcessorIdDistinctive, current.ProcessorId,
                FingerprintHygiene.IsDistinctive(baseline.ProcessorId), baseline.ProcessorId),
            ClassifyValue("Model", WeightModel,
                current.ModelDistinctive, current.Model,
                FingerprintHygiene.IsDistinctive(baseline.Model), baseline.Model),
        };

        var contributions = rows.Select(r => r.Contribution).ToList();
        var anomalies = rows.Where(r => r.Anomaly is not null).Select(r => r.Anomaly!).ToList();
        var matched = rows.Sum(r => r.Contribution.Awarded);
        var contradicted = rows.Sum(r => r.Contradiction);
        var firmwareScore = Math.Min(100, matched);

        if (Changed(current.Manufacturer, baseline.Manufacturer))
        {
            anomalies.Add("Manufacturer string changed.");
        }

        if (baseline.TpmPresent && !current.TpmPresent)
        {
            anomalies.Add("TPM was present before but is now absent.");
        }

        // --- TPM endorsement key: the hardware-attestation tier. ---
        var baselineHadEk = !string.IsNullOrWhiteSpace(baseline.TpmEkPublicHash);
        var ekMatch = current.TpmEkDistinctive
            && string.Equals(current.TpmEkPublicHash, baseline.TpmEkPublicHash, StringComparison.OrdinalIgnoreCase);
        var ekMismatch = baselineHadEk && !ekMatch;

        if (ekMismatch)
        {
            anomalies.Add(current.TpmEkDistinctive
                ? "TPM endorsement key changed (possible firmware update or TPM clear)."
                : "TPM endorsement key is no longer readable.");
        }

        var ekStatus = ekMatch ? "attested"
            : ekMismatch ? (current.TpmEkDistinctive ? "changed" : "lost")
            : current.TpmEkDistinctive ? "new" : "absent";

        IdentityVerdict verdict;
        IdentityConfidence confidence;

        if (ekMatch)
        {
            // Dispositive: cryptographic, hardware-protected, per-device → same TPM, same device.
            verdict = IdentityVerdict.Recognized;
            confidence = IdentityConfidence.High;
        }
        else if (ekMismatch)
        {
            // EK changed/lost → owner-in-the-loop, whatever the firmware signals say.
            verdict = IdentityVerdict.Review;
            confidence = ConfidenceFor(firmwareScore);
        }
        else
        {
            // No TPM/EK to attest with → fall back to the firmware-signal score.
            if (contradicted >= StrongContradiction)
            {
                verdict = IdentityVerdict.Unrecognized;
            }
            else if (firmwareScore >= MediumConfidence || (matched > 0 && contradicted == 0))
            {
                verdict = IdentityVerdict.Recognized;
            }
            else
            {
                verdict = IdentityVerdict.Review;
            }

            confidence = ConfidenceFor(firmwareScore);
        }

        return new IdentityAssessment
        {
            Verdict = verdict,
            Score = firmwareScore,
            Confidence = confidence,
            TpmMatch = ekMatch,
            EkStatus = ekStatus,
            Anomalies = anomalies,
            Contributions = contributions,
            ShouldRebind = verdict == IdentityVerdict.Recognized,
        };
    }

    private static IdentityConfidence ConfidenceFor(int score)
        => score >= HighConfidence ? IdentityConfidence.High
            : score >= MediumConfidence ? IdentityConfidence.Medium
            : IdentityConfidence.Low;

    private static (FieldContribution, int, string?) ClassifyValue(
        string field, int weight, bool currentDistinctive, string? current, bool baselineDistinctive, string? baseline)
    {
        if (currentDistinctive && baselineDistinctive)
        {
            return Equal(current, baseline)
                ? (new FieldContribution(field, weight, weight, "match"), 0, null)
                : (new FieldContribution(field, weight, 0, "changed"), weight,
                    $"{field} changed to a different distinctive value.");
        }

        if (currentDistinctive && !baselineDistinctive)
        {
            // Was a shared default, now a real value → captured on re-bind. Not a contradiction.
            return (new FieldContribution(field, weight, 0, "promoted"), 0, null);
        }

        if (!currentDistinctive && baselineDistinctive)
        {
            return (new FieldContribution(field, weight, 0, "lost"), weight, $"{field} lost its distinctive value.");
        }

        // Both shared defaults: consistency only.
        return Changed(current, baseline)
            ? (new FieldContribution(field, weight, 0, "shared default (changed)"), 0, $"{field} default value changed.")
            : (new FieldContribution(field, weight, 0, "shared default"), 0, null);
    }

    private static (FieldContribution, int, string?) ClassifyMac(DeviceFingerprint current, DeviceBaseline baseline)
    {
        var overlap = current.PhysicalMacs.Any(m => baseline.PhysicalMacs.Contains(m, StringComparer.OrdinalIgnoreCase));
        if (overlap)
        {
            return (new FieldContribution("Network MAC", WeightMac, WeightMac, "match"), 0, null);
        }

        // A MAC that changed is common (new adapter, cable vs Wi-Fi, travel) → not a strong contradiction.
        var note = current.PhysicalMacs.Count > 0 && baseline.PhysicalMacs.Count > 0 ? "changed" : "no signal";
        return (new FieldContribution("Network MAC", WeightMac, 0, note), 0, null);
    }

    private static bool Equal(string? a, string? b)
        => string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Changed(string? a, string? b)
    {
        var x = (a ?? "").Trim();
        var y = (b ?? "").Trim();
        if (x.Length == 0 && y.Length == 0)
        {
            return false;
        }

        return !string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
    }
}
