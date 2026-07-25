using System;
using System.IO;
using System.Text.Json;

namespace GabrielCapelettoStore.Hub.Entitlement;

/// <summary>
/// Locally persisted device state: the hub-generated device id, the license key (baked at
/// factory or typed by the user after approval), and the pending access-request details.
/// Lives in %APPDATA%\GabrielCapelettoStore\device.json for now — production should move this
/// to a machine-wide, DPAPI-protected store (see device-identity-provisioning.md).
/// </summary>
public sealed class DeviceState
{
    public string? DeviceId { get; set; }
    public string? License { get; set; }
    public string? RequestName { get; set; }
    public string? RequestEmail { get; set; }
    public bool AccessRequested { get; set; }
}

public interface IDeviceStore
{
    DeviceState Load();
    void Save(DeviceState state);
}

public sealed class FileDeviceStore : IDeviceStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _path;

    public FileDeviceStore()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GabrielCapelettoStore");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "device.json");
    }

    public DeviceState Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<DeviceState>(File.ReadAllText(_path), Json) ?? new DeviceState()
                : new DeviceState();
        }
        catch
        {
            return new DeviceState();
        }
    }

    public void Save(DeviceState state)
    {
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(state, Json));
        }
        catch
        {
            // Persisting device state is best-effort; a failure must not crash the hub.
        }
    }
}

public sealed class NullDeviceStore : IDeviceStore
{
    private DeviceState _state = new();
    public DeviceState Load() => _state;
    public void Save(DeviceState state) => _state = state;
}
