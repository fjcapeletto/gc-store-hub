using System;
using System.Collections.Generic;
using System.Linq;

namespace GabrielCapelettoStore.Hub.Identity;

/// <summary>
/// Decides whether a collected signal is <b>distinctive</b> to this device (a real,
/// device-specific value) or a <b>shared default</b> — a factory placeholder that many
/// boards ship identically ("Default string", AMI defaults, all-zero/all-FF UUIDs).
///
/// A shared default is still a REAL, reported value — it is just not usable to tell one
/// device from another, so it must not anchor UNIQUENESS. It can still corroborate
/// CONSISTENCY (it should stay stable), and a field can graduate from shared-default to
/// distinctive if a real value is written to it later (e.g. an asset tag stamped at
/// refurb). Classification is therefore per-VALUE and re-evaluated every check — never a
/// permanent blacklist of the field. Bias: under-flag rather than over-flag, so a genuine
/// value is never discarded.
/// </summary>
public static class FingerprintHygiene
{
    // Exact-match (case-insensitive) factory placeholders, kept deliberately conservative.
    private static readonly HashSet<string> SharedDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        "default string", "to be filled by o.e.m.", "to be filled by oem",
        "system serial number", "system manufacturer", "system product name",
        "none", "n/a", "not specified", "not available", "unknown",
        "oem", "o.e.m.", "0", "00000000",
        "alaska", "a_m_i_", // AMI system defaults this dev machine reports
    };

    private static readonly HashSet<string> SharedDefaultUuids = new(StringComparer.OrdinalIgnoreCase)
    {
        "00000000-0000-0000-0000-000000000000",
        "ffffffff-ffff-ffff-ffff-ffffffffffff",
        "03000200-0400-0500-0006-000700080009", // common AMI default
    };

    /// <summary>True if a generic string signal is distinctive (a real value, not a shared default).</summary>
    public static bool IsDistinctive(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        return trimmed.Length >= 2 && !SharedDefaults.Contains(trimmed);
    }

    /// <summary>True if a UUID is distinctive (not all-zero, all-FF, or a known shared default).</summary>
    public static bool IsDistinctiveUuid(string? value)
    {
        if (!IsDistinctive(value))
        {
            return false;
        }

        var trimmed = value!.Trim();
        if (SharedDefaultUuids.Contains(trimmed))
        {
            return false;
        }

        var hex = trimmed.Replace("-", "");
        var allZero = hex.All(c => c == '0');
        var allF = hex.All(c => c is 'f' or 'F');
        return !allZero && !allF;
    }

    /// <summary>True if a MAC is distinctive (not empty, a shared default, or all-zero).</summary>
    public static bool IsDistinctiveMac(string? value)
    {
        if (!IsDistinctive(value))
        {
            return false;
        }

        var hex = value!.Replace(":", "").Replace("-", "");
        return !hex.All(c => c == '0');
    }
}
