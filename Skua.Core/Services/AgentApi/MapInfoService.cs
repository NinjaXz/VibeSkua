using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Utils;

namespace Skua.Core.Services.AgentApi;

/// <summary>
/// Settings for the map reader: where map files and parsed results are kept,
/// and how long each step may take.
/// </summary>
public sealed class MapInfoOptions
{
    /// <summary>
    /// Bump when the parser changes what it can find, so old caches are ignored
    /// instead of serving an NPC list that is missing a whole feature.
    /// </summary>
    public const int CurrentCacheVersion = 2;

    public int CacheVersion { get; init; } = CurrentCacheVersion;

    /// <summary>Root for cached map files and parsed results, inside the client's own folder.</summary>
    public string CacheRoot { get; init; } = Path.Combine(ClientFileSources.SkuaDIR, "cache");

    public string MapsHost { get; init; } = "https://game.aq.com/game/gamefiles/";

    /// <summary>A map SWF smaller than this is an error page, not a map.</summary>
    public int MinSwfBytes { get; init; } = 1024;

    public int DownloadTimeoutSeconds { get; init; } = 90;

    /// <summary>FFDec on a large map takes seconds, not milliseconds.</summary>
    public int DecompileTimeoutSeconds { get; init; } = 120;
}

/// <summary>
/// Gets the map SWF for a map file path on local disk, downloading it once.
/// </summary>
public interface IMapSwfDownloader
{
    Task<string> EnsureLocalAsync(string filePath, CancellationToken ct);
}

/// <summary>
/// Decompiles a map SWF into a folder of ActionScript text.
///
/// This is the one step that needs Java and FFDec, so it sits behind an
/// interface: the parser is testable without either.
/// </summary>
public interface IMapDecompiler
{
    /// <summary>Returns the folder holding the decompiled scripts.</summary>
    Task<string> DecompileAsync(string swfPath, CancellationToken ct);
}

/// <summary>
/// Reads what is in the loaded map: NPCs, their cells, quests, shops and exits.
///
/// Steps: get the map SWF (cache first), decompile it with FFDec, parse the
/// timeline and the NPC classes, then cache the result per map file. The first
/// request for a map takes a few seconds, later requests read the cache.
/// </summary>
public sealed class MapInfoService : IMapInfoReader, IDisposable
{
    private static readonly JsonSerializerOptions CacheJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IScriptInterface _bot;
    private readonly MapInfoOptions _options;
    private readonly IMapSwfDownloader _downloader;
    private readonly IMapDecompiler _decompiler;
    private readonly ILogService? _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public MapInfoService(
        IScriptInterface bot,
        IMapSwfDownloader downloader,
        IMapDecompiler decompiler,
        MapInfoOptions? options = null,
        ILogService? log = null)
    {
        _bot = bot;
        _downloader = downloader;
        _decompiler = decompiler;
        _options = options ?? new MapInfoOptions();
        _log = log;
    }

