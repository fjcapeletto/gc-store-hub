using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GabrielCapelettoStore.Hub.Catalog;

/// <summary>
/// Caches the last-good catalog as JSON under the user's application-data folder
/// (%APPDATA%\GabrielCapelettoStore\catalog-cache.json). Best-effort.
/// </summary>
public sealed class FileCatalogCache : ICatalogCache
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower) },
    };

    private readonly string _path;

    public FileCatalogCache()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GabrielCapelettoStore");
        _path = Path.Combine(directory, "catalog-cache.json");
    }

    public CachedCatalog? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<CachedCatalog>(File.ReadAllText(_path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(CatalogManifest manifest, DateTimeOffset syncedAt)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var cached = new CachedCatalog { Manifest = manifest, SyncedAt = syncedAt };
            File.WriteAllText(_path, JsonSerializer.Serialize(cached, JsonOptions));
        }
        catch
        {
            // Best-effort.
        }
    }
}

/// <summary>No-op cache used only by the XAML previewer.</summary>
public sealed class NullCatalogCache : ICatalogCache
{
    public CachedCatalog? Load() => null;

    public void Save(CatalogManifest manifest, DateTimeOffset syncedAt)
    {
    }
}
