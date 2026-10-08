using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace TimeWidget.Services;

/// <summary>
/// Resolves album artwork for the media widget.
///
/// Many players never publish a usable SMTC thumbnail. ECHO NEXT is a concrete case:
/// it drives SystemMediaTransportControls from a separate native helper process and only
/// feeds it a cover path derived from the *local library* cover id
/// (<c>coverPath: resolveCoverPath(track?.coverId ?? null)</c>). Streaming sources such as
/// NetEase Cloud Music have no library entry, so coverId is null, the helper receives an
/// empty path and calls <c>updater.Thumbnail(nullptr)</c> - external readers observe a null
/// thumbnail forever, while locally played files still work.
///
/// ECHO does however still know the streaming cover URL and publishes it over its local
/// wallpaper-engine integration server as <c>track.coverUrl</c>, e.g.
/// <c>echo-image://remote/https%3A%2F%2Fp3.music.126.net%2F...jpg%3Fparam%3D160y160?referer=https%3A%2F%2Fmusic.163.com%2F</c>.
/// This service decodes that wrapper and fetches the image directly, asking NetEase for a
/// larger variant than the 160x160 ECHO requested.
///
/// Resolution order:
///   1. SMTC thumbnail (authoritative when a player publishes one).
///   2. ECHO local integration server snapshot -> remote cover URL (streaming fix).
///   3. ECHO's materialized local cover cache (local-library playback).
/// </summary>
public sealed class CoverArtService : IDisposable
{
    /// <summary>ECHO NEXT advertises this AUMID through its SMTC host process.</summary>
    public const string EchoAppUserModelId = "app.echo.next";

    /// <summary>Port ECHO NEXT's wallpaper-engine integration server listens on.</summary>
    private const int EchoSnapshotPort = 47668;

    /// <summary>Snapshot JSON should always be tiny; guard against a wedged endpoint.</summary>
    private const int SnapshotTimeoutMs = 1500;

    /// <summary>Remote cover download budget.</summary>
    private const int CoverDownloadTimeoutMs = 6000;

    /// <summary>Refuse absurd payloads from a misbehaving endpoint.</summary>
    private const int MaxCoverBytes = 16 * 1024 * 1024;

    /// <summary>
    /// NetEase image variants to try, best first. NetEase honours the <c>param=WxH</c>
    /// query value, so we can upgrade ECHO's 160x160 request to real artwork.
    /// </summary>
    private static readonly string[] NeteaseSizePreferences =
    [
        "param=500y500",
        "param=300y300",
        "param=160y160",
    ];