    public async Task<MapInfo> GetCurrentMapAsync(bool refresh, CancellationToken ct)
    {
        string mapName = Safe(() => _bot.Map.Name, string.Empty);
        string filePath = Safe(() => _bot.Map.FilePath, string.Empty);

        if (string.IsNullOrWhiteSpace(mapName) || string.IsNullOrWhiteSpace(filePath))
            throw new MapInfoException("map_not_loaded", "No map is loaded. Wait for the character to finish loading, then ask again.");

        // Only one map is read at a time: a second caller would repeat the same
        // download and the same decompile while the first one is still running.
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            int roomId = Safe(() => _bot.Map.RoomID, 0);
            List<string> cells = Safe(() => _bot.Map.Cells, null) ?? new List<string>();
            string cacheFile = CacheFilePath(filePath);

            if (!refresh && TryLoadCache(cacheFile, filePath, out MapInfo? cached) && cached is not null)
            {
                // The cell list comes from the running game, so refresh it even
                // when the NPC list came from cache.
                cached.Cells = cells;
                cached.RoomId = roomId;
                return cached;
            }

            string swf = await _downloader.EnsureLocalAsync(filePath, ct).ConfigureAwait(false);
            string scriptsRoot = await _decompiler.DecompileAsync(swf, ct).ConfigureAwait(false);

            MapInfo info;
            try
            {
                List<(int Frame, string Name)> labels = SwfFrameLabels.Read(swf);
                info = MapInfoParser.Parse(mapName, filePath, roomId, cells, scriptsRoot, labels);
            }
            catch (MapInfoException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new MapInfoException("map_parse_failed", ex.Message);
            }
            finally
            {
                TryDeleteDirectory(Path.GetDirectoryName(scriptsRoot));
            }

            SaveCache(cacheFile, filePath, info);
            return info;
        }
        finally
        {
            _gate.Release();
        }
    }

    private string CacheFilePath(string filePath)
    {
        string hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(Encoding.UTF8.GetBytes(filePath.ToLowerInvariant()))).ToLowerInvariant();
        return Path.Combine(_options.CacheRoot, "mapinfo", hash + ".json");
    }

    private bool TryLoadCache(string cacheFile, string filePath, out MapInfo? info)
    {
        info = null;
        if (!File.Exists(cacheFile))
            return false;

        try
        {
            MapCache? cache = JsonSerializer.Deserialize<MapCache>(File.ReadAllText(cacheFile), CacheJson);
            if (cache is null || cache.Version != _options.CacheVersion || cache.Map is null ||
                !string.Equals(cache.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                return false;

            cache.Map.FromCache = true;
            info = cache.Map;
            return true;
        }
        catch
        {
            // A cache file that cannot be read is simply no cache at all.
            return false;
        }
    }

    private void SaveCache(string cacheFile, string filePath, MapInfo info)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
            File.WriteAllText(cacheFile, JsonSerializer.Serialize(new MapCache
            {
                Version = _options.CacheVersion,
                FilePath = filePath,
                Map = info,
            }, CacheJson));
        }
        catch (Exception ex)
        {
            // The answer is already parsed; failing to cache it only costs time
            // on the next request.
            _log?.DebugLog($"Agent API: map cache write failed: {ex.Message}");
        }
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }

    private static void TryDeleteDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;
        try
        {
            Directory.Delete(directory, true);
        }
        catch
        {
            // A leftover temp folder is not worth failing the request.
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _gate.Dispose();
        if (_decompiler is IDisposable decompiler)
            decompiler.Dispose();
        if (_downloader is IDisposable downloader)
            downloader.Dispose();
    }

    private sealed class MapCache
    {
        public int Version { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public MapInfo? Map { get; set; }
    }
}

/// <summary>
/// Downloads a map SWF from the game CDN into the client's map cache, and
/// reuses a copy the client already fetched.
/// </summary>
public sealed class AqwMapSwfDownloader : IMapSwfDownloader, IDisposable
{
    private readonly MapInfoOptions _options;
    private readonly ILogService? _log;
    private readonly HttpClient? _http;
    private readonly Func<string, CancellationToken, Task<byte[]>> _fetch;
    private bool _disposed;

