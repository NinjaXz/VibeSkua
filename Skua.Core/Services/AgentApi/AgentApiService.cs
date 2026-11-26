using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;

namespace Skua.Core.Services.AgentApi;

/// <summary>
/// Owns the agent API listener for the running client: reads the saved
/// settings, wires the router to the bot, and binds or releases the port.
///
/// The rule that matters here is that a bad setting must not take the client
/// down. A wrong port, a port another program already holds, or a non-loopback
/// bind without a token is logged and the client runs on without the endpoint.
/// </summary>
public sealed class AgentApiService : IAgentApiService
{
    private readonly ISettingsService _settings;
    private readonly IServiceProvider _services;
    private readonly object _lock = new();
    private AgentApiServer? _server;
    private MapInfoService? _mapInfo;
    private bool _disposed;

    public AgentApiService(ISettingsService settingsService, IServiceProvider serviceProvider)
    {
        _settings = settingsService;
        _services = serviceProvider;
    }

    public bool IsRunning => _server?.IsRunning == true;

    public int Port { get; private set; } = AgentApiOptions.DefaultPort;

    public void Start()
    {
        lock (_lock)
        {
            if (IsRunning || _disposed)
                return;

            AgentApiOptions options;
            try
            {
                options = AgentApiOptions.FromClientSettings(_settings.GetClient());
            }
            catch (Exception ex)
            {
                Log().DebugLog($"Agent API: settings could not be read: {ex.Message}");
                return;
            }

            Port = options.Port;
            if (!options.Enabled)
                return;

            ILogService log = Log();
            try
            {
                // Resolving the bot can build it for the first time, and that can
                // fail, so it belongs inside the guard.
                IScriptInterface? bot = _services.GetService<IScriptInterface>();
                IScriptManager? manager = _services.GetService<IScriptManager>();
                if (bot is null || manager is null)
                {
                    log.DebugLog("Agent API: the bot is not available, so the endpoint stayed off.");
                    return;
                }

                MapInfoOptions mapOptions = new();
                _mapInfo = new MapInfoService(bot, new AqwMapSwfDownloader(mapOptions, log), new FfdecMapDecompiler(mapOptions, log), mapOptions, log);
                AgentApiRouter router = new(options, bot, manager, log, _mapInfo);
                AgentApiServer server = new(options, router, log);
                server.Start();
                _server = server;
                log.DebugLog($"Agent API listening on http://{options.Bind}:{options.Port}/");
            }
            catch (Exception ex)
            {
                // Bind failures, rejected settings and a bot that will not build
                // all land here. The client keeps running; the endpoint stays off.
                string hint = ex is HttpListenerException
                    ? " Another client process, or an earlier run, may already hold this port; the one that holds it answers."
                    : string.Empty;
                string reason = $"Agent API could not start on {options.Bind}:{options.Port}: {ex.GetType().Name}: {ex.Message}.{hint}";
                log.DebugLog(reason);
                Console.Error.WriteLine(reason);
                _mapInfo?.Dispose();
                _mapInfo = null;
                _server = null;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            AgentApiServer? server = _server;
            MapInfoService? mapInfo = _mapInfo;
            _server = null;
            _mapInfo = null;
            if (server is null && mapInfo is null)
                return;

            try
            {
                server?.Stop();
            }
            catch (Exception ex)
            {
                Log().DebugLog($"Agent API stop failed: {ex.Message}");
            }
            finally
            {
                server?.Dispose();
                mapInfo?.Dispose();
            }
        }
    }

    /// <summary>
    /// The log service is resolved lazily and must never be the reason a
    /// lifecycle step fails, so a missing log service becomes silence.
    /// </summary>
    private ILogService Log() => _services.GetService<ILogService>() ?? SilentLog.Instance;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
    }

    private sealed class SilentLog : ILogService
    {
        public static readonly SilentLog Instance = new();

        public void DebugLog(string message)
        {
        }

        public void ScriptLog(string message)
        {
        }

        public void FlashLog(string message)
        {
        }

        public void ClearLog(Skua.Core.Models.LogType logType)
        {
        }

        public List<string> GetLogs(Skua.Core.Models.LogType logType) => new();
    }
}
