using System;
using System.IO;
using System.Text.Json;

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// Stores catalog-observation state as JSON under the user's application-data folder
/// (%APPDATA%\GabrielCapelettoStore\catalog-state.json on Windows). Best-effort: a
/// missing/unreadable file means "no baseline yet", a failed save just re-derives later.
/// </summary>
public sealed class FileCatalogStateStore : ICatalogStateStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;

    public FileCatalogStateStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GabrielCapelettoStore");
        _path = Path.Combine(directory, "catalog-state.json");
    }

    public CatalogObservationState? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CatalogObservationState>(File.ReadAllText(_path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(CatalogObservationState state)
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

/// <summary>No-op observation store used only by the XAML previewer.</summary>
public sealed class NullCatalogStateStore : ICatalogStateStore
{
    public CatalogObservationState? Load() => null;

    public void Save(CatalogObservationState state)
    {
    }
}