    /// <summary>ECHO's own materialized cover cache, written for local-library tracks.</summary>
    private static readonly string[] EchoCoverCacheCandidates =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECHO NEXT", "smtc-covers"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ECHO", "smtc-covers"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECHO NEXT", "smtc-covers"),
    ];

    /// <summary>How long a resolved cover stays fresh for the same media key.</summary>
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(20);

    /// <summary>
    /// A freshly written cache file may still be mid-flush; ignore files younger than this
    /// so we don't try to decode a partial PNG.
    /// </summary>
    private static readonly TimeSpan MinCacheFileAge = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// How long a failed remote lookup is remembered for a given media key, so a persistent
    /// failure doesn't retry on every 1s refresh tick.
    /// </summary>
    private static readonly TimeSpan NegativeCacheLifetime = TimeSpan.FromSeconds(15);

    private readonly object _gate = new();
    private readonly HttpClient _http;

    private string? _cachedMediaKey;
    private BitmapSource? _cachedImage;
    private DateTime _cachedAtUtc = DateTime.MinValue;
    private string? _cachedSource;

    /// <summary>Media keys whose remote cover fetch recently failed, with the failure time.</summary>
    private readonly Dictionary<string, DateTime> _remoteFailures = new(StringComparer.Ordinal);

    public CoverArtService()
    {
        SocketsHttpHandler handler = new()
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(SnapshotTimeoutMs),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.All,
        };

        _http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMilliseconds(CoverDownloadTimeoutMs),
        };
    }

    /// <summary>Diagnostic description of where the last cover came from.</summary>
    public string LastSourceDescription { get; private set; } = "none";

    /// <summary>
    /// Attempts to resolve artwork for the current track.
    /// Returns null when no channel produced an image; callers should keep the previous art.
    /// </summary>
    /// <param name="thumbnail">Thumbnail reference handed out by SMTC (may be null).</param>
    /// <param name="mediaKey">Stable identity of the current track.</param>
    /// <param name="sourceAumid">SourceAppUserModelId of the active SMTC session.</param>
    public BitmapSource? TryResolve(
        IRandomAccessStreamReference? thumbnail,
        string mediaKey,
        string? sourceAumid)
    {
        if (string.IsNullOrEmpty(mediaKey))
        {
            return null;
        }

        // 1) Best channel: the thumbnail the player itself publishes.
        BitmapSource? image = TryLoadFromThumbnail(thumbnail);
        if (image is not null)
        {
            lock (_gate)
            {
                Remember(mediaKey, image, "smtc");
            }

            return image;
        }

        lock (_gate)
        {
            if (string.Equals(_cachedMediaKey, mediaKey, StringComparison.Ordinal)
                && _cachedImage is not null
                && DateTime.UtcNow - _cachedAtUtc < CacheLifetime)
            {
                // Still probe SMTC on later ticks: a player may publish art late.
                return _cachedImage;
            }
        }

        // 2) ECHO streaming fallback: ask its local integration server for the track's
        //    remote cover URL and download it ourselves.
        if (IsEchoSource(sourceAumid) && !IsRemoteLookupSuppressed(mediaKey))
        {
            image = TryLoadFromEchoSnapshot(mediaKey);
            if (image is not null)
            {
                lock (_gate)
                {
                    Remember(mediaKey, image, "echo-remote");
                }

                return image;
            }
        }

        // 3) Local-library playback: read the cover ECHO materialized into its own cache.
        if (IsEchoSource(sourceAumid))
        {
            image = TryLoadFromEchoCache();
            if (image is not null)
            {
                lock (_gate)
                {
                    Remember(mediaKey, image, "echo-cache");
                }

                return image;
            }
        }

        LastSourceDescription = "unresolved";
        return null;
    }

    // ---------------------------------------------------------------- ECHO snapshot

    /// <summary>
    /// Queries ECHO's local integration server for the current track and downloads its cover.
    /// </summary>
    private BitmapSource? TryLoadFromEchoSnapshot(string mediaKey)
    {
        string? rawCoverUrl = TryReadSnapshotCoverUrl();
        if (string.IsNullOrWhiteSpace(rawCoverUrl))
        {
            SuppressRemoteLookup(mediaKey);
            return null;
        }

        if (!TryDecodeEchoImageUrl(rawCoverUrl, out string? imageUrl, out string? referer))
        {
            SuppressRemoteLookup(mediaKey);
            return null;
        }

        foreach (string candidate in BuildCoverUrlCandidates(imageUrl!))
        {
            BitmapSource? image = TryDownloadImage(candidate, referer);
            if (image is not null)
            {
                return image;
            }
        }

        SuppressRemoteLookup(mediaKey);
        return null;
    }

    /// <summary>Reads <c>track.coverUrl</c> from the ECHO snapshot endpoint, if it is up.</summary>
    private string? TryReadSnapshotCoverUrl()
    {
        try
        {
            using CancellationTokenSource cts = new(SnapshotTimeoutMs);
            using HttpResponseMessage response = _http
                .GetAsync($"http://127.0.0.1:{EchoSnapshotPort}/snapshot", cts.Token)
                .GetAwaiter()
                .GetResult();

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = response.Content.ReadAsStringAsync(cts.Token).GetAwaiter().GetResult();
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.TryGetProperty("track", out JsonElement track)
                && track.ValueKind == JsonValueKind.Object
                && track.TryGetProperty("coverUrl", out JsonElement cover)
                && cover.ValueKind == JsonValueKind.String)
            {
                return cover.GetString();
            }
        }
        catch (Exception ex)
        {
            // ECHO not running / endpoint moved on: this is an expected, benign outcome.
            System.Diagnostics.Debug.WriteLine($"ECHO snapshot read failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Unwraps ECHO's <c>echo-image://remote/&lt;urlencoded&gt;?referer=...</c> indirection.
    /// </summary>
    internal static bool TryDecodeEchoImageUrl(string raw, out string? imageUrl, out string? referer)
    {
        imageUrl = null;
        referer = null;

        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        // Plain http(s) URLs pass straight through.
        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            imageUrl = raw;
            return true;
        }

        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri)
            || !uri.Scheme.Equals("echo-image", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Referer travels as a real query parameter; the payload itself is the escaped path.
        string query = uri.Query.TrimStart('?');
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            if (pair.AsSpan(0, eq).Equals("referer", StringComparison.OrdinalIgnoreCase))
            {
                referer = Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
        }

        string payload = uri.AbsolutePath.TrimStart('/');
        if (string.IsNullOrEmpty(payload))
        {
            return false;
        }

        string decoded = Uri.UnescapeDataString(payload);
        if (!decoded.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !decoded.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        imageUrl = decoded;
        return true;
    }

    /// <summary>
    /// Expands a cover URL into the variants worth trying, upgrading NetEase's low-res
    /// request to a larger render of the same artwork.
    /// </summary>
    internal static IEnumerable<string> BuildCoverUrlCandidates(string imageUrl)
    {
        if (!IsNeteaseImageUrl(imageUrl))
        {
            yield return imageUrl;
            yield break;
        }

        // Strip any existing "param=" so we control the requested size.
        int queryIndex = imageUrl.IndexOf('?');
        string baseUrl = queryIndex >= 0 ? imageUrl[..queryIndex] : imageUrl;

        foreach (string size in NeteaseSizePreferences)
        {
            yield return $"{baseUrl}?{size}";
        }

        yield return imageUrl;
    }

    private static bool IsNeteaseImageUrl(string url)
        => url.Contains("music.126.net", StringComparison.OrdinalIgnoreCase)
            || url.Contains("126.net", StringComparison.OrdinalIgnoreCase)
            || url.Contains("music.163.com", StringComparison.OrdinalIgnoreCase);

    private BitmapSource? TryDownloadImage(string url, string? referer)
    {
        try
        {
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(referer))
            {
                // NetEase's CDN rejects requests without a music.163.com referer.
                request.Headers.Referrer = new Uri(referer!);
            }

            request.Headers.UserAgent.ParseAdd("Mozilla/5.0");

            using CancellationTokenSource cts = new(CoverDownloadTimeoutMs);
            using HttpResponseMessage response = _http
                .Send(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            if (response.Content.Headers.ContentLength is long length && length > MaxCoverBytes)
            {
                return null;
            }

            using Stream stream = response.Content.ReadAsStream(cts.Token);
            using MemoryStream buffer = new();
            stream.CopyTo(buffer);

            if (buffer.Length == 0 || buffer.Length > MaxCoverBytes)
            {
                return null;
            }

            buffer.Position = 0;
            return Decode(buffer);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cover download failed ({url}): {ex.Message}");
            return null;
        }
    }

    private bool IsRemoteLookupSuppressed(string mediaKey)
    {
        lock (_gate)
        {
            if (!_remoteFailures.TryGetValue(mediaKey, out DateTime failedAt))
            {
                return false;
            }

            if (DateTime.UtcNow - failedAt < NegativeCacheLifetime)
            {
                return true;
            }

            _remoteFailures.Remove(mediaKey);
            return false;
        }
    }

    private void SuppressRemoteLookup(string mediaKey)
    {
        lock (_gate)
        {
            if (_remoteFailures.Count > 64)
            {
                _remoteFailures.Clear();
            }

            _remoteFailures[mediaKey] = DateTime.UtcNow;
        }
    }

    // ------------------------------------------------------------- ECHO cover cache

    /// <summary>Decodes a cover straight out of the cached ECHO directory, newest file first.</summary>
    private BitmapSource? TryLoadFromEchoCache()
    {
        foreach (string directory in EchoCoverCacheCandidates)
        {
            string? newest = FindNewestCoverFile(directory);
            if (newest is null)
            {
                continue;
            }

            BitmapSource? image = TryLoadFromFile(newest);
            if (image is not null)
            {
                return image;
            }
        }

        return null;
    }

    private static string? FindNewestCoverFile(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return null;
            }

            DirectoryInfo info = new(directory);
            string? best = null;
            DateTime bestStamp = DateTime.MinValue;
            DateTime now = DateTime.UtcNow;

            foreach (FileInfo file in info.EnumerateFiles("*.png", SearchOption.TopDirectoryOnly))
            {
                // "*-default.png" is ECHO's placeholder, not real artwork.
                if (file.Name.EndsWith("-default.png", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (file.Length < 1024)
                {
                    continue;
                }

                if (now - file.LastWriteTimeUtc < MinCacheFileAge)
                {
                    continue;
                }

                if (file.LastWriteTimeUtc > bestStamp)
                {
                    bestStamp = file.LastWriteTimeUtc;
                    best = file.FullName;
                }
            }

            return best;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ECHO cover cache scan failed: {ex.Message}");
            return null;
        }
    }

    // ------------------------------------------------------------------- decoding

    private static BitmapSource? TryLoadFromThumbnail(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
        {
            return null;
        }

        try
        {
            using IRandomAccessStreamWithContentType stream = thumbnail.OpenReadAsync().AsTask().GetAwaiter().GetResult();
            if (stream.Size == 0)
            {
                return null;
            }

            using Stream readStream = stream.AsStreamForRead();
            return Decode(readStream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SMTC thumbnail decode failed: {ex.Message}");
            return null;
        }
    }

    private static BitmapSource? TryLoadFromFile(string path)
    {
        try
        {
            // Read fully into memory so ECHO can rotate the cache file underneath us.
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length == 0)
            {
                return null;
            }

            using MemoryStream stream = new(bytes, writable: false);
            return Decode(stream);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Cover file decode failed ({path}): {ex.Message}");
            return null;
        }
    }

    private static BitmapSource Decode(Stream stream)
    {
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static bool IsEchoSource(string? sourceAumid)
    {
        if (string.IsNullOrWhiteSpace(sourceAumid))
        {
            return false;
        }

        return sourceAumid.Contains(EchoAppUserModelId, StringComparison.OrdinalIgnoreCase)
            || sourceAumid.Contains("echo", StringComparison.OrdinalIgnoreCase);
    }

    private void Remember(string mediaKey, BitmapSource image, string source)
    {
        _cachedMediaKey = mediaKey;
        _cachedImage = image;
        _cachedAtUtc = DateTime.UtcNow;
        _cachedSource = source;
        LastSourceDescription = source;
    }

    /// <summary>Drops any cached artwork; used when playback stops or the session changes.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _cachedMediaKey = null;
            _cachedImage = null;
            _cachedAtUtc = DateTime.MinValue;
            _cachedSource = null;
            _remoteFailures.Clear();
            LastSourceDescription = "none";
        }
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
