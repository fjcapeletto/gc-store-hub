using System;
using System.Collections.Generic;

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// Which apps the user has installed, and at what version. Absence of an id means
/// "not installed". Drives NEW (only for not-installed) vs UPDATE (installed, newer
/// version available) vs no badge (installed and up to date).
/// </summary>
public sealed class InstallState
{
    /// <summary>Per app id → installed version.</summary>
    public Dictionary<string, string> Installed { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>Persists and retrieves the local install registry.</summary>
public interface IInstallStateStore
{
    InstallState Load();

    void Save(InstallState state);
}
