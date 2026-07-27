using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace GabrielCapelettoStore.Hub.Icons;

/// <summary>
/// Resolves publisher-supplied app icons (the catalog's <c>iconUrl</c>) to decoded bitmaps, backed by
/// an on-disk cache. The URL is the icon's version token (see contract/app-icons.md): the cache is
/// keyed by URL, so a changed URL is a cache miss and re-fetches — a marketing refresh with no app
/// version bump. Everything is best-effort: a missing/broken/oversized/undecodable asset yields null
/// and the tile falls back to the glyph vocabulary. Never throws to the caller.
/// </summary>
public interface IAppIconCache
{
    /// <summary>The decoded bitmap for this URL if already loaded in memory; null otherwise (no I/O).</summary>
    Bitmap? Get(string iconUrl);

    /// <summary>
    /// Ensures each URL is loaded (disk, then network), populating the in-memory map. Raises
    /// <see cref="Changed"/> once if any new icon became available so the shelf can re-render.
    /// </summary>
    Task WarmAsync(IEnumerable<string> iconUrls, CancellationToken cancellationToken = default);

    /// <summary>Fired (off the UI thread) when one or more icons finished loading during a warm.</summary>
    event Action? Changed;
}

public sealed class FileAppIconCache : IAppIconCache
{
    // The contract caps assets at 256 KB; accept up to 2 MB defensively before giving up.
    private const long MaxBytes = 2 * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly string _root;
    private readonly ConcurrentDictionary<string, Bitmap> _memory = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);

    public event Action? Changed;

    public FileAppIconCache(HttpClient http)
    {
        _http = http;
        _root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GabrielCapelettoStore", "icons");
    }

    public Bitmap? Get(string iconUrl)
        => !string.IsNullOrWhiteSpace(iconUrl) && _memory.TryGetValue(iconUrl, out var bmp) ? bmp : null;

    public async Task WarmAsync(IEnumerable<string> iconUrls, CancellationToken cancellationToken = default)
    {
        var loadedAny = false;

        foreach (var url in iconUrls)
        {
            if (string.IsNullOrWhiteSpace(url) || _memory.ContainsKey(url))
            {
                continue;
            }

            // Coalesce concurrent warms for the same URL (e.g. two rapid catalog polls).
            if (!_inFlight.TryAdd(url, 0))
            {
                continue;
            }

            try
            {
                var bmp = await LoadOneAsync(url, cancellationToken).ConfigureAwait(false);
                if (bmp is not null && _memory.TryAdd(url, bmp))
                {
                    loadedAny = true;
                }
            }
            catch
            {
                // Best effort: a failed icon leaves the tile on its glyph fallback. It stays out of
                // the memory map, so a later catalog poll retries (e.g. once the server publishes it).
            }
            finally
            {
                _inFlight.TryRemove(url, out _);
            }
        }

        if (loadedAny)
        {
            Changed?.Invoke();
        }
    }

    private async Task<Bitmap?> LoadOneAsync(string url, CancellationToken cancellationToken)
    {
        // Only http(s); ignore anything exotic (data:, file:, ...).
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return null;
        }

        var path = Path.Combine(_root, CacheFileName(url));

        // 1) disk cache (keyed by URL) — the common path after first encounter.
        if (File.Exists(path))
        {
            var cached = TryDecode(path);
            if (cached is not null)
            {
                return cached;
            }

            TryDelete(path); // corrupt cache entry — fall through to a fresh fetch.
        }

        // 2) fetch, size-guard, and decode. SVG/garbage simply fails to decode → null → glyph.
        var bytes = await FetchGuardedAsync(uri, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        Bitmap decoded;
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            decoded = new Bitmap(ms);
        }
        catch
        {
            return null; // not a raster image the hub can render.
        }

        // 3) persist to the disk cache (best effort; a failed write just means we re-fetch next time).
        try
        {
            Directory.CreateDirectory(_root);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // ignore — the in-memory bitmap is still good for this session.
        }

        return decoded;
    }

    private async Task<byte[]?> FetchGuardedAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await _http
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        if (response.Content.Headers.ContentLength is { } declared && declared > MaxBytes)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxBytes)
            {
                return null; // server lied about (or omitted) Content-Length; stop reading.
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static Bitmap? TryDecode(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return new Bitmap(fs);
        }
        catch
        {
            return null;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* best effort */ }
    }

    /// <summary>A filesystem-safe, collision-resistant name derived from the URL (its version token).</summary>
    private static string CacheFileName(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        var sb = new StringBuilder(64);
        foreach (var b in hash)
        {
            sb.Append(b.ToString("x2"));
        }

        return sb.Append(".img").ToString();
    }
}

/// <summary>No-op cache for design-time / catalog-only contexts.</summary>
public sealed class NullAppIconCache : IAppIconCache
{
    public event Action? Changed { add { } remove { } }

    public Bitmap? Get(string iconUrl) => null;

    public Task WarmAsync(IEnumerable<string> iconUrls, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
