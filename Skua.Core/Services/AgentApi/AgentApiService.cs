using System.Globalization;
using System.Net;
using System.Net.Sockets;
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
    private string? _workerFile;
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

            if (AgentApiClientController.IsEmbedChild)
            {
                // A tab child never uses the shared port: the host holds it, and
                // children must not collide with each other. Each child publishes
                // its own port in a file, as the remote control service does.
                options.Bind = "127.0.0.1";
                options.Port = FreeTcpPort();
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
                AgentApiClientController client = new(_settings);
                AgentApiRouter router = new(options, bot, manager, log, _mapInfo, client);
                AgentApiServer server = new(options, router, log);
                server.Start();
                _server = server;   // the server logs the listening line, with the token state
                PublishWorkerPort(options.Port, log);
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
            string? workerFile = _workerFile;
            _server = null;
            _mapInfo = null;
            _workerFile = null;
            if (workerFile is not null)
            {
                try { File.Delete(workerFile); } catch { /* a dead child's file is harmless */ }
            }
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
    /// Writes the port file a tab host reads to find this client, and only for an
    /// embedded child: the host is found by its settings port instead.
    /// </summary>
    private void PublishWorkerPort(int port, ILogService log)
    {
        if (!AgentApiClientController.IsEmbedChild)
            return;

        try
        {
            string path = AgentApiClientController.OwnWorkerFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, port.ToString(CultureInfo.InvariantCulture));
            _workerFile = path;
        }
        catch (Exception ex)
        {
            log.DebugLog($"Agent API: the port file could not be written: {ex.Message}");
        }
    }

    private static int FreeTcpPort()
    {
        TcpListener probe = new(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
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
