using System;
using Microsoft.Win32;

namespace GabrielCapelettoStore.Hub.Startup;

/// <summary>
/// Registers the installed hub to launch at Windows sign-in via a per-user Run key (HKCU\...\Run —
/// no admin). The hub boots straight to the tray, so it's unobtrusive. Auto-start is mandatory (no
/// user opt-out) and re-applied on every launch, so an already-installed client picks it up
/// automatically the first time the self-updated version runs.
/// </summary>
public interface IStartupService
{
    /// <summary>Idempotently ensure the Run key (no-op unless this is a real installed Windows hub).</summary>
    void EnsureRegistered();
}

public sealed class WindowsStartupService : IStartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GabrielCapelettoStore";

    private readonly string _launcherPath;

    public WindowsStartupService(string launcherPath) => _launcherPath = launcherPath;

    public void EnsureRegistered()
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(_launcherPath))
        {
            return;
        }

        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            run?.SetValue(ValueName, $"\"{_launcherPath}\"");
        }
        catch
        {
            // Best effort — auto-start is a convenience, never worth crashing the hub.
        }
    }

    /// <summary>Remove the Run key (best effort) — call from the uninstall hook so we don't leave a
    /// dead auto-start entry pointing at a deleted exe.</summary>
    public static void RemoveAutostart()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            run?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch
        {
            // best effort
        }
    }
}

public sealed class NullStartupService : IStartupService
{
    public void EnsureRegistered() { }
}
