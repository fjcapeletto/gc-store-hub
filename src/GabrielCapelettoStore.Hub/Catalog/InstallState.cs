using System;
using System.Collections.Generic;

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// Which apps are installed locally, at what version, and how to launch them. Absence of an id
/// means "not installed". Drives NEW (only for not-installed) vs UPDATE (installed, newer version
/// available) vs Open (installed and up to date).
/// </summary>
public sealed class InstallState
{
    /// <summary>Per app id → the installed record.</summary>
    public Dictionary<string, InstalledEntry> Apps { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>A locally installed app: version + where/how to launch it.</summary>
public sealed class InstalledEntry
{
    public string Version { get; set; } = "";
    public string EntryExe { get; set; } = "";
    public string InstallDir { get; set; } = "";
}

/// <summary>Persists and retrieves the local install registry.</summary>
public interface IInstallStateStore
{
    InstallState Load();

    void Save(InstallState state);
}
