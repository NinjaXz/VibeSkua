using System.Net;
using System.Text;
using System.Text.Json;
using Skua.Core.Interfaces;

namespace Skua.Core.Services.AgentApi;

/// <summary>
/// HTTP shell for <see cref="AgentApiRouter"/>.
///
/// Two deliberate differences from the remote-control service:
/// the response is one JSON envelope, and no CORS header is sent, so a page
/// open in the user's browser cannot read the bot state without an explicit
/// choice by the operator.
/// </summary>
public sealed class AgentApiServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly AgentApiOptions _options;
    private readonly AgentApiRouter _router;
    private readonly ILogService _log;
    private readonly HttpListener _listener = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public AgentApiServer(AgentApiOptions options, AgentApiRouter router, ILogService log)
    {
        _options = options;
        _router = router;
        _log = log;
    }

    public bool IsRunning => _listener.IsListening;

    public void Start()
    {
        _options.Validate();

        string prefix = $"http://{(_options.Bind == "0.0.0.0" ? "+" : _options.Bind)}:{_options.Port}/";
        try
        {
            _listener.Prefixes.Add(prefix);
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            // Windows needs a URL ACL for a non-loopback prefix; the log line
            // names the command that grants it.
            _log.DebugLog($"Agent API could not bind {prefix}: {ex.Message}. " +
                $"For a non-loopback bind run: netsh http add urlacl url={prefix} user={Environment.UserDomainName}\\{Environment.UserName}");
            throw;
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => LoopAsync(_cts.Token));
        _log.DebugLog($"Agent API listening on {prefix} (token {(string.IsNullOrWhiteSpace(_options.Token) ? "off" : "on")})");
    }

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (ct.IsCancellationRequested || !_listener.IsListening)
            {
                return;
            }
            catch (Exception ex)
            {
                _log.DebugLog($"Agent API accept failed: {ex.Message}");
                continue;
            }

            // One request at a time: the router serialises mutations, and an
            // agent that fires ten calls at once should see ten ordered answers.
            try
            {
                await HandleContextAsync(context, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.DebugLog($"Agent API request failed: {ex.Message}");
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken ct)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;

        try
        {
            AgentApiRequest apiRequest = new()
            {
                Method = request.HttpMethod,
                Path = request.Url?.AbsolutePath ?? "/",
                Query = ReadQuery(request),
                Body = await ReadBodyAsync(request).ConfigureAwait(false),
                Authorization = request.Headers["Authorization"] ?? request.Headers["X-Api-Key"],
            };

            AgentApiResponse result = await _router.HandleAsync(apiRequest, ct).ConfigureAwait(false);
            WriteJson(response, result.Status, result.ToEnvelope());
        }
        finally
        {
            response.Close();
        }
    }

    private static Dictionary<string, string> ReadQuery(HttpListenerRequest request)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        if (request.QueryString is null)
            return values;

        foreach (string? key in request.QueryString.AllKeys)
        {
            if (key is not null)
                values[key] = request.QueryString[key] ?? string.Empty;
        }
        return values;
    }

    private async Task<string?> ReadBodyAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody)
            return null;

        using StreamReader reader = new(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        string body = await reader.ReadToEndAsync().ConfigureAwait(false);
        if (Encoding.UTF8.GetByteCount(body) > _options.MaxBodyBytes)
            throw new AgentApiException(413, "request_body_too_large");
        return body;
    }

    private static void WriteJson(HttpListenerResponse response, int status, object payload)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        response.StatusCode = status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            if (_listener.IsListening)
                _listener.Stop();
            _loop?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _log?.DebugLog($"Agent API stop failed: {ex.Message}");
        }
        finally
        {
            _log?.DebugLog("Agent API stopped");
        }
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _cts?.Dispose();
    }
}
