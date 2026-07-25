using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace GabrielCapelettoStore.Hub.Update;

/// <summary>Self-update lifecycle, surfaced so the UI can show it (checking → downloading → ready).</summary>
public enum UpdateState
{
    None,        // no update / not an install / not configured
    Checking,
    Downloading,
    Ready,       // downloaded and staged; a restart applies it
}

public interface IUpdateService
{
    UpdateState State { get; }

    /// <summary>Version of the pending update (when Downloading/Ready), for display.</summary>
    string? NewVersion { get; }

    /// <summary>Raised (on a threadpool thread) whenever <see cref="State"/> changes.</summary>
    event Action? StateChanged;

    /// <summary>Best-effort background check + download from the release feed.</summary>
    Task CheckAsync();

    /// <summary>Apply the staged update and relaunch now (user asked).</summary>
    void ApplyAndRestart();

    /// <summary>Apply the staged update after the app exits (called on Quit).</summary>
    void ApplyOnExit();
}

/// <summary>
/// Velopack-backed self-update. Prefers the GitHub Releases feed (public client binaries); falls
/// back to a static feed URL. Reports progress so the header can show it, instead of the old silent
/// background+apply-on-quit ritual.
/// </summary>
public sealed class UpdateService : IUpdateService
{
    private readonly IUpdateSource? _source;
    private UpdateManager? _manager;
    private UpdateInfo? _pending;

    public UpdateService(string? githubRepo, string? feedUrl)
    {
        if (!string.IsNullOrWhiteSpace(githubRepo))
        {
            _source = new GithubSource(githubRepo, accessToken: null, prerelease: false);
        }
        else if (!string.IsNullOrWhiteSpace(feedUrl))
        {
            _source = new SimpleWebSource(feedUrl);
        }
    }

    public UpdateState State { get; private set; } = UpdateState.None;
    public string? NewVersion { get; private set; }
    public event Action? StateChanged;

    private void Set(UpdateState state)
    {
        State = state;
        StateChanged?.Invoke();
    }

    public async Task CheckAsync()
    {
        if (_source is null)
        {
            return;
        }

        try
        {
            var manager = new UpdateManager(_source);
            if (!manager.IsInstalled)
            {
                return; // running from source / not a Velopack install — nothing to update.
            }

            Set(UpdateState.Checking);
            var info = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (info is null)
            {
                Set(UpdateState.None);
                return;
            }

            NewVersion = info.TargetFullRelease?.Version?.ToString();
            Set(UpdateState.Downloading);
            await manager.DownloadUpdatesAsync(info).ConfigureAwait(false);

            _manager = manager;
            _pending = info;
            Set(UpdateState.Ready);
        }
        catch
        {
            // Update checks are best-effort; a bad/unreachable feed must not affect the hub.
            Set(UpdateState.None);
        }
    }

    public void ApplyAndRestart()
    {
        if (_manager is not null && _pending is not null)
        {
            try
            {
                _manager.ApplyUpdatesAndRestart(_pending);
            }
            catch
            {
                // If applying fails, stay on the current version rather than crash.
            }
        }
    }

    public void ApplyOnExit()
    {
        if (_manager is not null && _pending is not null)
        {
            try
            {
                _manager.WaitExitThenApplyUpdates(_pending, silent: true, restart: false);
            }
            catch
            {
                // Applying an update must never block quitting.
            }
        }
    }
}

/// <summary>No update source: design time and catalog-only runs.</summary>
public sealed class NullUpdateService : IUpdateService
{
    public UpdateState State => UpdateState.None;
    public string? NewVersion => null;
    public event Action? StateChanged { add { } remove { } }
    public Task CheckAsync() => Task.CompletedTask;
    public void ApplyAndRestart() { }
    public void ApplyOnExit() { }
}
