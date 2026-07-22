using System.Collections.Generic;

namespace GabrielCapelettoStore.Hub.Identity;

/// <summary>The outcome of comparing a fresh fingerprint against the stored baseline.</summary>
public enum IdentityVerdict
{
    /// <summary>First-ever run: baseline was just established, nothing to compare yet.</summary>
    BaselineEstablished,

    /// <summary>Consistent with the baseline — same device.</summary>
    Recognized,

    /// <summary>Ambiguous — low confidence or a shared-default anomaly; owner-in-the-loop.</summary>
    Review,

    /// <summary>A durable anchor now shows a different value — looks like a different device.</summary>
    Unrecognized,
}

public enum IdentityConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>One field's part in the score (for dev diagnostics).</summary>
public sealed record FieldContribution(string Field, int Weight, int Awarded, string Note);

public sealed record IdentityAssessment
{
    public IdentityVerdict Verdict { get; init; }

    /// <summary>Uniqueness confidence score 0–100 (matched distinctive weights + TPM bonus).</summary>
    public int Score { get; init; }

    public IdentityConfidence Confidence { get; init; }

    /// <summary>True when the TPM endorsement key matched — a hardware-attested identity.</summary>
    public bool TpmMatch { get; init; }

    /// <summary>EK tier state for diagnostics: attested / changed / lost / new / absent.</summary>
    public string EkStatus { get; init; } = "";

    /// <summary>Human-readable notes about anything that changed unexpectedly.</summary>
    public IReadOnlyList<string> Anomalies { get; init; } = [];

    /// <summary>Per-field breakdown (dev diagnostics only).</summary>
    public IReadOnlyList<FieldContribution> Contributions { get; init; } = [];

    /// <summary>True when the baseline should be refreshed to the current vector (rolling re-bind).</summary>
    public bool ShouldRebind { get; init; }
}
