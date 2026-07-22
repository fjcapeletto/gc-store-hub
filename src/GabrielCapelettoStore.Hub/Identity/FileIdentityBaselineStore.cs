using System;
using System.IO;
using System.Text.Json;

namespace GabrielCapelettoStore.Hub.Identity;

/// <summary>
/// Stores the device baseline as JSON under the user's application-data folder
/// (%APPDATA%\GabrielCapelettoStore\device-baseline.json). Best-effort.
/// </summary>
public sealed class FileIdentityBaselineStore : IIdentityBaselineStore
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;

    public FileIdentityBaselineStore()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GabrielCapelettoStore");
        _path = Path.Combine(directory, "device-baseline.json");
    }

    public DeviceBaseline? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<DeviceBaseline>(File.ReadAllText(_path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(DeviceBaseline baseline)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(baseline, JsonOptions));
        }
        catch
        {
            // Best-effort.
        }
    }
}

/// <summary>No-op baseline store used only by the XAML previewer.</summary>
public sealed class NullIdentityBaselineStore : IIdentityBaselineStore
{
    public DeviceBaseline? Load() => null;

    public void Save(DeviceBaseline baseline)
    {
    }
}
