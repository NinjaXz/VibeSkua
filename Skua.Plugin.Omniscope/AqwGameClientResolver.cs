using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Skua.Plugin.Omniscope;

/// <summary>
/// Resolves and downloads the latest AQW game client using the game-version API with the CDN probing fallback
/// used by VibeSkua's FetchandDownloadGameFiles developer tool.
/// </summary>
internal static class AqwGameClientResolver
{
    private const string GameRoot = "https://game.aq.com/game/";
    private const int StartNumber = 3097;
    private const int MaximumRetries = 5;
    private const long MaximumSwfBytes = 128L * 1024 * 1024;
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(3);
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    /// <summary>
    /// Resolves and downloads the current client. If the game-version endpoint is unavailable, the CDN is
    /// searched using the cached version and the developer fetcher's exponential/binary probing strategy.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel resolution, probing, and download.</param>
    /// <returns>The downloaded SWF path and its source information.</returns>
    public static async Task<AqwGameClientFile> DownloadLatestAsync(CancellationToken cancellationToken = default)
    {
        string? fileName = null;
        Uri? gameClientUri = null;
        string resolutionMethod = "AQW game-version API";
        Exception? manifestFailure = null;

        try
        {
            fileName = await ResolveFromManifestAsync(cancellationToken).ConfigureAwait(false);
            gameClientUri = BuildGameClientUri(fileName);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            manifestFailure = ex;
        }

        string destinationDirectory = GetDestinationDirectory();
        Directory.CreateDirectory(destinationDirectory);

        if (fileName is null || gameClientUri is null)
        {
            int? latestVersion = await SearchLatestClientNumberAsync(destinationDirectory, cancellationToken).ConfigureAwait(false);
            if (!latestVersion.HasValue)
            {
                throw new InvalidOperationException(
                    $"AQW game-version lookup failed ({manifestFailure?.Message ?? "unknown error"}) and the CDN fallback could not resolve a Game####.swf client.",
                    manifestFailure);
            }

            fileName = $"Game{latestVersion.Value}.swf";
            gameClientUri = BuildGameClientUri(fileName);
            resolutionMethod = "CDN probe fallback";
        }

        string destinationPath = Path.Combine(destinationDirectory, fileName);
        try
        {
            await DownloadClientFileAsync(gameClientUri, destinationPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (resolutionMethod == "AQW game-version API" && IsRecoverable(ex))
        {
            int? latestVersion = await SearchLatestClientNumberAsync(destinationDirectory, cancellationToken).ConfigureAwait(false);
            if (!latestVersion.HasValue)
                throw new InvalidOperationException($"AQW resolved {fileName}, but its download failed and the CDN fallback could not find another client.", ex);

            string fallbackFileName = $"Game{latestVersion.Value}.swf";
            if (string.Equals(fallbackFileName, fileName, StringComparison.OrdinalIgnoreCase))
                throw;

            fileName = fallbackFileName;
            gameClientUri = BuildGameClientUri(fileName);
            destinationPath = Path.Combine(destinationDirectory, fileName);
            resolutionMethod = "CDN probe fallback after manifest download failure";
            await DownloadClientFileAsync(gameClientUri, destinationPath, cancellationToken).ConfigureAwait(false);
        }

        SaveLastVersion(destinationDirectory, fileName);
        return new AqwGameClientFile(fileName, destinationPath, gameClientUri, resolutionMethod);
    }

    private static async Task<string> ResolveFromManifestAsync(CancellationToken cancellationToken)
    {
        Uri versionUri = new(new Uri(GameRoot), "api/data/gameversion");
        Exception? lastFailure = null;

        for (int attempt = 1; attempt <= MaximumRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using HttpRequestMessage request = CreateGameRequest(HttpMethod.Get, versionUri);
                using HttpResponseMessage response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                if (ShouldRetry(response.StatusCode) && attempt < MaximumRetries)
                {
                    await DelayForRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await EnsureSuccessStatusCodeAsync(response, "AQW game-version lookup", cancellationToken).ConfigureAwait(false);
                await using Stream versionStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using JsonDocument versionDocument = await JsonDocument.ParseAsync(versionStream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (!versionDocument.RootElement.TryGetProperty("sFile", out JsonElement fileElement) ||
                    fileElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException("AQW's gameversion response did not contain a valid sFile entry.");
                }

                string? fileName = fileElement.GetString();
                ValidateGameFileName(fileName);
                return fileName!;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                lastFailure = ex;
                if (attempt < MaximumRetries)
                    await DelayForRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new HttpRequestException($"AQW game-version lookup failed after {MaximumRetries} attempts.", lastFailure);
    }

    private static async Task<int?> SearchLatestClientNumberAsync(string destinationDirectory, CancellationToken cancellationToken)
    {
        int start = LoadLastVersion(destinationDirectory) ?? StartNumber;
        if (!await ClientExistsAsync(start, cancellationToken).ConfigureAwait(false))
        {
            if (start == StartNumber)
                return null;

            start = StartNumber;
            if (!await ClientExistsAsync(start, cancellationToken).ConfigureAwait(false))
                return null;
        }

        int lastGood = start;
        int step = 1;
        int firstBad;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long candidateValue = (long)lastGood + step;
            int candidate = (int)Math.Min(candidateValue, int.MaxValue);
            if (candidate <= lastGood)
                return lastGood;

            if (await ClientExistsAsync(candidate, cancellationToken).ConfigureAwait(false))
            {
                lastGood = candidate;
                step = (int)Math.Min((long)step * 2, int.MaxValue);
                continue;
            }

            firstBad = candidate;
            break;
        }

        int low = lastGood;
        int high = firstBad;
        while (high - (long)low > 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int middle = (int)((low + (long)high) / 2);
            if (await ClientExistsAsync(middle, cancellationToken).ConfigureAwait(false))
                low = middle;
            else
                high = middle;
        }

        return low;
    }

    private static async Task<bool> ClientExistsAsync(int version, CancellationToken cancellationToken)
    {
        Uri clientUri = BuildGameClientUri($"Game{version}.swf");
        Exception? lastFailure = null;

        for (int attempt = 1; attempt <= MaximumRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using HttpRequestMessage headRequest = CreateGameRequest(HttpMethod.Head, clientUri);
                using HttpResponseMessage headResponse = await HttpClient.SendAsync(
                    headRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                if (headResponse.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented)
                {
                    using HttpRequestMessage getRequest = CreateGameRequest(HttpMethod.Get, clientUri);
                    using HttpResponseMessage getResponse = await HttpClient.SendAsync(
                        getRequest,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken).ConfigureAwait(false);

                    if (ShouldRetry(getResponse.StatusCode) && attempt < MaximumRetries)
                    {
                        await DelayForRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    return IsRealSwfResponse(getResponse);
                }

                if (ShouldRetry(headResponse.StatusCode) && attempt < MaximumRetries)
                {
                    await DelayForRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return IsRealSwfResponse(headResponse);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsRecoverable(ex))
            {
                lastFailure = ex;
                if (attempt < MaximumRetries)
                    await DelayForRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
        }

        if (lastFailure is not null)
            System.Diagnostics.Trace.WriteLine($"OMNISCOPE CDN probe failed for Game{version}.swf: {lastFailure.Message}");

        return false;
    }

    private static bool IsRealSwfResponse(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.OK)
            return false;

        if (response.RequestMessage?.RequestUri is Uri finalUri &&
            !finalUri.AbsolutePath.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        return !string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task DownloadClientFileAsync(Uri sourceUri, string destinationPath, CancellationToken cancellationToken)
    {
        string temporaryPath = destinationPath + ".download";
        Exception? lastFailure = null;

        for (int attempt = 1; attempt <= MaximumRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using HttpRequestMessage request = CreateGameRequest(HttpMethod.Get, sourceUri);
                using HttpResponseMessage response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaximumRetries)
                {
                    await DelayForRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await EnsureSuccessStatusCodeAsync(response, "AQW game-client SWF download", cancellationToken).ConfigureAwait(false);
                if (response.Content.Headers.ContentLength is > MaximumSwfBytes)
                    throw new InvalidDataException($"AQW game client SWF exceeds the {MaximumSwfBytes / 1024 / 1024:N0} MB safety limit.");

                byte[] signature = new byte[3];
                int signatureLength = 0;
                long totalBytes = 0;

                await using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                await using (FileStream destination = new(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
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
                            throw new InvalidDataException($"AQW game client SWF exceeded the {MaximumSwfBytes / 1024 / 1024:N0} MB safety limit.");

                        await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    }

                    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                bool validSignature = signatureLength == 3 &&
                    signature[1] == (byte)'W' &&
                    signature[2] == (byte)'S' &&
                    signature[0] is (byte)'F' or (byte)'C' or (byte)'Z';
                if (!validSignature)
                    throw new InvalidDataException("AQW returned a response that is not a valid SWF file.");

                if (totalBytes == 0)
                    throw new InvalidDataException("AQW returned an empty game client SWF.");

                File.Move(temporaryPath, destinationPath, true);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                TryDeleteTemporaryFile(temporaryPath);
                throw;
            }
            catch (Exception ex) when (attempt < MaximumRetries && IsRecoverable(ex))
            {
                lastFailure = ex;
                TryDeleteTemporaryFile(temporaryPath);
                await DelayForRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                TryDeleteTemporaryFile(temporaryPath);
                throw;
            }
        }

        throw new HttpRequestException($"AQW game-client download failed after {MaximumRetries} attempts: {sourceUri}", lastFailure);
    }

    private static HttpRequestMessage CreateGameRequest(HttpMethod method, Uri requestUri)
    {
        HttpRequestMessage request = new(method, requestUri);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd("*/*");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        request.Headers.TryAddWithoutValidation("Connection", "keep-alive");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return request;
    }

    private static Uri BuildGameClientUri(string fileName) =>
        new(new Uri(GameRoot), $"gamefiles/{Uri.EscapeDataString(fileName)}");

    private static void ValidateGameFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.OrdinalIgnoreCase) ||
            !fileName.StartsWith("Game", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(".swf", StringComparison.OrdinalIgnoreCase) ||
            fileName.Any(static character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
        {
            throw new InvalidDataException($"AQW returned an unexpected game client filename: {fileName ?? "null"}");
        }
    }

    private static string GetDestinationDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VibeSkua", "Omniscope", "GameClient");

    private static int? LoadLastVersion(string destinationDirectory)
    {
        string statePath = Path.Combine(destinationDirectory, "last_version.txt");
        try
        {
            string? state = File.Exists(statePath) ? File.ReadAllText(statePath).Trim() : null;
            return TryParseVersionPrefix(state, out int version) ? version : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void SaveLastVersion(string destinationDirectory, string fileName)
    {
        string statePath = Path.Combine(destinationDirectory, "last_version.txt");
        try
        {
            File.WriteAllText(statePath, fileName);
        }
        catch (IOException ex)
        {
            System.Diagnostics.Trace.WriteLine($"OMNISCOPE could not save the last game-client version: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            System.Diagnostics.Trace.WriteLine($"OMNISCOPE could not save the last game-client version: {ex.Message}");
        }
    }

    private static bool TryParseVersionPrefix(string? fileName, out int version)
    {
        version = 0;
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        string stem = Path.GetFileNameWithoutExtension(fileName);
        int start = 0;
        while (start < stem.Length && !char.IsDigit(stem[start]))
            start++;

        int end = start;
        while (end < stem.Length && char.IsDigit(stem[end]))
            end++;

        return end > start && int.TryParse(stem.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out version);
    }

    private static bool ShouldRetry(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.TooManyRequests || (int)statusCode >= 500;

    private static bool IsRecoverable(Exception exception) =>
        exception is HttpRequestException or IOException or JsonException or TaskCanceledException or InvalidDataException;

    private static Task DelayForRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        double seconds = Math.Min(30, RetryBaseDelay.TotalSeconds * Math.Pow(2, attempt - 1));
        return Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken);
    }

    private static async Task EnsureSuccessStatusCodeAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        string requestUrl = response.RequestMessage?.RequestUri?.ToString() ?? "Unknown URL";
        string responseBody = await ReadErrorBodyAsync(response, cancellationToken).ConfigureAwait(false);
        string diagnostic = string.IsNullOrWhiteSpace(responseBody) ? string.Empty : $" Response: {responseBody}";
        throw new HttpRequestException(
            $"{operation} was rejected by AQW. HTTP {(int)response.StatusCode} ({response.ReasonPhrase}). URL: {requestUrl}.{diagnostic}",
            null,
            response.StatusCode);
    }

    private static async Task<string> ReadErrorBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            byte[] buffer = new byte[2048];
            int totalRead = 0;
            while (totalRead < buffer.Length)
            {
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                    break;
                totalRead += bytesRead;
            }

            return Encoding.UTF8.GetString(buffer, 0, totalRead).Replace('\r', ' ').Replace('\n', ' ').Trim();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static void TryDeleteTemporaryFile(string temporaryPath)
    {
        try
        {
            File.Delete(temporaryPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>
/// Describes the AQW game client SWF downloaded from the manifest or discovered using CDN probing.
/// </summary>
internal sealed record AqwGameClientFile(string FileName, string LocalPath, Uri SourceUri, string ResolutionMethod);
