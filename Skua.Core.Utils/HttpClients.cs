using System.Net;
using System.Net.Http.Headers;

namespace Skua.Core.Utils;

/// <summary>
/// HttpClient with connection pooling
/// </summary>
public class WebClient : HttpClient
{
    /// <param name="accJson"></param>
    public WebClient(bool accJson) : base(CreateHttpClientHandler())
    {
        if (accJson)
            DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        DefaultRequestHeaders.UserAgent.ParseAdd("Skua");
        Timeout = TimeSpan.FromSeconds(60);
    }

    /// <param name="token"></param>
    public WebClient(string token) : base(CreateHttpClientHandler())
    {
        DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", token);
        DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        DefaultRequestHeaders.UserAgent.ParseAdd("Skua/ScriptsUser");
        Timeout = TimeSpan.FromSeconds(60);
    }

    private static HttpClientHandler CreateHttpClientHandler()
    {
        return new HttpClientHandler()
        {
            MaxConnectionsPerServer = 10,
            UseCookies = false
        };
    }
}

/// <summary>
/// All HttpClients
/// </summary>
public static class HttpClients
{
    private static readonly SemaphoreSlim _githubApiSemaphore = new(5, 5);
    private static DateTime _lastGitHubApiCall = DateTime.MinValue;
    private static readonly TimeSpan _minDelayBetweenCalls = TimeSpan.FromMilliseconds(100);

    static HttpClients()
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.SystemDefault;
    }

    /// <summary>
    /// Gets the GitHub Client
    /// </summary>
    public static WebClient GitHubClient { get; private set; } = new(true);

    /// <summary>
    /// Gets the GitHub User Client
    /// </summary>
    public static WebClient? UserGitHubClient { get; set; } = null;

    /// <summary>
    /// Gets the Map Client
    /// </summary>
    public static WebClient GetAQContent { get; set; } = new(false);

    /// <summary>
    /// Default HttpClient
    /// </summary>
    public static HttpClient Default { get; private set; } = CreateSafeHttpClient();

    /// <summary>
    /// GitHub Raw Content Client - for raw.githubusercontent.com requests
    /// </summary>
    public static HttpClient GitHubRaw { get; private set; } = CreateSafeGitHubRawClient();

    /// <summary>
    /// Creates a new HttpClient that won't cause socket exhaustion - use this instead of 'new HttpClient()'
    /// </summary>
    /// <returns>A properly configured HttpClient</returns>
    public static HttpClient CreateSafeHttpClient()
    {
        return CreateHttpClient("Skua/1.0", TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// Creates a new HttpClient for raw.githubusercontent.com that won't cause socket exhaustion - use this instead of 'new HttpClient()' for github raw requests only.
    /// </summary>
    /// <returns>A properly configured HttpClient</returns>
    public static HttpClient CreateSafeGitHubRawClient()
    {
        return CreateHttpClient("Skua/1.0", TimeSpan.FromSeconds(60), true);
    }

    /// <summary>
    /// Makes a GitHub API request with automatic rate limiting and validation
    /// </summary>
    public static async Task<HttpResponseMessage> MakeGitHubApiRequestAsync(string url)
    {
        await _githubApiSemaphore.WaitAsync();
        try
        {
            TimeSpan timeSinceLastCall = DateTime.UtcNow - _lastGitHubApiCall;
            if (timeSinceLastCall < _minDelayBetweenCalls)
            {
                await Task.Delay(_minDelayBetweenCalls - timeSinceLastCall);
            }
            _lastGitHubApiCall = DateTime.UtcNow;
            HttpClient client = GetGHClient();
            HttpResponseMessage response = await ValidatedHttpExtensions.GetAsyncWithRetry(client, url, CancellationToken.None);

            if (response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? remainingValues))
            {
                if (int.TryParse(remainingValues.FirstOrDefault(), out int remaining) && remaining < 10)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
            return response;
        }
        finally
        {
            _githubApiSemaphore.Release();
        }
    }

    private static HttpClient CreateHttpClient(string userAgent, TimeSpan timeout, bool githubraw = false)
    {
        HttpClientHandler handler = new()
        {
            MaxConnectionsPerServer = 10,
            UseCookies = false
        };
        HttpClient client = new(handler);
        if (githubraw)
        {
            client.BaseAddress = new Uri("https://raw.githubusercontent.com/");
            client.Timeout = timeout;
            client.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
            {
                NoCache = true,
                NoStore = true
            };
        }
        else
        {
            client.Timeout = timeout;
        }
        client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        return client;
    }

    /// <summary>
    /// Gets either the GitHub or Github User Client
    /// </summary>
    /// <returns>Client Type</returns>
    public static HttpClient GetGHClient()
    {
        return UserGitHubClient ?? GitHubClient;
    }
}

/// <summary>
/// Extension methods for validated HTTP requests
/// </summary>
public static class ValidatedHttpExtensions
{
    private const int MaxRetries = 3;
    private const int InitialDelayMs = 1000;

    /// <summary>
    /// Makes a validated GET request that throws on failure
    /// </summary>
    public static async Task<HttpResponseMessage> GetAsync(this HttpClient client, string requestUri)
    {
        return await GetAsyncWithRetry(client, requestUri, CancellationToken.None);
    }

    /// <summary>
    /// Makes a validated GET request with cancellation token that throws on failure
    /// </summary>
    public static async Task<HttpResponseMessage> GetAsync(this HttpClient client, string requestUri, CancellationToken cancellationToken)
    {
        return await GetAsyncWithRetry(client, requestUri, cancellationToken);
    }

    /// <summary>
    /// Gets string content with validation
    /// </summary>
    public static async Task<string> GetStringAsync(this HttpClient client, string requestUri)
    {
        using HttpResponseMessage response = await GetAsync(client, requestUri);
        string content = await response.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(content) ? throw new InvalidDataException("Response content is empty or null") : content;
    }

    /// <summary>
    /// Gets string content with validation and retries that throws on failure
    /// </summary>
    public static async Task<HttpResponseMessage> GetAsyncWithRetry(HttpClient client, string requestUri, CancellationToken cancellationToken)
    {
        int delayMs = InitialDelayMs;
        Exception? lastException = null;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                HttpResponseMessage response = await client.GetAsync(requestUri, cancellationToken);
                response.EnsureSuccessStatusCode();
                return response;
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries - 1 && !cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
            catch (TaskCanceledException ex) when (attempt < MaxRetries - 1 && !ex.CancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
            catch (Exception ex)
            {
                lastException = ex;
                if (attempt >= MaxRetries - 1)
                    break;

                await Task.Delay(delayMs, cancellationToken);
                delayMs *= 2;
            }
        }

        throw lastException ?? new HttpRequestException($"Failed to fetch {requestUri} after {MaxRetries} attempts");
    }
}