    public AqwMapSwfDownloader(
        MapInfoOptions? options = null,
        ILogService? log = null,
        Func<string, CancellationToken, Task<byte[]>>? fetch = null)
    {
        _options = options ?? new MapInfoOptions();
        _log = log;
        if (fetch is null)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(_options.DownloadTimeoutSeconds) };
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (VibeSkua map reader)");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://game.aq.com/game/");
            _fetch = (url, ct) => _http.GetByteArrayAsync(url, ct);
        }
        else
        {
            _fetch = fetch;
        }
    }

    public async Task<string> EnsureLocalAsync(string filePath, CancellationToken ct)
    {
        string relative = SafeRelativePath(filePath);
        string mapsRoot = Path.Combine(_options.CacheRoot, "maps");
        string target = Path.Combine(mapsRoot, relative);

        if (IsUsable(target))
            return target;

        // The script-side map tools cache by file name in the cache root. If one
        // already downloaded this map, do not download it a second time.
        string shared = Path.Combine(_options.CacheRoot, Path.GetFileName(relative));
        if (IsUsable(shared))
            return shared;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string url = DownloadUrl(filePath);

        _log?.DebugLog($"Agent API: downloading {url}");
        byte[] bytes;
        try
        {
            bytes = await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new MapInfoException("map_download_failed", $"{url}: {ex.Message}");
        }

        if (bytes.Length < _options.MinSwfBytes)
            throw new MapInfoException("map_download_failed", $"{url} returned {bytes.Length} bytes, which is too small to be a map.");

        await File.WriteAllBytesAsync(target, bytes, ct).ConfigureAwait(false);
        return target;
    }

    private bool IsUsable(string path) => File.Exists(path) && new FileInfo(path).Length >= _options.MinSwfBytes;

    private string DownloadUrl(string filePath)
    {
        if (filePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            filePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return filePath;

        string clean = filePath.Replace('\\', '/').TrimStart('/');
        string escaped = string.Join('/', clean.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        return clean.StartsWith("maps/", StringComparison.OrdinalIgnoreCase)
            ? _options.MapsHost + escaped
            : _options.MapsHost + "maps/" + escaped;
    }

    /// <summary>
    /// Turns a game-supplied path into a safe relative file name: no drive, no
    /// parent directory, always ending in .swf. The value comes from the game,
    /// so it is treated as untrusted input.
    /// </summary>
    internal static string SafeRelativePath(string filePath)
    {
        string[] parts = filePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p is not "." and not "..")
            .ToArray();

        if (parts.Length == 0)
            throw new MapInfoException("map_path_invalid", "The game returned an empty map file path.");

        string relative = Path.Combine(parts);
        if (!relative.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
            relative += ".swf";
        return relative;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _http?.Dispose();
    }
}

/// <summary>
/// Decompiles a map SWF with the FFDec jar the client ships, using its bundled
/// Java runtime when present.
/// </summary>
public sealed class FfdecMapDecompiler : IMapDecompiler, IDisposable
{
    private readonly MapInfoOptions _options;
    private readonly ILogService? _log;

    public FfdecMapDecompiler(MapInfoOptions? options = null, ILogService? log = null)
    {
        _options = options ?? new MapInfoOptions();
        _log = log;
    }

    public async Task<string> DecompileAsync(string swfPath, CancellationToken ct)
    {
        string? jar = FfdecLocator.FindJar();
        if (jar is null)
            throw new MapInfoException("ffdec_missing",
                "FFDec was not found next to the client. Expected FFDec/ffdec.jar (the packaged client includes it).");

        string output = Path.Combine(Path.GetTempPath(), "vibeskoa-map-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);

        ProcessStartInfo start = new()
        {
            FileName = FfdecLocator.FindJava(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-jar");
        start.ArgumentList.Add(jar);
        start.ArgumentList.Add("-export");
        start.ArgumentList.Add("script");
        start.ArgumentList.Add(output);
        start.ArgumentList.Add(swfPath);

        _log?.DebugLog($"Agent API: decompiling {Path.GetFileName(swfPath)}");
        Stopwatch watch = Stopwatch.StartNew();
        using Process process = new() { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            TryDelete(output);
            throw new MapInfoException("java_missing",
                $"Could not start Java to run FFDec: {ex.Message}. The packaged client bundles a Java runtime in FFDec/Runtime.");
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderr = process.StandardError.ReadToEndAsync(ct);

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.DecompileTimeoutSeconds));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // Already gone.
            }
            TryDelete(output);
            throw new MapInfoException("map_decompile_timeout",
                $"FFDec took longer than {_options.DecompileTimeoutSeconds} seconds on this map.");
        }

        string error = (await stderr.ConfigureAwait(false)).Trim();
        string scriptsRoot = Path.Combine(output, "scripts");
        if (!Directory.Exists(scriptsRoot))
        {
            string detail = error.Length > 0 ? error : (await stdout.ConfigureAwait(false)).Trim();
            TryDelete(output);
            throw new MapInfoException("map_decompile_failed",
                "FFDec did not produce map scripts." + (detail.Length == 0 ? string.Empty : " " + detail));
        }

        _log?.DebugLog($"Agent API: decompiled in {watch.Elapsed.TotalSeconds:0.0}s");
        return scriptsRoot;
    }

    private static void TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
        catch
        {
            // Best effort.
        }
    }

    public void Dispose()
    {
        // The process is disposed per call; nothing long-lived is held here.
    }
}

/// <summary>
/// Locates the FFDec jar and a Java executable.
///
/// Resolution order matches the packaged client: the Java runtime VibeSkua
/// ships inside FFDec/Runtime first, then JAVA_HOME, then PATH. An explicit
/// environment override lets a developer point at another FFDec build.
/// </summary>
internal static class FfdecLocator
{
    public static string? FindJar()
    {
        string? explicitJar = Environment.GetEnvironmentVariable("VIBESKUA_FFDEC_JAR");
        if (!string.IsNullOrWhiteSpace(explicitJar) && File.Exists(explicitJar))
            return explicitJar;

        string local = Path.Combine(AppContext.BaseDirectory, "FFDec", "ffdec.jar");
        if (File.Exists(local))
            return local;

        // Running from a source checkout: walk up to the repository root.
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "FFDec", "ffdec.jar");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    public static string FindJava()
    {
        string ffdecDirectory = Path.Combine(AppContext.BaseDirectory, "FFDec");
        string bundled = Path.Combine(ffdecDirectory, "Runtime", "bin", JavaName());
        if (File.Exists(bundled))
            return bundled;

        string? home = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            string candidate = Path.Combine(home, "bin", JavaName());
            if (File.Exists(candidate))
                return candidate;
        }

        string? path = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (string entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string candidate = Path.Combine(entry, JavaName());
                if (File.Exists(candidate))
                    return candidate;
            }
        }

        // Let the OS try; the failure is reported as java_missing by the caller.
        return JavaName();
    }

    private static string JavaName() => OperatingSystem.IsWindows() ? "java.exe" : "java";
}
