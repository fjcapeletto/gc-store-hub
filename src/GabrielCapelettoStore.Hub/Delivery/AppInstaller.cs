using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GabrielCapelettoStore.Hub.Delivery;

/// <summary>An app installed locally: where it lives and how to launch it.</summary>
public sealed record InstalledApp
{
    public required string AppId { get; init; }
    public required string Version { get; init; }
    public required string EntryExe { get; init; }
    public required string InstallDir { get; init; }
}

public interface IAppInstaller
{
    /// <summary>Download → verify sha-256 → extract → validate app.json → install into the versioned dir.</summary>
    Task<InstalledApp> InstallAsync(DeliveryResponse delivery, CancellationToken cancellationToken = default);

    /// <summary>Launch the app's entry exe from its install folder. (Caller gates on the lease first.)</summary>
    void Launch(InstalledApp app);

    /// <summary>Remove the app locally — deletes all versions of it.</summary>
    void Uninstall(string appId);
}

/// <summary>
/// Installs clear-phase zip packages under %LocalAppData%\GabrielCapelettoStore\apps\{appId}\{version}\.
/// Per-user, no admin, portable — install/update/uninstall are folder operations.
/// </summary>
public sealed class AppInstaller : IAppInstaller
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _appsRoot;

    public AppInstaller(HttpClient http)
    {
        _http = http;
        _appsRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GabrielCapelettoStore", "apps");
    }

    public async Task<InstalledApp> InstallAsync(DeliveryResponse delivery, CancellationToken cancellationToken = default)
    {
        var tempZip = Path.Combine(Path.GetTempPath(), $"gcstore-{Guid.NewGuid():N}.zip");
        var tempExtract = Path.Combine(Path.GetTempPath(), $"gcstore-{Guid.NewGuid():N}");

        try
        {
            // 1) download the (clear) zip from the short-lived signed URL.
            await using (var src = await _http.GetStreamAsync(delivery.Package.Url, cancellationToken).ConfigureAwait(false))
            await using (var dst = File.Create(tempZip))
            {
                await src.CopyToAsync(dst, cancellationToken).ConfigureAwait(false);
            }

            // 2) integrity: verify sha-256 against the (TLS-authenticated) delivery response.
            var actual = await Sha256HexAsync(tempZip, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, delivery.Package.Hash.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Package hash mismatch — refusing to install.");
            }

            // 3) extract and read the self-describing manifest.
            ZipFile.ExtractToDirectory(tempZip, tempExtract);
            var manifestPath = Path.Combine(tempExtract, "app.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException("Package has no app.json at its root.");
            }

            var manifest = JsonSerializer.Deserialize<AppManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false), Json)
                ?? throw new InvalidOperationException("Unreadable app.json.");

            if (!string.Equals(manifest.AppId, delivery.AppId, StringComparison.Ordinal)
                || !string.Equals(manifest.Version, delivery.Version, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("app.json appId/version does not match the delivery.");
            }

            var entryPath = Path.Combine(tempExtract, manifest.EntryExe);
            if (!File.Exists(entryPath))
            {
                throw new InvalidOperationException($"Entry exe '{manifest.EntryExe}' not found in the package.");
            }

            // 4) place into the versioned install dir (replace any existing same-version install).
            var installDir = Path.Combine(_appsRoot, delivery.AppId, delivery.Version);
            if (Directory.Exists(installDir))
            {
                Directory.Delete(installDir, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(installDir)!);
            Directory.Move(tempExtract, installDir);
            tempExtract = null!; // moved — don't clean it up below

            return new InstalledApp
            {
                AppId = delivery.AppId,
                Version = delivery.Version,
                EntryExe = manifest.EntryExe,
                InstallDir = installDir,
            };
        }
        finally
        {
            TryDeleteFile(tempZip);
            if (tempExtract is not null)
            {
                TryDeleteDir(tempExtract);
            }
        }
    }

    public void Launch(InstalledApp app)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(app.InstallDir, app.EntryExe),
            WorkingDirectory = app.InstallDir,
            UseShellExecute = true,
        });
    }

    public void Uninstall(string appId)
    {
        var dir = Path.Combine(_appsRoot, appId);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task<string> Sha256HexAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private static void TryDeleteDir(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch { /* best effort */ }
    }
}

public sealed class NullAppInstaller : IAppInstaller
{
    public Task<InstalledApp> InstallAsync(DeliveryResponse delivery, CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
    public void Launch(InstalledApp app) { }
    public void Uninstall(string appId) { }
}
