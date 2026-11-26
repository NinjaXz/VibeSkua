using System.Net;
using System.Net.Http.Headers;

namespace Skua.Plugin.Omniscope;

/// <summary>
/// Resolves and downloads the SWF for the map currently joined in AQW.
/// Uses the same gamefiles/maps endpoint as VibeSkua's existing map-item indexer.
/// </summary>
internal static class AqwMapSwfDownloader
{
    private const string MapRoot = "https://game.aq.com/game/gamefiles/maps/";
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
    private const long MaximumSwfBytes = 64L * 1024 * 1024;
    private const int MaximumRetries = 3;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(2)
    };

    /// <summary>
    /// Returns a valid local map SWF, downloading and caching it when it is not already present.
    /// </summary>
    /// <param name="mapFilePath">The map SWF path exposed by VibeSkua's map interface.</param>
    /// <param name="cancellationToken">Token used to cancel the download.</param>
    /// <returns>The absolute path to the local map SWF.</returns>
    public static async Task<string> DownloadAsync(string mapFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mapFilePath))
            throw new ArgumentException("The current map did not expose a SWF path.", nameof(mapFilePath));

        if (File.Exists(mapFilePath))
        {
            string localPath = Path.GetFullPath(mapFilePath);
            if (await IsValidSwfAsync(localPath, cancellationToken).ConfigureAwait(false))
                return localPath;

            throw new InvalidDataException($"The local map file is not a valid SWF: {localPath}");
        }

        (string relativePath, Uri sourceUri) = ResolveMapSource(mapFilePath);
        string cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VibeSkua",
            "Omniscope",
            "MapSwf");
        string destinationPath = Path.GetFullPath(Path.Combine(
            cacheDirectory,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string cacheRoot = Path.GetFullPath(cacheDirectory) + Path.DirectorySeparatorChar;
        if (!destinationPath.StartsWith(cacheRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The map SWF path resolves outside OMNISCOPE's cache directory.");

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (await IsValidSwfAsync(destinationPath, cancellationToken).ConfigureAwait(false))
            return destinationPath;

        string temporaryPath = destinationPath + ".download";
        TryDeleteTemporaryFile(temporaryPath);

        for (int attempt = 1; attempt <= MaximumRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using HttpRequestMessage request = CreateRequest(sourceUri);
            HttpResponseMessage response;
            try
            {
                response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                attempt < MaximumRetries &&
                !cancellationToken.IsCancellationRequested &&
                ex is HttpRequestException or IOException or TaskCanceledException)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
                continue;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    if (attempt < MaximumRetries)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(attempt), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                }

                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException(
                        $"AQW rejected the map SWF download. HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). URL: {sourceUri}",
                        null,
                        response.StatusCode);

                if (response.Content.Headers.ContentLength is > MaximumSwfBytes)
                    throw new InvalidDataException($"The map SWF exceeds the {MaximumSwfBytes / 1024 / 1024:N0} MB safety limit.");

                try
                {
                    await DownloadResponseAsync(response, temporaryPath, cancellationToken).ConfigureAwait(false);
                    File.Move(temporaryPath, destinationPath, true);
                    return destinationPath;
                }
                catch
                {
                    TryDeleteTemporaryFile(temporaryPath);
                    throw;
                }
            }
        }

        throw new HttpRequestException($"The map SWF could not be downloaded after {MaximumRetries} attempts: {sourceUri}");
    }

    /// <summary>
    /// Resolves a map path or AQW map URL to its safe relative cache path and download URI.
    /// </summary>
    private static (string RelativePath, Uri SourceUri) ResolveMapSource(string mapFilePath)
    {
        string value = mapFilePath.Trim().Replace('\\', '/');
        int queryIndex = value.IndexOfAny(['?', '#']);
        if (queryIndex >= 0)
            value = value[..queryIndex];

        Uri? directUri = null;
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? absoluteUri))
        {
            bool aqwHost = absoluteUri.Host.Equals("game.aq.com", StringComparison.OrdinalIgnoreCase) ||
                absoluteUri.Host.EndsWith(".aq.com", StringComparison.OrdinalIgnoreCase) ||
                absoluteUri.Host.EndsWith(".aqworlds.com", StringComparison.OrdinalIgnoreCase);
            if (absoluteUri.Scheme is not ("http" or "https") || !aqwHost)
                throw new InvalidDataException($"The map SWF URL is not an AQW game asset URL: {absoluteUri}");

            string absolutePath = Uri.UnescapeDataString(absoluteUri.AbsolutePath);
            int mapsIndex = absolutePath.IndexOf("/maps/", StringComparison.OrdinalIgnoreCase);
            if (mapsIndex < 0)
                throw new InvalidDataException($"The AQW map URL does not contain a maps directory: {absoluteUri}");

            value = absolutePath[(mapsIndex + "/maps/".Length)..];
            directUri = new UriBuilder(absoluteUri) { Query = string.Empty, Fragment = string.Empty }.Uri;
        }

        string[] prefixes = ["game/gamefiles/maps/", "gamefiles/maps/", "maps/"];
        foreach (string prefix in prefixes)
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..];
                break;
            }

        string[] segments = Uri.UnescapeDataString(value).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0 ||
            segments.Any(static segment =>
                segment is "." or ".." ||
                segment.Contains(':') ||
                segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidDataException($"AQW exposed an invalid map SWF path: {mapFilePath}");
        }

        if (!segments[^1].EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
            segments[^1] += ".swf";

        string relativePath = string.Join('/', segments);
        Uri sourceUri = directUri ?? new Uri(
            MapRoot + string.Join('/', segments.Select(Uri.EscapeDataString)));
        return (relativePath, sourceUri);
    }

    /// <summary>
    /// Downloads and validates the SWF response before it is moved into the cache.
    /// </summary>
    private static async Task DownloadResponseAsync(
        HttpResponseMessage response,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        byte[] signature = new byte[3];
        int signatureLength = 0;
        long totalBytes = 0;

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream destination = new(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (signatureLength < signature.Length)
            {
                int copyLength = Math.Min(signature.Length - signatureLength, bytesRead);
                Buffer.BlockCopy(buffer, 0, signature, signatureLength, copyLength);
                signatureLength += copyLength;
            }

            totalBytes += bytesRead;
            if (totalBytes > MaximumSwfBytes)
                throw new InvalidDataException($"The map SWF exceeded the {MaximumSwfBytes / 1024 / 1024:N0} MB safety limit.");

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
        }

        if (totalBytes == 0 || signatureLength != 3 || !IsSwfSignature(signature))
            throw new InvalidDataException("AQW returned a response that is not a valid map SWF.");

        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks a cached file's length and SWF signature.
    /// </summary>
    private static async Task<bool> IsValidSwfAsync(string filePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
            return false;

        FileInfo fileInfo = new(filePath);
        if (fileInfo.Length is < 3 or > MaximumSwfBytes)
            return false;

        try
        {
            await using FileStream stream = new(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                3,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] signature = new byte[3];
            int bytesRead = await stream.ReadAsync(signature.AsMemory(), cancellationToken).ConfigureAwait(false);
            return bytesRead == 3 && IsSwfSignature(signature);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Recognizes the uncompressed, zlib-compressed, and LZMA-compressed SWF signatures.
    /// </summary>
    private static bool IsSwfSignature(byte[] signature) =>
        signature.Length >= 3 &&
        signature[1] == (byte)'W' &&
        signature[2] == (byte)'S' &&
        (signature[0] is (byte)'F' or (byte)'C' or (byte)'Z');

    /// <summary>
    /// Creates an AQW-compatible asset request.
    /// </summary>
    private static HttpRequestMessage CreateRequest(Uri sourceUri)
    {
        HttpRequestMessage request = new(HttpMethod.Get, sourceUri);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("*/*");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        request.Headers.Referrer = new Uri("https://www.aq.com/");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return request;
    }

    /// <summary>
    /// Removes an incomplete download without obscuring the original failure.
    /// </summary>
    private static void TryDeleteTemporaryFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
