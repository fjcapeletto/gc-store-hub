using System;
using System.IO;
using System.Text.Json;

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// Stores the install registry as JSON under the user's application-data folder
/// (%APPDATA%\GabrielCapelettoStore\installed.json on Windows). NOTE: today the
/// "install" is a stub that only records state — no real app is installed yet.
/// </summary>
public sealed class FileInstallStateStore : IInstallStateStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;

    public FileInstallStateStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GabrielCapelettoStore");
        _path = Path.Combine(directory, "installed.json");
    }

    public InstallState Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new InstallState();
            }

            return JsonSerializer.Deserialize<InstallState>(File.ReadAllText(_path), JsonOptions)
                   ?? new InstallState();
        }
        catch
        {
            return new InstallState();
        }
    }

    public void Save(InstallState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(state, JsonOptions));
        }
        catch
        {
            // Best-effort.
        }
    }
}

/// <summary>No-op install store used only by the XAML previewer.</summary>
public sealed class NullInstallStateStore : IInstallStateStore
{
    public InstallState Load() => new();

    public void Save(InstallState state)
    {
    }
}
