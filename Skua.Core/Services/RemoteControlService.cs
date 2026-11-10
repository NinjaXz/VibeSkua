using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using Skua.Core.Models.Quests;
using Skua.Core.Models.GitHub;
using Skua.Core.AppStartup;

namespace Skua.Core.Services;

/// <summary>
/// Strongly-typed action result returned by RemoteControlService endpoints.
/// </summary>
public class RemoteActionResult
{
    public bool success { get; set; }
    public string message { get; set; } = string.Empty;
    public object? data { get; set; }

    public static RemoteActionResult Ok(string message = "Success", object? data = null) =>
        new() { success = true, message = message, data = data };

    public static RemoteActionResult Fail(string message, object? data = null) =>
        new() { success = false, message = message, data = data };
}

public class ChatMessageDto
{
    public string type { get; set; } = "zone";
    public string sender { get; set; } = string.Empty;
    public string message { get; set; } = string.Empty;
    public string timestamp { get; set; } = string.Empty;
}

/// <summary>
/// Embedded HTTP REST server and remote controller for VibeSkua.
/// Allows Android mobile controllers, web browsers, and LAN dashboards to monitor and operate the bot.
/// </summary>
public class RemoteControlService : IRemoteControlService
{
    private readonly ISettingsService _settingsService;
    private readonly IServiceProvider _serviceProvider;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenerTask;
    private bool _isRunning;
    private int _port = 8088;
    private readonly object _lock = new();
    private DateTime _serverStartTime = DateTime.UtcNow;
    private readonly System.Collections.Concurrent.ConcurrentQueue<ChatMessageDto> _chatMessages = new();
    private bool _chatHooked = false;

    private void EnsureChatHooked()
    {
        if (_chatHooked) return;
        lock (_lock)
        {
            if (_chatHooked) return;
            try
            {
                var messenger = StrongReferenceMessenger.Default;
                messenger.Register<RemoteControlService, PacketMessage, int>(
                    this,
                    (int)MessageChannels.GameEvents,
                    (recipient, message) => recipient.OnPacketReceivedForChat(message.Packet));
                messenger.Register<RemoteControlService, ExtensionPacketMessage, int>(
                    this,
                    (int)MessageChannels.GameEvents,
                    (recipient, message) => recipient.OnExtensionPacketForChat(message.Packet));

                try
                {
                    WeakReferenceMessenger.Default.Register<RemoteControlService, PacketMessage, int>(
                        this,
                        (int)MessageChannels.GameEvents,
                        (recipient, message) => recipient.OnPacketReceivedForChat(message.Packet));
                }
                catch { }

                _chatHooked = true;
            }
            catch { }
        }
    }

    private void OnExtensionPacketForChat(dynamic packet)
    {
        if (packet == null) return;
        try
        {
            string type = Convert.ToString(packet["params"]?.type) ?? string.Empty;
            if (type == "str")
            {
                dynamic data = packet["params"]?.dataObj;
                if (data != null)
                {
                    string cmd = Convert.ToString(data[0]) ?? string.Empty;
                    if (cmd.Equals("chatm", StringComparison.OrdinalIgnoreCase))
                    {
                        string rawMsg = Convert.ToString(data[2]) ?? string.Empty;
                        string sender = Convert.ToString(data[3]) ?? "World";
                        string msgType = "zone";
                        string msg = rawMsg;
                        if (rawMsg.Contains('~'))
                        {
                            var s = rawMsg.Split(new[] { '~' }, 2);
                            msgType = s[0].ToLowerInvariant();
                            msg = s[1];
                        }
                        AddChatMessage(msgType, sender, msg);
                    }
                    else if (cmd.Equals("whisper", StringComparison.OrdinalIgnoreCase))
                    {
                        string rawMsg = Convert.ToString(data[2]) ?? string.Empty;
                        string sender = Convert.ToString(data[3]) ?? "Player";
                        AddChatMessage("whisper", sender, rawMsg);
                    }
                    else if (cmd.Equals("server", StringComparison.OrdinalIgnoreCase) || cmd.Equals("warning", StringComparison.OrdinalIgnoreCase))
                    {
                        string rawMsg = Convert.ToString(data[2]) ?? string.Empty;
                        AddChatMessage("server", "Server", rawMsg);
                    }
                }
            }
        }
        catch { }
    }

    private void OnPacketReceivedForChat(string packet)
    {
        if (string.IsNullOrWhiteSpace(packet)) return;
        try
        {
            if (packet.Contains("%chatm%"))
            {
                var parts = packet.Split('%');
                if (parts.Length >= 5)
                {
                    string rawMsg = parts[4];
                    string type = "zone";
                    string msg = rawMsg;
                    if (rawMsg.Contains('~'))
                    {
                        var s = rawMsg.Split(new[] { '~' }, 2);
                        type = s[0].ToLowerInvariant();
                        msg = s[1];
                    }
                    string sender = parts.Length > 5 && !string.IsNullOrWhiteSpace(parts[5]) ? parts[5] : "World";
                    AddChatMessage(type, sender, msg);
                }
            }
            else if (packet.Contains("%warning%") || packet.Contains("%server%"))
            {
                var parts = packet.Split('%');
                if (parts.Length >= 4)
                {
                    string msg = parts[parts.Length - 2];
                    AddChatMessage("server", "Server", msg);
                }
            }
            else if (packet.Contains("%whisper%"))
            {
                var parts = packet.Split('%');
                if (parts.Length >= 6)
                {
                    string msg = parts[4];
                    string sender = parts[5];
                    AddChatMessage("whisper", sender, msg);
                }
            }
        }
        catch { }
    }

    private void AddChatMessage(string type, string sender, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        _chatMessages.Enqueue(new ChatMessageDto
        {
            type = type.ToLowerInvariant(),
            sender = sender,
            message = message,
            timestamp = DateTime.UtcNow.ToString("HH:mm:ss")
        });
        while (_chatMessages.Count > 200)
        {
            _chatMessages.TryDequeue(out _);
        }
    }

    private object GetChatMessages()
    {
        EnsureChatHooked();
        return new
        {
            success = true,
            count = _chatMessages.Count,
            messages = _chatMessages.ToArray()
        };
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public bool IsRunning => _isRunning;
    public int Port => _port;

    public Func<int>? ActiveChildPidProvider { get; set; }
    public Action<int>? SelectChildPidAction { get; set; }
    public Action<int>? FocusChildPidAction { get; set; }
    public Func<int, bool>? CloseChildPidAction { get; set; }
    public Action<string, string?, string?>? LaunchAccountAction { get; set; }
    public Action<string>? LaunchGroupAction { get; set; }
    public Action? StartAllScriptsAction { get; set; }
    public Action? StopAllScriptsAction { get; set; }
    public Action<string>? LoadScriptAllAction { get; set; }
    public Action<bool>? HeadlessAllAction { get; set; }
    public Action? ReloginAllAction { get; set; }
    public Action<string>? BroadcastArmySchedulerAction { get; set; }
    public Action? StopArmySchedulerAction { get; set; }
        public Func<List<object>>? TabsInfoProvider { get; set; }
    public bool IsManagerMode { get; set; }
    public Func<object>? ArmyQueueProvider { get; set; }
    public Func<Task<object>>? StartArmyQueueAction { get; set; }
    public Action? StopArmyQueueAction { get; set; }
    public Action? PauseResumeArmyQueueAction { get; set; }
    public Action<int, string?>? SetArmyQueueStepScriptAction { get; set; }
    public Action<string>? AddArmyQueueStepAction { get; set; }
    public Action<int>? RemoveArmyQueueStepAction { get; set; }

    public List<(int pid, int port)> GetAllChildTabs()
    {
        var list = new List<(int pid, int port)>();
        string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
        if (!Directory.Exists(dir)) return list;

        try
        {
            var files = Directory.GetFiles(dir, "*.remote.txt");
            foreach (var f in files)
            {
                string fileName = Path.GetFileName(f);
                string pidStr = fileName.Split('.')[0];
                if (int.TryParse(pidStr, out int pid) && IsProcessAlive(pid))
                {
                    int port = ReadPortFile(f);
                    if (port > 0)
                    {
                        list.Add((pid, port));
                    }
                }
            }
        }
        catch { }
        return list;
    }

    private static int ReadPortFile(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            if (int.TryParse(reader.ReadToEnd().Trim(), out int port))
                return port;
        }
        catch { }
        return 0;
    }

    private static readonly HttpClient _proxyClient = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectTimeout = TimeSpan.FromSeconds(3)
    })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public RemoteControlService(ISettingsService settingsService, IServiceProvider serviceProvider)
    {
        _settingsService = settingsService;
        _serviceProvider = serviceProvider;
    }

    public void Start()
    {
        EnsureChatHooked();
        lock (_lock)
        {
            if (_isRunning)
                return;

            var clientSettings = _settingsService.GetClient();
            if (clientSettings != null && !clientSettings.RemoteControlServerEnabled)
                return;

            bool isHost = HotKeys.IsHostProcess;
            bool isChild = !isHost && HotKeys.ParentHostProcessId > 0;

            if (isChild)
            {
                // In embedded child mode: bind ephemeral loopback port so tabs do not collide
                try
                {
                    _cts = new CancellationTokenSource();
                    _listener = new TcpListener(IPAddress.Loopback, 0);
                    _listener.Start();
                    _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                    _serverStartTime = DateTime.UtcNow;
                    _isRunning = true;
                    _listenerTask = Task.Run(() => AcceptClientsAsync(_listener, _cts.Token));

                    string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                    Directory.CreateDirectory(dir);
                    string remoteFile = Path.Combine(dir, $"{Environment.ProcessId}.remote.txt");
                    File.WriteAllText(remoteFile, _port.ToString());

                    System.Diagnostics.Trace.WriteLine($"[RemoteControlService] Child worker server started on 127.0.0.1:{_port} (PID {Environment.ProcessId})");
                }
                catch (Exception ex)
                {
                    _isRunning = false;
                    System.Diagnostics.Trace.WriteLine($"[RemoteControlService] Failed to start child worker server: {ex.Message}");
                }
            }
            else if (IsManagerMode)
            {
                // Manager Gateway Mode: prefer default port 8088 for direct mobile connectivity
                _port = (clientSettings != null && clientSettings.RemoteControlPort > 0)
                    ? clientSettings.RemoteControlPort
                    : 8088;

                bool bound = false;
                try
                {
                    _cts = new CancellationTokenSource();
                    _listener = new TcpListener(IPAddress.Any, _port);
                    _listener.Start();
                    _serverStartTime = DateTime.UtcNow;
                    _isRunning = true;
                    _listenerTask = Task.Run(() => AcceptClientsAsync(_listener, _cts.Token));
                    bound = true;
                    System.Diagnostics.Trace.WriteLine($"[RemoteControlService] Manager gateway server started on http://0.0.0.0:{_port}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"[RemoteControlService] Manager failed to bind {_port}, falling back to ephemeral: {ex.Message}");
                }

                if (!bound)
                {
                    try
                    {
                        _cts = new CancellationTokenSource();
                        _listener = new TcpListener(IPAddress.Loopback, 0);
                        _listener.Start();
                        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                        _serverStartTime = DateTime.UtcNow;
                        _isRunning = true;
                        _listenerTask = Task.Run(() => AcceptClientsAsync(_listener, _cts.Token));
                        System.Diagnostics.Trace.WriteLine($"[RemoteControlService] Manager secondary server started on 127.0.0.1:{_port}");
                    }
                    catch { }
                }

                try
                {
                    string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                    Directory.CreateDirectory(dir);
                    string managerFile = Path.Combine(dir, "manager.remote.txt");
                    File.WriteAllText(managerFile, $"{Environment.ProcessId}:{_port}");
                }
                catch { }
            }
            else
            {
                // In Host mode (or standalone single window): bind external/LAN port for mobile apps and dashboards
                _port = (clientSettings != null && clientSettings.RemoteControlPort > 0)
                    ? clientSettings.RemoteControlPort
                    : 8088;

                bool bound = false;
                try
                {
                    _cts = new CancellationTokenSource();
                    _listener = new TcpListener(IPAddress.Any, _port);
                    _listener.Start();
                    _serverStartTime = DateTime.UtcNow;
                    _isRunning = true;
                    _listenerTask = Task.Run(() => AcceptClientsAsync(_listener, _cts.Token));
                    bound = true;
                    System.Diagnostics.Trace.WriteLine($"[RemoteControlService] Gateway server started on http://0.0.0.0:{_port} (IsHost: {isHost})");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"[RemoteControlService] Failed to bind port {_port} as primary gateway: {ex.Message}");
                }

                if (!bound)
                {
                    try
                    {
                        _cts = new CancellationTokenSource();
                        _listener = new TcpListener(IPAddress.Loopback, 0);
                        _listener.Start();
                        _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
                        _serverStartTime = DateTime.UtcNow;
                        _isRunning = true;
                        _listenerTask = Task.Run(() => AcceptClientsAsync(_listener, _cts.Token));

                        string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                        Directory.CreateDirectory(dir);
                        string remoteFile = Path.Combine(dir, $"{Environment.ProcessId}.remote.txt");
                        File.WriteAllText(remoteFile, _port.ToString());
                        System.Diagnostics.Trace.WriteLine($"[RemoteControlService] Registered tab on 127.0.0.1:{_port} under gateway");
                    }
                    catch (Exception ex)
                    {
                        _isRunning = false;
                        System.Diagnostics.Trace.WriteLine($"[RemoteControlService] Failed fallback worker server: {ex.Message}");
                    }
                }

                try
                {
                    string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                    Directory.CreateDirectory(dir);
                    string hostFile = Path.Combine(dir, "host.remote.txt");
                    File.WriteAllText(hostFile, $"{Environment.ProcessId}:{_port}");
                }
                catch { }
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning)
                return;

            _isRunning = false;

            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                string remoteFile = Path.Combine(dir, $"{Environment.ProcessId}.remote.txt");
                if (File.Exists(remoteFile))
                    File.Delete(remoteFile);

                string hostFile = Path.Combine(dir, "host.remote.txt");
                if (File.Exists(hostFile))
                    File.Delete(hostFile);

                if (IsManagerMode)
                {
                    string managerFile = Path.Combine(dir, "manager.remote.txt");
                    if (File.Exists(managerFile))
                        File.Delete(managerFile);
                }
            }
            catch { }

            try
            {
                _cts?.Cancel();
                _listener?.Stop();
            }
            catch
            {
                // ignore shutdown exceptions
            }
            finally
            {
                _listener = null;
                _cts?.Dispose();
                _cts = null;
                _listenerTask = null;
            }
            System.Diagnostics.Trace.WriteLine("[RemoteControlService] Server stopped.");
        }
    }

    private async Task AcceptClientsAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                TcpClient client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (token.IsCancellationRequested)
                    break;
                System.Diagnostics.Trace.WriteLine($"[RemoteControlService] Accept error: {ex.Message}");
                await Task.Delay(100, token).ConfigureAwait(false);
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        try
        {
            client.NoDelay = true;
            client.LingerState = new LingerOption(true, 5);
            client.SendBufferSize = 65536;
            client.ReceiveBufferSize = 65536;
        }
        catch { }

        using (client)
        using (NetworkStream stream = client.GetStream())
        {
            try
            {
                stream.ReadTimeout = 10000;
                stream.WriteTimeout = 15000;

                byte[] buffer = new byte[8192];
                using var ms = new MemoryStream();
                int headerEndIndex = -1;
                int headerDelimiterLength = 4;

                while (headerEndIndex == -1 && ms.Length < 32768)
                {
                    int read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                    if (read <= 0) return;
                    ms.Write(buffer, 0, read);

                    byte[] current = ms.GetBuffer();
                    headerEndIndex = FindHeaderEnd(current, (int)ms.Length, out headerDelimiterLength);
                }

                if (headerEndIndex == -1)
                    return;

                byte[] rawData = ms.ToArray();
                string headerText = Encoding.UTF8.GetString(rawData, 0, headerEndIndex);

                using var strReader = new StringReader(headerText);
                string? requestLine = await strReader.ReadLineAsync(token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestLine))
                    return;

                string[] parts = requestLine.Split(' ');
                if (parts.Length < 2)
                    return;

                string method = parts[0].ToUpperInvariant();
                string fullUrl = parts[1];
                string path = fullUrl;
                string queryString = string.Empty;

                int queryIdx = fullUrl.IndexOf('?');
                if (queryIdx >= 0)
                {
                    path = fullUrl[..queryIdx];
                    queryString = fullUrl[(queryIdx + 1)..];
                }

                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                int contentLength = 0;
                string? hLine;
                while (!string.IsNullOrEmpty(hLine = await strReader.ReadLineAsync(token).ConfigureAwait(false)))
                {
                    int colonIdx = hLine.IndexOf(':');
                    if (colonIdx > 0)
                    {
                        string headerName = hLine[..colonIdx].Trim();
                        string headerValue = hLine[(colonIdx + 1)..].Trim();
                        headers[headerName] = headerValue;
                        if (headerName.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(headerValue, out contentLength);
                        }
                    }
                }

                string body = string.Empty;
                if (contentLength > 0 && contentLength < 10 * 1024 * 1024)
                {
                    int bodyStart = headerEndIndex + headerDelimiterLength;
                    int bodyAlreadyRead = rawData.Length - bodyStart;
                    byte[] bodyBytes = new byte[contentLength];

                    int copyLen = Math.Min(contentLength, Math.Max(0, bodyAlreadyRead));
                    if (copyLen > 0)
                    {
                        Array.Copy(rawData, bodyStart, bodyBytes, 0, copyLen);
                    }

                    int remaining = contentLength - copyLen;
                    int offset = copyLen;
                    while (remaining > 0)
                    {
                        int read = await stream.ReadAsync(bodyBytes, offset, remaining, token).ConfigureAwait(false);
                        if (read <= 0) break;
                        offset += read;
                        remaining -= read;
                    }

                    body = Encoding.UTF8.GetString(bodyBytes);
                }

                if (method == "OPTIONS")
                {
                    await SendResponseAsync(stream, 204, "No Content", "text/plain", Array.Empty<byte>()).ConfigureAwait(false);
                    return;
                }

                await RouteRequestAsync(stream, method, path, queryString, body, headers, token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[RemoteControlService] HandleClient error: {ex.Message}");
            }
            finally
            {
                try
                {
                    client.Client?.Shutdown(SocketShutdown.Send);
                }
                catch { }
            }
        }
    }

    private static int FindHeaderEnd(byte[] data, int length, out int delimiterLength)
    {
        for (int i = 0; i < length - 3; i++)
        {
            if (data[i] == 13 && data[i + 1] == 10 && data[i + 2] == 13 && data[i + 3] == 10)
            {
                delimiterLength = 4;
                return i;
            }
        }
        for (int i = 0; i < length - 1; i++)
        {
            if (data[i] == 10 && data[i + 1] == 10)
            {
                delimiterLength = 2;
                return i;
            }
        }
        delimiterLength = 0;
        return -1;
    }

    private async Task RouteRequestAsync(NetworkStream stream, string method, string path, string query, string body, Dictionary<string, string> headers, CancellationToken token)
    {
        try
        {
            string lowerPath = path.ToLowerInvariant();

            // When running in Host Gateway or Manager mode, proxy API requests or serve fleet commands
            if (HotKeys.IsHostProcess || IsManagerMode)
            {
                if (IsManagerMode)
                {
                    string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                    string hostFile = Path.Combine(dir, "host.remote.txt");
                    if (File.Exists(hostFile))
                    {
                        try
                        {
                            string[] parts = File.ReadAllText(hostFile).Trim().Split(':');
                            if (parts.Length == 2 && int.TryParse(parts[0], out int hPid) && int.TryParse(parts[1], out int hPort) && IsProcessAlive(hPid))
                            {
                                if (lowerPath != "/api/army/queue" && lowerPath != "/api/fleet/queue" &&
                                    lowerPath != "/api/army/queue/start" && lowerPath != "/api/fleet/queue/start" &&
                                    lowerPath != "/api/army/queue/stop" && lowerPath != "/api/fleet/queue/stop" &&
                                    lowerPath != "/api/army/queue/pause" && lowerPath != "/api/fleet/queue/pause" &&
                                    lowerPath != "/api/army/queue/step/script" && lowerPath != "/api/fleet/queue/step/script" &&
                                    lowerPath != "/api/accounts" && lowerPath != "/api/accounts/saved" &&
                                    lowerPath != "/api/accounts/launch")
                                {
                                    bool forwarded = await ForwardRequestToChildAsync(stream, hPort, method, path, query, body, headers, token).ConfigureAwait(false);
                                    if (forwarded) return;
                                }
                            }
                        }
                        catch { }
                    }
                }

                if (lowerPath == "/api/ping")
                {
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET").ConfigureAwait(false); return; }
                    int activePid = 0;
                    if (ActiveChildPidProvider != null)
                    {
                        try { activePid = ActiveChildPidProvider(); } catch { }
                    }
                    await SendJsonResponseAsync(stream, 200, new
                    {
                        status = "ok",
                        app = IsManagerMode ? "VibeSkua.Manager" : "VibeSkua",
                        version = ClientFileSources.AssemblyVersion,
                        port = _port,
                        is_host = true,
                        is_manager = IsManagerMode,
                        has_tabs = GetAllChildTabs().Count > 0,
                        active_pid = activePid,
                        timestamp = DateTime.UtcNow
                    }).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/tabs")
                {
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET").ConfigureAwait(false); return; }
                    int activePid = 0;
                    if (ActiveChildPidProvider != null)
                    {
                        try { activePid = ActiveChildPidProvider(); } catch { }
                    }
                    await SendJsonResponseAsync(stream, 200, new
                    {
                        active_pid = activePid,
                        tabs = GetTabsInfo()
                    }).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/tabs/select")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    int targetPid = 0;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            if (doc.RootElement.TryGetProperty("pid", out var pidProp))
                                targetPid = pidProp.GetInt32();
                        }
                        catch { }
                    }
                    if (targetPid > 0)
                    {
                        SelectChildPidAction?.Invoke(targetPid);
                        await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok($"Selected tab {targetPid}")).ConfigureAwait(false);
                    }
                    else
                    {
                        await SendJsonResponseAsync(stream, 400, RemoteActionResult.Fail("Invalid or missing 'pid' parameter")).ConfigureAwait(false);
                    }
                    return;
                }

                if (lowerPath == "/api/tabs/focus" || lowerPath == "/api/tabs/focus-pc")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    int targetPid = 0;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            if (doc.RootElement.TryGetProperty("pid", out var pidProp))
                                targetPid = pidProp.GetInt32();
                        }
                        catch { }
                    }
                    if (targetPid > 0)
                    {
                        FocusChildPidAction?.Invoke(targetPid);
                        await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok($"Focused tab {targetPid} on PC")).ConfigureAwait(false);
                    }
                    else
                    {
                        await SendJsonResponseAsync(stream, 400, RemoteActionResult.Fail("Invalid or missing 'pid' parameter")).ConfigureAwait(false);
                    }
                    return;
                }

                if (lowerPath == "/api/tabs/close")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    int targetPid = 0;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            if (doc.RootElement.TryGetProperty("pid", out var pidProp))
                                targetPid = pidProp.GetInt32();
                        }
                        catch { }
                    }
                    if (targetPid > 0 && CloseChildPidAction != null)
                    {
                        bool closed = CloseChildPidAction(targetPid);
                        await SendJsonResponseAsync(stream, closed ? 200 : 404, closed ? RemoteActionResult.Ok($"Closed tab {targetPid}") : RemoteActionResult.Fail($"Tab {targetPid} not found")).ConfigureAwait(false);
                    }
                    else
                    {
                        await SendJsonResponseAsync(stream, 400, RemoteActionResult.Fail("Invalid or missing 'pid' parameter")).ConfigureAwait(false);
                    }
                    return;
                }

                if (lowerPath == "/api/accounts" || lowerPath == "/api/accounts/saved")
                {
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET").ConfigureAwait(false); return; }
                    var accs = _settingsService.Get<Dictionary<string, AccountData>>("ManagedAccounts") ?? new();
                    var groups = _settingsService.Get<List<GroupData>>("AccountGroups") ?? new();

                    var accountsList = accs.Select(kv => new
                    {
                        username = kv.Key,
                        display_name = !string.IsNullOrWhiteSpace(kv.Value.DisplayName) ? kv.Value.DisplayName : kv.Key,
                        tags = kv.Value.Tags ?? new List<string>()
                    }).ToList();

                    var groupsList = groups.Select(g => new
                    {
                        name = g.Name,
                        accounts = g.Accounts ?? new List<string>()
                    }).ToList();

                    await SendJsonResponseAsync(stream, 200, new
                    {
                        accounts = accountsList,
                        groups = groupsList
                    }).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/accounts/launch")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    string username = string.Empty;
                    string group = string.Empty;
                    string server = string.Empty;

                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            if (doc.RootElement.TryGetProperty("username", out var u)) username = u.GetString() ?? string.Empty;
                            else if (doc.RootElement.TryGetProperty("account", out var a)) username = a.GetString() ?? string.Empty;
                            else if (doc.RootElement.TryGetProperty("user", out var usr)) username = usr.GetString() ?? string.Empty;

                            if (doc.RootElement.TryGetProperty("group", out var g)) group = g.GetString() ?? string.Empty;
                            if (doc.RootElement.TryGetProperty("server", out var s)) server = s.GetString() ?? string.Empty;
                        }
                        catch { }
                    }

                    if (!string.IsNullOrWhiteSpace(group))
                    {
                        LaunchGroupAction?.Invoke(group);
                        await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok($"Launching account group '{group}'")).ConfigureAwait(false);
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(username))
                    {
                        LaunchAccountAction?.Invoke(username, null, string.IsNullOrEmpty(server) ? null : server);
                        await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok($"Launching account '{username}'")).ConfigureAwait(false);
                        return;
                    }

                    await SendJsonResponseAsync(stream, 400, RemoteActionResult.Fail("Must specify 'username' or 'group' to launch")).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/army/queue" || lowerPath == "/api/fleet/queue")
                {
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET").ConfigureAwait(false); return; }
                    if (ArmyQueueProvider != null)
                    {
                        await SendJsonResponseAsync(stream, 200, ArmyQueueProvider()).ConfigureAwait(false);
                        return;
                    }

                    string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                    string managerFile = Path.Combine(dir, "manager.remote.txt");
                    if (File.Exists(managerFile))
                    {
                        try
                        {
                            string[] parts = File.ReadAllText(managerFile).Split(':');
                            if (parts.Length == 2 && int.TryParse(parts[0], out int mPid) && int.TryParse(parts[1], out int mPort) && IsProcessAlive(mPid))
                            {
                                await ForwardRequestToChildAsync(stream, mPort, method, path, query, body, headers, token).ConfigureAwait(false);
                                return;
                            }
                        }
                        catch { }
                    }

                    await SendJsonResponseAsync(stream, 200, new
                    {
                        is_running = false,
                        is_paused = false,
                        status = "Army Queue available via VibeSkua Manager",
                        steps = new object[0],
                        presets = new object[0]
                    }).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/army/queue/start" || lowerPath == "/api/fleet/queue/start")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    if (StartArmyQueueAction != null)
                    {
                        var res = await StartArmyQueueAction().ConfigureAwait(false);
                        await SendJsonResponseAsync(stream, (res is RemoteActionResult rar && !rar.success) ? 400 : 200, res).ConfigureAwait(false);
                        return;
                    }
                    string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                    string managerFile = Path.Combine(dir, "manager.remote.txt");
                    if (File.Exists(managerFile))
                    {
                        try
                        {
                            string[] parts = File.ReadAllText(managerFile).Split(':');
                            if (parts.Length == 2 && int.TryParse(parts[0], out int mPid) && int.TryParse(parts[1], out int mPort) && IsProcessAlive(mPid))
                            {
                                await ForwardRequestToChildAsync(stream, mPort, method, path, query, body, headers, token).ConfigureAwait(false);
                                return;
                            }
                        }
                        catch { }
                    }
                    await SendJsonResponseAsync(stream, 400, RemoteActionResult.Fail("Army Queue not connected in Manager")).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/army/queue/stop" || lowerPath == "/api/fleet/queue/stop")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    if (StopArmyQueueAction != null)
                    {
                        StopArmyQueueAction();
                        await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok("Army Queue stopped")).ConfigureAwait(false);
                        return;
                    }
                    string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                    string managerFile = Path.Combine(dir, "manager.remote.txt");
                    if (File.Exists(managerFile))
                    {
                        try
                        {
                            string[] parts = File.ReadAllText(managerFile).Split(':');
                            if (parts.Length == 2 && int.TryParse(parts[0], out int mPid) && int.TryParse(parts[1], out int mPort) && IsProcessAlive(mPid))
                            {
                                await ForwardRequestToChildAsync(stream, mPort, method, path, query, body, headers, token).ConfigureAwait(false);
                                return;
                            }
                        }
                        catch { }
                    }
                    await SendJsonResponseAsync(stream, 400, RemoteActionResult.Fail("Army Queue not connected in Manager")).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/army/queue/pause" || lowerPath == "/api/fleet/queue/pause")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    if (PauseResumeArmyQueueAction != null)
                    {
                        PauseResumeArmyQueueAction();
                        await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok("Army Queue pause toggled")).ConfigureAwait(false);
                        return;
                    }
                    string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                    string managerFile = Path.Combine(dir, "manager.remote.txt");
                    if (File.Exists(managerFile))
                    {
                        try
                        {
                            string[] parts = File.ReadAllText(managerFile).Split(':');
                            if (parts.Length == 2 && int.TryParse(parts[0], out int mPid) && int.TryParse(parts[1], out int mPort) && IsProcessAlive(mPid))
                            {
                                await ForwardRequestToChildAsync(stream, mPort, method, path, query, body, headers, token).ConfigureAwait(false);
                                return;
                            }
                        }
                        catch { }
                    }
                    await SendJsonResponseAsync(stream, 400, RemoteActionResult.Fail("Army Queue not connected in Manager")).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/army/queue/step/script" || lowerPath == "/api/fleet/queue/step/script")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    int step = 0;
                    string scriptPath = string.Empty;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            if (doc.RootElement.TryGetProperty("step", out var sProp)) step = sProp.GetInt32();
                            if (doc.RootElement.TryGetProperty("path", out var pProp)) scriptPath = pProp.GetString() ?? string.Empty;
                            else if (doc.RootElement.TryGetProperty("script", out var scProp)) scriptPath = scProp.GetString() ?? string.Empty;
                        }
                        catch { }
                    }
                    if (SetArmyQueueStepScriptAction != null)
                    {
                        SetArmyQueueStepScriptAction(step, scriptPath);
                        await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok($"Set script on Squad {step + 1}")).ConfigureAwait(false);
                        return;
                    }
                    await SendJsonResponseAsync(stream, 400, RemoteActionResult.Fail("Army Queue not connected in Manager")).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/fleet" || lowerPath == "/api/fleet/status" || lowerPath == "/api/army" || lowerPath == "/api/army/status")
                {
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET").ConfigureAwait(false); return; }
                    var tabs = GetTabsInfo();
                    int activePid = 0;
                    if (ActiveChildPidProvider != null)
                    {
                        try { activePid = ActiveChildPidProvider(); } catch { }
                    }
                    await SendJsonResponseAsync(stream, 200, new
                    {
                        active_pid = activePid,
                        count = tabs.Count,
                        tabs = tabs
                    }).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/scripts/update" || lowerPath == "/api/scripts/refresh")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    try
                    {
                        var getScripts = _serviceProvider.GetService<IGetScriptsService>();
                        if (getScripts != null)
                        {
                            await getScripts.GetScriptsAsync(null, default).ConfigureAwait(false);
                        }
                    }
                    catch { }
                    await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok("Scripts updated successfully")).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/fleet/start-all" || lowerPath == "/api/army/start-all")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    StartAllScriptsAction?.Invoke();
                    var children = GetAllChildTabs();
                    foreach (var c in children)
                    {
                        _ = ForwardRequestToChildAsync(null, c.port, "POST", "/api/scripts/start", string.Empty, body, headers, token);
                    }
                    await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok($"Start All broadcasted to {children.Count} accounts")).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/fleet/stop-all" || lowerPath == "/api/army/stop-all")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    StopAllScriptsAction?.Invoke();
                    var children = GetAllChildTabs();
                    foreach (var c in children)
                    {
                        _ = ForwardRequestToChildAsync(null, c.port, "POST", "/api/scripts/stop", string.Empty, body, headers, token);
                    }
                    await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok($"Stop All broadcasted to {children.Count} accounts")).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/fleet/load-script" || lowerPath == "/api/army/load-script")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    string scriptPath = string.Empty;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            if (doc.RootElement.TryGetProperty("path", out var pProp)) scriptPath = pProp.GetString() ?? string.Empty;
                            else if (doc.RootElement.TryGetProperty("script", out var sProp)) scriptPath = sProp.GetString() ?? string.Empty;
                        }
                        catch { }
                    }
                    if (!string.IsNullOrWhiteSpace(scriptPath))
                    {
                        LoadScriptAllAction?.Invoke(scriptPath);
                    }
                    var children = GetAllChildTabs();
                    foreach (var c in children)
                    {
                        _ = ForwardRequestToChildAsync(null, c.port, "POST", "/api/scripts/load", string.Empty, body, headers, token);
                    }
                    await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok($"Loaded script on {children.Count} accounts")).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/fleet/scheduler/start" || lowerPath == "/api/army/scheduler/start")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    BroadcastArmySchedulerAction?.Invoke(body);
                    await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok("Army Scheduler started across fleet")).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/fleet/scheduler/stop" || lowerPath == "/api/army/scheduler/stop")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    StopArmySchedulerAction?.Invoke();
                    await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok("Army Scheduler stopped across fleet")).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/fleet/headless" || lowerPath == "/api/army/headless")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    bool headless = true;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            if (doc.RootElement.TryGetProperty("value", out var vProp)) headless = vProp.GetBoolean();
                            else if (doc.RootElement.TryGetProperty("enabled", out var eProp)) headless = eProp.GetBoolean();
                        }
                        catch { }
                    }
                    HeadlessAllAction?.Invoke(headless);
                    var children = GetAllChildTabs();
                    string toggleBody = System.Text.Json.JsonSerializer.Serialize(new { option = "HeadlessMode", value = headless });
                    foreach (var c in children)
                    {
                        _ = ForwardRequestToChildAsync(null, c.port, "POST", "/api/options/toggle", string.Empty, toggleBody, headers, token);
                    }
                    await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok($"Headless mode set to {headless} across {children.Count} accounts")).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "/api/fleet/relogin-all" || lowerPath == "/api/army/relogin-all")
                {
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST").ConfigureAwait(false); return; }
                    ReloginAllAction?.Invoke();
                    var children = GetAllChildTabs();
                    foreach (var c in children)
                    {
                        _ = ForwardRequestToChildAsync(null, c.port, "POST", "/api/player/relog", string.Empty, body, headers, token);
                    }
                    await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok($"Relogin broadcasted to {children.Count} accounts")).ConfigureAwait(false);
                    return;
                }

                if (lowerPath == "" || lowerPath == "/" || lowerPath == "/index.html")
                {
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET").ConfigureAwait(false); return; }
                    byte[] htmlBytes = Encoding.UTF8.GetBytes(GetEmbeddedWebDashboardHtml());
                    await SendResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", htmlBytes).ConfigureAwait(false);
                    return;
                }

                // If Broadcast to All is ON and this is a command action POST, mirror across all child tabs
                bool isBroadcast = headers.TryGetValue("X-Skua-Broadcast", out string? bVal) && (bVal == "1" || bVal.Equals("true", StringComparison.OrdinalIgnoreCase));
                if (isBroadcast && method == "POST")
                {
                    var children = GetAllChildTabs();
                    var tasks = children.Select(c => ForwardRequestToChildAsync(null, c.port, method, path, query, body, headers, token));
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                    await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok($"Command mirrored to {children.Count} accounts")).ConfigureAwait(false);
                    return;
                }

                // Proxy any other endpoint (/api/status, /api/scripts, /api/player/..., etc.) to the child process
                int reqPid = 0;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    foreach (var part in query.Split('&'))
                    {
                        var kv = part.Split('=');
                        if (kv.Length == 2 && kv[0].Equals("tab", StringComparison.OrdinalIgnoreCase))
                        {
                            int.TryParse(kv[1], out reqPid);
                            break;
                        }
                    }
                }
                if (reqPid <= 0 && headers.TryGetValue("X-Skua-Tab", out string? tabHeader))
                {
                    int.TryParse(tabHeader, out reqPid);
                }

                int targetPort = ResolveChildPort(reqPid, out int resolvedPid);
                if (targetPort > 0)
                {
                    bool forwarded = await ForwardRequestToChildAsync(stream, targetPort, method, path, query, body, headers, token).ConfigureAwait(false);
                    if (forwarded)
                        return;
                }

                if (lowerPath == "/api/status")
                {
                    await SendJsonResponseAsync(stream, 200, BuildHostStatusFromSnapshot(resolvedPid > 0 ? resolvedPid : (reqPid > 0 ? reqPid : 0))).ConfigureAwait(false);
                    return;
                }

                if (targetPort > 0)
                {
                    await SendJsonResponseAsync(stream, 502, RemoteActionResult.Fail($"Proxy error: unable to communicate with game tab (PID {resolvedPid}, Port {targetPort})")).ConfigureAwait(false);
                    return;
                }

                await SendJsonResponseAsync(stream, 400, RemoteActionResult.Fail("No game tab is currently running. Please wait for the game to start.")).ConfigureAwait(false);
                return;
            }

            switch (lowerPath)
            {
                case "/api/ping":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    await SendJsonResponseAsync(stream, 200, new
                    {
                        status = "ok",
                        app = "VibeSkua",
                        version = ClientFileSources.AssemblyVersion,
                        port = _port,
                        timestamp = DateTime.UtcNow
                    }).ConfigureAwait(false);
                    break;

                case "/api/status":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    await SendJsonResponseAsync(stream, 200, BuildStatusSnapshot()).ConfigureAwait(false);
                    break;

                case "/api/scripts":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    await SendJsonResponseAsync(stream, 200, GetAvailableScripts(query)).ConfigureAwait(false);
                    break;

                case "/api/scripts/start":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var startRes = await HandleStartScriptAsync(body).ConfigureAwait(false);
                    await SendJsonResponseAsync(stream, startRes.success ? 200 : 400, startRes).ConfigureAwait(false);
                    break;

                case "/api/scripts/stop":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var stopRes = await HandleStopScriptAsync().ConfigureAwait(false);
                    await SendJsonResponseAsync(stream, stopRes.success ? 200 : 400, stopRes).ConfigureAwait(false);
                    break;

                case "/api/scripts/pause":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var pauseRes = HandleTogglePause();
                    await SendJsonResponseAsync(stream, pauseRes.success ? 200 : 400, pauseRes).ConfigureAwait(false);
                    break;

                case "/api/scripts/load":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var loadRes = HandleLoadScript(body);
                    await SendJsonResponseAsync(stream, loadRes.success ? 200 : 400, loadRes).ConfigureAwait(false);
                    break;

                case "/api/player/jump":
                case "/api/map/jump":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var jumpRes = HandleJump(body);
                    await SendJsonResponseAsync(stream, jumpRes.success ? 200 : 400, jumpRes).ConfigureAwait(false);
                    break;

                case "/api/player/goto":
                case "/api/map/goto":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var gotoRes = HandleGoto(body);
                    await SendJsonResponseAsync(stream, gotoRes.success ? 200 : 400, gotoRes).ConfigureAwait(false);
                    break;

                case "/api/player/join":
                case "/api/map/join":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var joinRes = HandleJoinMap(body);
                    await SendJsonResponseAsync(stream, joinRes.success ? 200 : 400, joinRes).ConfigureAwait(false);
                    break;

                case "/api/player/rest":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var restRes = HandleRest();
                    await SendJsonResponseAsync(stream, restRes.success ? 200 : 400, restRes).ConfigureAwait(false);
                    break;

                case "/api/player/relog":
                case "/api/player/relogin":
                case "/api/map/relog":
                case "/api/map/relogin":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var relogRes = HandleRelog(body);
                    await SendJsonResponseAsync(stream, relogRes.success ? 200 : 400, relogRes).ConfigureAwait(false);
                    break;

                case "/api/combat/attack":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var attackRes = HandleAttack(body);
                    await SendJsonResponseAsync(stream, attackRes.success ? 200 : 400, attackRes).ConfigureAwait(false);
                    break;

                case "/api/combat/skill":
                case "/api/combat/skill/1":
                case "/api/combat/skill/2":
                case "/api/combat/skill/3":
                case "/api/combat/skill/4":
                case "/api/combat/skill/5":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    int? pathSkill = null;
                    if (lowerPath.StartsWith("/api/combat/skill/"))
                    {
                        string sNum = lowerPath.Substring("/api/combat/skill/".Length);
                        if (int.TryParse(sNum, out int pSk)) pathSkill = pSk;
                    }
                    var skillRes = HandleUseSkill(body, pathSkill);
                    await SendJsonResponseAsync(stream, skillRes.success ? 200 : 400, skillRes).ConfigureAwait(false);
                    break;

                case "/api/combat/cancel-target":
                case "/api/combat/clear-target":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var cancelRes = HandleCancelTarget();
                    await SendJsonResponseAsync(stream, cancelRes.success ? 200 : 400, cancelRes).ConfigureAwait(false);
                    break;

                case "/api/drops":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    await SendJsonResponseAsync(stream, 200, GetCurrentDrops()).ConfigureAwait(false);
                    break;

                case "/api/quests":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    await SendJsonResponseAsync(stream, 200, GetCurrentQuests()).ConfigureAwait(false);
                    break;

                case "/api/quests/complete":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var completeQuestRes = HandleCompleteQuest(body);
                    await SendJsonResponseAsync(stream, completeQuestRes.success ? 200 : 400, completeQuestRes).ConfigureAwait(false);
                    break;

                case "/api/drops/pickup":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var pickupRes = HandlePickupDrop(body);
                    await SendJsonResponseAsync(stream, pickupRes.success ? 200 : 400, pickupRes).ConfigureAwait(false);
                    break;

                case "/api/drops/pickup-all":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var pickupAllRes = HandlePickupAllDrops();
                    await SendJsonResponseAsync(stream, pickupAllRes.success ? 200 : 400, pickupAllRes).ConfigureAwait(false);
                    break;

                case "/api/inventory":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    await SendJsonResponseAsync(stream, 200, GetInventoryItems()).ConfigureAwait(false);
                    break;

                case "/api/temp-items":
                case "/api/inventory/temp":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    await SendJsonResponseAsync(stream, 200, GetTempItems()).ConfigureAwait(false);
                    break;

                case "/api/bank":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    await SendJsonResponseAsync(stream, 200, GetBankItems()).ConfigureAwait(false);
                    break;

                case "/api/bank/open":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var openBankRes = HandleOpenBank();
                    await SendJsonResponseAsync(stream, openBankRes.success ? 200 : 400, openBankRes).ConfigureAwait(false);
                    break;

                case "/api/bank/load":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var loadBankRes = HandleLoadBank();
                    await SendJsonResponseAsync(stream, loadBankRes.success ? 200 : 400, loadBankRes).ConfigureAwait(false);
                    break;

                case "/api/inventory/equip":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var equipRes = HandleEquipItem(body);
                    await SendJsonResponseAsync(stream, equipRes.success ? 200 : 400, equipRes).ConfigureAwait(false);
                    break;

                case "/api/inventory/bank":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var toBankRes = HandleToBank(body);
                    await SendJsonResponseAsync(stream, toBankRes.success ? 200 : 400, toBankRes).ConfigureAwait(false);
                    break;

                case "/api/inventory/unbank":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var unbankRes = HandleToInventory(body);
                    await SendJsonResponseAsync(stream, unbankRes.success ? 200 : 400, unbankRes).ConfigureAwait(false);
                    break;

                case "/api/options":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    await SendJsonResponseAsync(stream, 200, GetOptionsState()).ConfigureAwait(false);
                    break;

                case "/api/options/toggle":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var toggleRes = HandleToggleOption(body);
                    await SendJsonResponseAsync(stream, toggleRes.success ? 200 : 400, toggleRes).ConfigureAwait(false);
                    break;

                case "/api/chat":
                case "/api/chat/messages":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    await SendJsonResponseAsync(stream, 200, GetChatMessages()).ConfigureAwait(false);
                    break;

                case "/api/chat/send":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var chatRes = HandleSendChat(body);
                    await SendJsonResponseAsync(stream, chatRes.success ? 200 : 400, chatRes).ConfigureAwait(false);
                    break;

                case "/api/packet/send":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var pktRes = HandleSendPacket(body);
                    await SendJsonResponseAsync(stream, pktRes.success ? 200 : 400, pktRes).ConfigureAwait(false);
                    break;

                case "/api/logs":
                case "/api/logs/script":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    var logsSvc = _serviceProvider.GetService<ILogService>();
                    var sLogs = logsSvc?.GetLogs(LogType.Script) ?? new();
                    var logsResult = new
                    {
                        success = true,
                        count = sLogs.Count,
                        last_log = sLogs.LastOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty,
                        logs = sLogs.Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(50).ToList()
                    };
                    await SendJsonResponseAsync(stream, 200, logsResult).ConfigureAwait(false);
                    break;

                case "":
                case "/":
                case "/index.html":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    byte[] htmlBytes = Encoding.UTF8.GetBytes(GetEmbeddedWebDashboardHtml());
                    await SendResponseAsync(stream, 200, "OK", "text/html; charset=utf-8", htmlBytes).ConfigureAwait(false);
                    break;

                case "/api/tabs":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    await SendJsonResponseAsync(stream, 200, new
                    {
                        active_pid = Environment.ProcessId,
                        tabs = GetTabsInfo()
                    }).ConfigureAwait(false);
                    break;

                case "/api/tabs/select":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok($"Selected tab {Environment.ProcessId}")).ConfigureAwait(false);
                    break;

                case "/api/tabs/focus":
                case "/api/tabs/focus-pc":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    try
                    {
                        var proc = Process.GetCurrentProcess();
                        if (proc.MainWindowHandle != IntPtr.Zero)
                        {
                            SetForegroundWindow(proc.MainWindowHandle);
                        }
                    }
                    catch { }
                    await SendJsonResponseAsync(stream, 200, RemoteActionResult.Ok($"Focused tab {Environment.ProcessId} on PC")).ConfigureAwait(false);
                    break;

                case "/api/accounts":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    var stAccs = _settingsService.Get<Dictionary<string, AccountData>>("ManagedAccounts") ?? new();
                    var stGroups = _settingsService.Get<List<GroupData>>("AccountGroups") ?? new();
                    await SendJsonResponseAsync(stream, 200, new
                    {
                        accounts = stAccs.Select(kv => new
                        {
                            username = kv.Key,
                            display_name = !string.IsNullOrWhiteSpace(kv.Value.DisplayName) ? kv.Value.DisplayName : kv.Key,
                            tags = kv.Value.Tags ?? new List<string>()
                        }).ToList(),
                        groups = stGroups.Select(g => new
                        {
                            name = g.Name,
                            accounts = g.Accounts ?? new List<string>()
                        }).ToList()
                    }).ConfigureAwait(false);
                    break;

                case "/api/accounts/launch":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var launchRes = HandleLaunchAccount(body);
                    await SendJsonResponseAsync(stream, launchRes.success ? 200 : 400, launchRes).ConfigureAwait(false);
                    break;

                case "/api/fleet":
                case "/api/fleet/status":
                    if (method != "GET") { await SendMethodNotAllowedAsync(stream, "GET"); break; }
                    var flTabs = GetTabsInfo();
                    await SendJsonResponseAsync(stream, 200, new
                    {
                        active_pid = Environment.ProcessId,
                        count = flTabs.Count,
                        tabs = flTabs
                    }).ConfigureAwait(false);
                    break;

                case "/api/fleet/start-all":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var flStart = await HandleStartScriptAsync(body).ConfigureAwait(false);
                    await SendJsonResponseAsync(stream, flStart.success ? 200 : 400, flStart).ConfigureAwait(false);
                    break;

                case "/api/fleet/stop-all":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var flStop = await HandleStopScriptAsync().ConfigureAwait(false);
                    await SendJsonResponseAsync(stream, flStop.success ? 200 : 400, flStop).ConfigureAwait(false);
                    break;

                case "/api/fleet/load-script":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var flLoad = HandleLoadScript(body);
                    await SendJsonResponseAsync(stream, flLoad.success ? 200 : 400, flLoad).ConfigureAwait(false);
                    break;

                case "/api/fleet/headless":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    bool flH = true;
                    if (!string.IsNullOrWhiteSpace(body))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(body);
                            if (doc.RootElement.TryGetProperty("value", out var v)) flH = v.GetBoolean();
                            else if (doc.RootElement.TryGetProperty("enabled", out var e)) flH = e.GetBoolean();
                        }
                        catch { }
                    }
                    var flHRes = HandleToggleOption(System.Text.Json.JsonSerializer.Serialize(new { option = "HeadlessMode", value = flH }));
                    await SendJsonResponseAsync(stream, flHRes.success ? 200 : 400, flHRes).ConfigureAwait(false);
                    break;

                case "/api/fleet/relogin-all":
                    if (method != "POST") { await SendMethodNotAllowedAsync(stream, "POST"); break; }
                    var flRelog = HandleRelog(body);
                    await SendJsonResponseAsync(stream, flRelog.success ? 200 : 400, flRelog).ConfigureAwait(false);
                    break;

                default:
                    await SendJsonResponseAsync(stream, 404, new { error = "Not found", path }).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            await SendJsonResponseAsync(stream, 500, new { error = ex.Message }).ConfigureAwait(false);
        }
    }

    private int ResolveChildPort(int preferredPid, out int resolvedPid)
    {
        resolvedPid = 0;
        string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
        if (!Directory.Exists(dir))
            return 0;

        if (preferredPid > 0)
        {
            string file = Path.Combine(dir, $"{preferredPid}.remote.txt");
            if (File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out int port) && port > 0)
            {
                if (IsProcessAlive(preferredPid))
                {
                    resolvedPid = preferredPid;
                    return port;
                }
                else
                {
                    CleanupStaleTabFiles(dir, preferredPid);
                }
            }
        }

        if (ActiveChildPidProvider != null)
        {
            try
            {
                int activePid = ActiveChildPidProvider();
                if (activePid > 0)
                {
                    string file = Path.Combine(dir, $"{activePid}.remote.txt");
                    if (File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out int port) && port > 0)
                    {
                        if (IsProcessAlive(activePid))
                        {
                            resolvedPid = activePid;
                            return port;
                        }
                        else
                        {
                            CleanupStaleTabFiles(dir, activePid);
                        }
                    }
                }
            }
            catch { }
        }

        try
        {
            var files = Directory.GetFiles(dir, "*.remote.txt")
                .OrderByDescending(File.GetLastWriteTimeUtc);

            foreach (var f in files)
            {
                string fileName = Path.GetFileName(f);
                string pidStr = fileName.Split('.')[0];
                if (int.TryParse(pidStr, out int pid))
                {
                    if (IsProcessAlive(pid))
                    {
                        if (int.TryParse(File.ReadAllText(f).Trim(), out int port) && port > 0)
                        {
                            resolvedPid = pid;
                            return port;
                        }
                    }
                    else
                    {
                        CleanupStaleTabFiles(dir, pid);
                    }
                }
            }
        }
        catch { }

        return 0;
    }

    private static void CleanupStaleTabFiles(string dir, int pid)
    {
        try { string f = Path.Combine(dir, $"{pid}.remote.txt"); if (File.Exists(f)) File.Delete(f); } catch { }
        try { string f = Path.Combine(dir, $"{pid}.txt"); if (File.Exists(f)) File.Delete(f); } catch { }
        try { string f = Path.Combine(dir, $"{pid}.location.json"); if (File.Exists(f)) File.Delete(f); } catch { }
        try { string f = Path.Combine(dir, $"{pid}.location.json.tmp"); if (File.Exists(f)) File.Delete(f); } catch { }
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            var p = System.Diagnostics.Process.GetProcessById(pid);
            if (p.HasExited) return false;
            string name = p.ProcessName;
            return name.Contains("VibeSkua", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Skua", StringComparison.OrdinalIgnoreCase)
                || name.Contains("dotnet", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private List<object> GetTabsInfo()
    {
        if (TabsInfoProvider != null)
        {
            try
            {
                var tabs = TabsInfoProvider();
                if (tabs != null && tabs.Count > 0)
                    return tabs;
            }
            catch { }
        }

        var result = new List<object>();
        string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
        if (Directory.Exists(dir))
        {
            try
            {
                int activePid = 0;
                if (ActiveChildPidProvider != null)
                {
                    try { activePid = ActiveChildPidProvider(); } catch { }
                }

                int tabIndex = 1;
                var files = Directory.GetFiles(dir, "*.remote.txt");
                foreach (var f in files)
                {
                    string fileName = Path.GetFileName(f);
                    string pidStr = fileName.Split('.')[0];
                    if (int.TryParse(pidStr, out int pid) && IsProcessAlive(pid))
                    {
                        string username = $"Skua {tabIndex}";
                        string userFile = Path.Combine(dir, $"{pid}.txt");
                        if (File.Exists(userFile))
                        {
                            try
                            {
                                using var ufs = new FileStream(userFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                using var ureader = new StreamReader(ufs);
                                string u = ureader.ReadToEnd().Trim();
                                if (!string.IsNullOrWhiteSpace(u) && !u.StartsWith("Tab ", StringComparison.OrdinalIgnoreCase))
                                    username = u;
                            }
                            catch { }
                        }
                        tabIndex++;

                        string map = "";
                        string cell = "";
                        string pad = "";
                        string server = "";
                        int hp = 0;
                        int maxHp = 0;
                        int mp = 0;
                        int maxMp = 0;
                        bool isScriptRunning = false;
                        string scriptName = "";

                        string locFile = Path.Combine(dir, $"{pid}.location.json");
                        if (File.Exists(locFile))
                        {
                            try
                            {
                                using var lfs = new FileStream(locFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                                using var doc = JsonDocument.Parse(lfs);
                                var root = doc.RootElement;
                                if (root.TryGetProperty("Map", out var mpProp)) map = mpProp.GetString() ?? "";
                                if (root.TryGetProperty("Cell", out var clProp)) cell = clProp.GetString() ?? "";
                                if (root.TryGetProperty("Pad", out var pdProp)) pad = pdProp.GetString() ?? "";
                                if (root.TryGetProperty("Server", out var svProp)) server = svProp.GetString() ?? "";
                                if (root.TryGetProperty("Hp", out var hProp)) hp = hProp.GetInt32();
                                if (root.TryGetProperty("MaxHp", out var mhProp)) maxHp = mhProp.GetInt32();
                                if (root.TryGetProperty("Mp", out var mProp)) mp = mProp.GetInt32();
                                if (root.TryGetProperty("MaxMp", out var mmProp)) maxMp = mmProp.GetInt32();
                                if (root.TryGetProperty("ScriptRunning", out var srProp)) isScriptRunning = srProp.GetBoolean();
                                if (root.TryGetProperty("ScriptName", out var snProp)) scriptName = snProp.GetString() ?? "";
                            }
                            catch { }
                        }

                        result.Add(new
                        {
                            pid = pid,
                            username = username,
                            is_active = (pid == activePid),
                            map = map,
                            cell = cell,
                            pad = pad,
                            server = server,
                            hp = hp,
                            max_hp = maxHp,
                            mp = mp,
                            max_mp = maxMp,
                            script_running = isScriptRunning,
                            script_name = scriptName
                        });
                    }
                }
            }
            catch { }
        }

        if (result.Count == 0)
        {
            if (IsManagerMode)
                return result;

            var bot = _serviceProvider.GetService<IScriptInterface>();
            var scriptMgr = _serviceProvider.GetService<IScriptManager>();
            string username = bot?.Player?.Username ?? string.Empty;
            int myPid = Environment.ProcessId;
            result.Add(new
            {
                pid = myPid,
                username = (!string.IsNullOrWhiteSpace(username) && username != "null") ? username : "Skua 1",
                is_active = true,
                map = bot?.Map?.Name ?? "",
                cell = bot?.Player?.Cell ?? "",
                pad = bot?.Player?.Pad ?? "",
                server = bot?.Servers?.LastName ?? "",
                hp = bot?.Player?.Health ?? 0,
                max_hp = bot?.Player?.MaxHealth ?? 0,
                mp = bot?.Player?.Mana ?? 0,
                max_mp = bot?.Player?.MaxMana ?? 0,
                script_running = scriptMgr?.ScriptRunning ?? false,
                script_name = Path.GetFileName(scriptMgr?.LoadedScript ?? "")
            });
        }

        return result;
    }

    private async Task<bool> ForwardRequestToChildAsync(NetworkStream? stream, int targetPort, string method, string path, string query, string body, Dictionary<string, string> headers, CancellationToken token)
    {
        try
        {
            string url = $"http://127.0.0.1:{targetPort}{path}{(string.IsNullOrEmpty(query) ? "" : "?" + query)}";
            using var req = new HttpRequestMessage(new HttpMethod(method), url);

            if ((method == "POST" || method == "PUT") && body != null)
            {
                req.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            foreach (var h in headers)
            {
                if (h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ||
                    h.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                    h.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    continue;

                req.Headers.TryAddWithoutValidation(h.Key, h.Value);
            }

            using var resp = await _proxyClient.SendAsync(req, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
            int statusCode = (int)resp.StatusCode;
            string statusText = resp.ReasonPhrase ?? "OK";
            string contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json; charset=utf-8";
            byte[] respBytes = await resp.Content.ReadAsByteArrayAsync(token).ConfigureAwait(false);

            if (stream != null)
            {
                await SendResponseAsync(stream, statusCode, statusText, contentType, respBytes).ConfigureAwait(false);
            }
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RemoteControlService] Proxy error forwarding to port {targetPort}: {ex.Message}");
            return false;
        }
    }

    private static object BuildHostStatusFromSnapshot(int preferredPid = 0)
    {
        string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
        string? candidateFile = null;

        if (preferredPid > 0)
        {
            string loc = Path.Combine(dir, $"{preferredPid}.location.json");
            if (File.Exists(loc))
                candidateFile = loc;
        }

        if (candidateFile == null && Directory.Exists(dir))
        {
            try
            {
                var files = Directory.GetFiles(dir, "*.location.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc);
                foreach (var f in files)
                {
                    string pidStr = Path.GetFileName(f).Split('.')[0];
                    if (int.TryParse(pidStr, out int pid) && IsProcessAlive(pid))
                    {
                        candidateFile = f;
                        break;
                    }
                }
            }
            catch { }
        }

        string username = "Waiting for game tab...";
        string map = "None";
        string cell = "Enter";
        string pad = "Spawn";
        int roomId = 0;
        int level = 0, hp = 0, maxHp = 100, mp = 0, maxMp = 100, gold = 0, coins = 0, classRank = 0;
        string currentClass = "None";
        bool inCombat = false, alive = false, hasTarget = false, loggedIn = false, playing = false;
        string targetName = string.Empty;
        int targetHp = 0, targetMaxHp = 0;

        if (candidateFile != null && File.Exists(candidateFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(candidateFile));
                var root = doc.RootElement;
                if (root.TryGetProperty("Username", out var uProp) && !string.IsNullOrWhiteSpace(uProp.GetString()))
                    username = uProp.GetString()!;
                if (root.TryGetProperty("Map", out var mProp)) map = mProp.GetString() ?? map;
                if (root.TryGetProperty("Room", out var rProp) && int.TryParse(rProp.GetString(), out int r)) roomId = r;
                if (root.TryGetProperty("Cell", out var cProp)) cell = cProp.GetString() ?? cell;
                if (root.TryGetProperty("Pad", out var pProp)) pad = pProp.GetString() ?? pad;
                if (root.TryGetProperty("Level", out var lProp)) level = lProp.GetInt32();
                if (root.TryGetProperty("HP", out var hpProp)) hp = hpProp.GetInt32();
                if (root.TryGetProperty("MaxHP", out var mhpProp)) maxHp = mhpProp.GetInt32();
                if (root.TryGetProperty("MP", out var mpProp)) mp = mpProp.GetInt32();
                if (root.TryGetProperty("MaxMP", out var mmpProp)) maxMp = mmpProp.GetInt32();
                if (root.TryGetProperty("Gold", out var gProp)) gold = gProp.GetInt32();
                if (root.TryGetProperty("Coins", out var coProp)) coins = coProp.GetInt32();
                if (root.TryGetProperty("CurrentClass", out var clsProp)) currentClass = clsProp.GetString() ?? currentClass;
                if (root.TryGetProperty("ClassRank", out var crProp)) classRank = crProp.GetInt32();
                if (root.TryGetProperty("InCombat", out var icProp)) inCombat = icProp.GetBoolean();
                if (root.TryGetProperty("Alive", out var alProp)) alive = alProp.GetBoolean();
                if (root.TryGetProperty("HasTarget", out var htProp)) hasTarget = htProp.GetBoolean();
                if (root.TryGetProperty("TargetName", out var tnProp)) targetName = tnProp.GetString() ?? "";
                if (root.TryGetProperty("TargetHP", out var thpProp)) targetHp = thpProp.GetInt32();
                if (root.TryGetProperty("TargetMaxHP", out var tmhpProp)) targetMaxHp = tmhpProp.GetInt32();
                if (root.TryGetProperty("LoggedIn", out var liProp)) loggedIn = liProp.GetBoolean();
                if (root.TryGetProperty("Playing", out var plProp)) playing = plProp.GetBoolean();
            }
            catch { }
        }

        if (maxHp <= 0) maxHp = hp > 0 ? hp : 100;
        if (maxMp <= 0) maxMp = mp > 0 ? mp : 100;

        return new
        {
            app = "VibeSkua",
            version = ClientFileSources.AssemblyVersion,
            logged_in = loggedIn || (hp > 0 && username != "Waiting for game tab..."),
            playing = playing || (hp > 0 && username != "Waiting for game tab..."),
            username = username,
            level = level,
            gold = gold,
            coins = coins,
            hp = hp,
            max_hp = maxHp,
            mp = mp,
            max_mp = maxMp,
            current_class = currentClass,
            class_rank = classRank,
            in_combat = inCombat,
            alive = alive || hp > 0,
            afk = false,
            map = map,
            room_id = roomId,
            cell = cell,
            pad = pad,
            server = "None",
            has_target = hasTarget,
            target_name = targetName,
            target_hp = targetHp,
            target_max_hp = targetMaxHp,
            script_running = false,
            script_paused = false,
            loaded_script = string.Empty,
            loaded_script_path = string.Empty,
            kills = 0,
            deaths = 0,
            drops = 0,
            quests_completed = 0,
            relogins = 0,
            uptime_seconds = 0,
            uptime = "00:00:00",
            inventory_count = 0,
            inventory_used = 0,
            inventory_max = 0,
            bank_count = 0,
            bank_used = 0,
            bank_max = 0,
            bank_loaded = false,
            active_quests = Array.Empty<object>(),
            current_drops = Array.Empty<string>(),
            temp_items = Array.Empty<object>(),
            options = new { },
            recent_chat = Array.Empty<object>()
        };
    }

    private static object BuildEmptyHostStatus()
    {
        return BuildHostStatusFromSnapshot(0);
    }

    private static async Task SendMethodNotAllowedAsync(NetworkStream stream, string allowed)
    {
        await SendResponseAsync(stream, 405, "Method Not Allowed", "text/plain", Encoding.UTF8.GetBytes($"Use {allowed}")).ConfigureAwait(false);
    }

    private object BuildStatusSnapshot()
    {
        EnsureChatHooked();
        var bot = _serviceProvider.GetService<IScriptInterface>();
        var manager = _serviceProvider.GetService<IScriptManager>();

        bool loggedIn = bot?.Player != null && bot.Player.LoggedIn;
        bool playing = bot?.Player != null && bot.Player.Playing;

        string username = bot?.Player?.Username?.Trim('\"') ?? "Unknown";
        if (username == "loginInfo.strUsername" || string.IsNullOrWhiteSpace(username) || username == "Unknown" || username == "VibeSkua Bot")
        {
            try
            {
                string tabUserFile = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs", $"{Environment.ProcessId}.txt");
                if (File.Exists(tabUserFile))
                {
                    string u = File.ReadAllText(tabUserFile).Trim();
                    if (!string.IsNullOrWhiteSpace(u) && !u.StartsWith("User ") && !u.StartsWith("Skua "))
                        username = u;
                }
            }
            catch { }
        }
        if (username == "loginInfo.strUsername" || string.IsNullOrWhiteSpace(username))
            username = bot?.Servers?.CachedUsername ?? "VibeSkua Bot";

        int level = bot?.Player?.Level ?? 0;
        int hp = bot?.Player?.Health ?? 0;
        int maxHp = bot?.Player?.MaxHealth ?? 0;
        int mp = bot?.Player?.Mana ?? 0;
        int maxMp = bot?.Player?.MaxMana ?? 0;
        int gold = bot?.Player?.Gold ?? 0;
        int coins = bot?.Player?.Coins ?? 0;
        string currentClass = bot?.Player?.CurrentClass?.Name ?? "None";
        int classRank = bot?.Player?.CurrentClassRank ?? 0;

        // Flash fallback if player dataLeaf or direct props are uninitialized / Lag Killer active
        if (bot?.Flash != null && bot.Flash.IsWorldLoaded)
        {
            if (level <= 0)
            {
                try { level = bot.Flash.GetGameObject<int>("world.myAvatar.dataLeaf.intLevel"); } catch { }
                if (level <= 0) { try { level = bot.Flash.GetGameObject<int>("world.myAvatar.objData.intLevel"); } catch { } }
            }
            if (hp <= 0)
            {
                try { hp = bot.Flash.GetGameObject<int>("world.myAvatar.dataLeaf.intHP"); } catch { }
                if (hp <= 0) { try { hp = bot.Flash.GetGameObject<int>("world.myAvatar.objData.intHP"); } catch { } }
            }
            if (maxHp <= 0)
            {
                try { maxHp = bot.Flash.GetGameObject<int>("world.myAvatar.dataLeaf.intHPMax"); } catch { }
                if (maxHp <= 0) { try { maxHp = bot.Flash.GetGameObject<int>("world.myAvatar.objData.intHPMax"); } catch { } }
            }
            if (mp <= 0)
            {
                try { mp = bot.Flash.GetGameObject<int>("world.myAvatar.dataLeaf.intMP"); } catch { }
                if (mp <= 0) { try { mp = bot.Flash.GetGameObject<int>("world.myAvatar.objData.intMP"); } catch { } }
            }
            if (maxMp <= 0)
            {
                try { maxMp = bot.Flash.GetGameObject<int>("world.myAvatar.dataLeaf.intMPMax"); } catch { }
                if (maxMp <= 0) { try { maxMp = bot.Flash.GetGameObject<int>("world.myAvatar.objData.intMPMax"); } catch { } }
            }
            if (gold <= 0)
            {
                try { gold = bot.Flash.GetGameObject<int>("world.myAvatar.objData.intGold"); } catch { }
            }
            if (coins <= 0)
            {
                try { coins = bot.Flash.GetGameObject<int>("world.myAvatar.objData.intCoins"); } catch { }
            }
            if (string.IsNullOrWhiteSpace(username) || username == "loginInfo.strUsername" || username == "Unknown" || username == "VibeSkua Bot")
            {
                try
                {
                    string? u = bot.Flash.GetGameObject<string>("world.myAvatar.objData.strUsername");
                    if (!string.IsNullOrWhiteSpace(u) && u != "null") username = u.Trim('\"');
                }
                catch { }
                if (string.IsNullOrWhiteSpace(username) || username == "loginInfo.strUsername" || username == "Unknown")
                {
                    try
                    {
                        string? u = bot.Flash.GetGameObject<string>("world.strUsername");
                        if (!string.IsNullOrWhiteSpace(u) && u != "null") username = u.Trim('\"');
                    }
                    catch { }
                }
                if (string.IsNullOrWhiteSpace(username) || username == "loginInfo.strUsername" || username == "Unknown")
                {
                    try
                    {
                        string? u = bot.Flash.GetGameObject<string>("sfc.myUserName");
                        if (!string.IsNullOrWhiteSpace(u) && u != "null") username = u.Trim('\"');
                    }
                    catch { }
                }
                if ((string.IsNullOrWhiteSpace(username) || username == "loginInfo.strUsername" || username == "Unknown") && !string.IsNullOrWhiteSpace(bot?.Options?.CustomName))
                {
                    username = bot.Options.CustomName;
                }
            }
            if (currentClass == "None" || string.IsNullOrWhiteSpace(currentClass))
            {
                try
                {
                    string c = bot.Flash.GetGameObject<string>("world.myAvatar.objData.strClassName");
                    if (!string.IsNullOrWhiteSpace(c) && c != "null") currentClass = c;
                }
                catch { }
            }
            if (!loggedIn)
            {
                try { loggedIn = bot.Flash.Call<bool>("isLoggedIn"); } catch { }
                if (!loggedIn)
                {
                    try { loggedIn = !bot.Flash.IsNull("world.myAvatar"); } catch { }
                }
                if (!loggedIn)
                {
                    try { loggedIn = bot.Flash.IsWorldLoaded && (hp > 0 || bot.Flash.GetGameObject<int>("world.myAvatar.dataLeaf.intHP") > 0 || bot.Flash.GetGameObject<int>("world.myAvatar.objData.intHP") > 0); } catch { }
                }
            }
            if (!playing)
            {
                try { playing = loggedIn && (hp > 0 || bot.Flash.IsWorldLoaded); } catch { }
            }
        }

        // Backfill from local disk location snapshot written by UI thread timer
        string locFile = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs", $"{Environment.ProcessId}.location.json");
        if (File.Exists(locFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(locFile));
                var root = doc.RootElement;
                if ((string.IsNullOrWhiteSpace(username) || username == "Unknown" || username == "VibeSkua Bot" || username == "loginInfo.strUsername") &&
                    root.TryGetProperty("Username", out var uProp) && !string.IsNullOrWhiteSpace(uProp.GetString()))
                {
                    username = uProp.GetString()!;
                }
                if (hp <= 0 && root.TryGetProperty("HP", out var hpProp))
                    hp = hpProp.GetInt32();
                if (maxHp <= 0 && root.TryGetProperty("MaxHP", out var mhpProp))
                    maxHp = mhpProp.GetInt32();
                if (mp <= 0 && root.TryGetProperty("MP", out var mpProp))
                    mp = mpProp.GetInt32();
                if (maxMp <= 0 && root.TryGetProperty("MaxMP", out var mmpProp))
                    maxMp = mmpProp.GetInt32();
                if (level <= 0 && root.TryGetProperty("Level", out var lvlProp))
                    level = lvlProp.GetInt32();
                if (gold <= 0 && root.TryGetProperty("Gold", out var goldProp))
                    gold = goldProp.GetInt32();
                if (coins <= 0 && root.TryGetProperty("Coins", out var coinsProp))
                    coins = coinsProp.GetInt32();
                if ((currentClass == "None" || string.IsNullOrWhiteSpace(currentClass)) &&
                    root.TryGetProperty("CurrentClass", out var clsProp) && !string.IsNullOrWhiteSpace(clsProp.GetString()))
                {
                    currentClass = clsProp.GetString()!;
                }
                if (classRank <= 0 && root.TryGetProperty("ClassRank", out var crProp))
                    classRank = crProp.GetInt32();
                if (!loggedIn && root.TryGetProperty("LoggedIn", out var liProp))
                    loggedIn = liProp.GetBoolean();
                if (!playing && root.TryGetProperty("Playing", out var plProp))
                    playing = plProp.GetBoolean();
            }
            catch { }
        }

        if (maxHp <= 0 && hp > 0)
            maxHp = hp;
        if (maxMp <= 0)
            maxMp = mp > 0 ? mp : 100;

        if (hp > 0 && !string.IsNullOrWhiteSpace(username) && username != "Unknown" && username != "VibeSkua Bot")
        {
            loggedIn = true;
            playing = true;
        }

        string targetName = bot?.Player?.Target?.Name ?? string.Empty;
        int targetHp = bot?.Player?.Target?.HP ?? 0;
        int targetMaxHp = bot?.Player?.Target?.MaxHP ?? 0;

        if (string.IsNullOrWhiteSpace(targetName) && bot?.Flash != null && bot.Flash.IsWorldLoaded)
        {
            try
            {
                string tName = bot.Flash.GetGameObject<string>("world.myAvatar.target.objData.strMonName");
                if (string.IsNullOrWhiteSpace(tName) || tName == "null")
                    tName = bot.Flash.GetGameObject<string>("world.myAvatar.target.dataLeaf.strMonName");

                if (!string.IsNullOrWhiteSpace(tName) && tName != "null")
                {
                    targetName = tName;
                    targetHp = bot.Flash.GetGameObject<int>("world.myAvatar.target.dataLeaf.intHP");
                    if (targetHp <= 0)
                        targetHp = bot.Flash.GetGameObject<int>("world.myAvatar.target.objData.intHP");

                    targetMaxHp = bot.Flash.GetGameObject<int>("world.myAvatar.target.dataLeaf.intHPMax");
                    if (targetMaxHp <= 0)
                        targetMaxHp = bot.Flash.GetGameObject<int>("world.myAvatar.target.objData.intHPMax");
                }
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(targetName) && File.Exists(locFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(locFile));
                var root = doc.RootElement;
                if (root.TryGetProperty("TargetName", out var tnProp) && !string.IsNullOrWhiteSpace(tnProp.GetString()))
                {
                    targetName = tnProp.GetString()!;
                    if (root.TryGetProperty("TargetHP", out var thpProp)) targetHp = thpProp.GetInt32();
                    if (root.TryGetProperty("TargetMaxHP", out var tmhpProp)) targetMaxHp = tmhpProp.GetInt32();
                }
            }
            catch { }
        }

        string mapName = bot?.Map?.FullName ?? bot?.Map?.Name ?? "Unknown";
        string cell = bot?.Player?.Cell ?? "Enter";
        string pad = bot?.Player?.Pad ?? "Spawn";
        int roomId = bot?.Map?.RoomID ?? 0;

        if ((mapName == "Unknown" || string.IsNullOrWhiteSpace(mapName)) && File.Exists(locFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(locFile));
                var root = doc.RootElement;
                if (root.TryGetProperty("Map", out var mProp) && !string.IsNullOrWhiteSpace(mProp.GetString()))
                    mapName = mProp.GetString()!;
                if (root.TryGetProperty("Room", out var rProp) && int.TryParse(rProp.GetString(), out int r) && r > 0)
                    roomId = r;
                if (root.TryGetProperty("Cell", out var cProp) && !string.IsNullOrWhiteSpace(cProp.GetString()))
                    cell = cProp.GetString()!;
                if (root.TryGetProperty("Pad", out var pProp) && !string.IsNullOrWhiteSpace(pProp.GetString()))
                    pad = pProp.GetString()!;
            }
            catch { }
        }

        string scriptName = string.Empty;
        string scriptPath = string.Empty;
        bool scriptRunning = false;
        bool scriptPaused = false;

        if (manager != null)
        {
            scriptRunning = manager.ScriptRunning;
            scriptPaused = manager.ScriptPaused;
            scriptPath = manager.LoadedScript ?? string.Empty;
            scriptName = !string.IsNullOrWhiteSpace(scriptPath) ? Path.GetFileName(scriptPath) : string.Empty;
        }

        var activeQuests = GetActiveQuestsList(bot);

        var currentDrops = new List<string>();
        if (bot?.Drops?.CurrentDrops != null)
        {
            foreach (var d in bot.Drops.CurrentDrops)
            {
                if (!string.IsNullOrWhiteSpace(d) && !currentDrops.Contains(d))
                    currentDrops.Add(d);
            }
        }
        if (bot?.Drops?.CurrentDropInfos != null)
        {
            foreach (var di in bot.Drops.CurrentDropInfos)
            {
                if (di != null && !string.IsNullOrWhiteSpace(di.Name) && !currentDrops.Contains(di.Name))
                {
                    currentDrops.Add(di.Name);
                }
            }
        }
        if (bot?.Flash != null && bot.Flash.IsWorldLoaded)
        {
            try
            {
                int dropCount = bot.Flash.GetGameObject<int>("world.ui.mcDropMenu.numChildren");
                for (int i = 0; i < dropCount; i++)
                {
                    string dropName = bot.Flash.GetGameObject<string>($"world.ui.mcDropMenu.getChildAt({i}).cnt.strName");
                    if (!string.IsNullOrWhiteSpace(dropName) && dropName != "null" && !currentDrops.Contains(dropName))
                    {
                        currentDrops.Add(dropName);
                    }
                }
            }
            catch { }
        }
        if (bot?.Drops?.ToPickup != null)
        {
            try
            {
                foreach (var tp in bot.Drops.ToPickup)
                {
                    if (!string.IsNullOrWhiteSpace(tp) && !currentDrops.Contains(tp))
                        currentDrops.Add(tp);
                }
            }
            catch { }
        }

        int kills = bot?.Stats?.Kills ?? 0;
        int deaths = bot?.Stats?.Deaths ?? 0;
        int drops = bot?.Stats?.Drops ?? 0;
        int quests = bot?.Stats?.QuestsCompleted ?? 0;
        int relogs = bot?.Stats?.Relogins ?? 0;

        TimeSpan uptimeSpan = (bot?.Stats != null && bot.Stats.Time != TimeSpan.Zero)
            ? bot.Stats.Time
            : DateTime.UtcNow - _serverStartTime;

        int uptimeSec = (int)uptimeSpan.TotalSeconds;
        string uptimeFmt = uptimeSpan.ToString(@"hh\:mm\:ss");

        var logService = _serviceProvider.GetService<ILogService>();
        List<string> scriptLogs = logService?.GetLogs(LogType.Script) ?? new();
        string lastScriptLog = scriptLogs.LastOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? string.Empty;
        var recentScriptLogs = scriptLogs.Where(l => !string.IsNullOrWhiteSpace(l)).TakeLast(10).ToList();
        string scriptStatus = scriptRunning
            ? (scriptPaused ? "PAUSED" : "RUNNING")
            : (!string.IsNullOrWhiteSpace(scriptName) ? "LOADED" : "STOPPED");

        return new
        {
            app = "VibeSkua",
            version = ClientFileSources.AssemblyVersion,
            logged_in = loggedIn,
            playing = playing,
            username = username,
            level = level,
            gold = gold,
            coins = coins,
            hp = hp,
            max_hp = maxHp,
            mp = mp,
            max_mp = maxMp,
            current_class = currentClass,
            class_rank = classRank,
            in_combat = bot?.Player?.InCombat ?? false,
            alive = bot?.Player?.Alive ?? (hp > 0),
            afk = bot?.Player?.AFK ?? false,
            map = mapName,
            room_id = roomId,
            cell = cell,
            pad = pad,
            server = bot?.Servers?.LastName ?? "Unknown",
            has_target = !string.IsNullOrWhiteSpace(targetName) || (bot?.Player?.HasTarget ?? false),
            target_name = targetName,
            target_hp = targetHp,
            target_max_hp = targetMaxHp,
            script_running = scriptRunning,
            script_paused = scriptPaused,
            script_status = scriptStatus,
            loaded_script = scriptName,
            loaded_script_path = scriptPath,
            script_last_log = lastScriptLog,
            script_logs = recentScriptLogs,
            kills = kills,
            deaths = deaths,
            drops = drops,
            quests_completed = quests,
            relogins = relogs,
            uptime_seconds = uptimeSec,
            uptime = uptimeFmt,
            inventory_count = bot?.Inventory?.Items?.Count ?? 0,
            inventory_used = bot?.Inventory?.UsedSlots ?? (bot?.Inventory?.Items?.Count ?? 0),
            inventory_max = bot?.Inventory?.Slots ?? 0,
            bank_count = bot?.Bank?.Items?.Count ?? 0,
            bank_used = bot?.Bank?.UsedSlots ?? 0,
            bank_max = bot?.Bank?.Slots ?? 0,
            bank_loaded = bot?.Bank?.Loaded ?? false,
            active_quests = activeQuests,
            current_drops = currentDrops,
            temp_items = GetTempItemsList(bot),
            options = GetOptionsState(),
            recent_chat = _chatMessages.TakeLast(30).ToArray()
        };
    }

    private static string DetermineScriptCategory(string relPath)
    {
        string norm = "/" + relPath.Replace('\\', '/').TrimStart('/');
        if (norm.Contains("/Classes/", StringComparison.OrdinalIgnoreCase) ||
            norm.Contains("/Class/", StringComparison.OrdinalIgnoreCase) ||
            norm.EndsWith("Class.cs", StringComparison.OrdinalIgnoreCase) ||
            norm.EndsWith("Classes.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "Classes";
        }
        if (norm.Contains("/REP/", StringComparison.OrdinalIgnoreCase) ||
            norm.Contains("/Rep/", StringComparison.OrdinalIgnoreCase) ||
            norm.Contains("/Reputation/", StringComparison.OrdinalIgnoreCase) ||
            norm.EndsWith("REP.cs", StringComparison.OrdinalIgnoreCase) ||
            norm.EndsWith("Rep.cs", StringComparison.OrdinalIgnoreCase))
        {
            return "Rep";
        }
        string stripped = norm.TrimStart('/');
        int slashIdx = stripped.IndexOf('/');
        return slashIdx > 0 ? stripped.Substring(0, slashIdx) : "Other";
    }

    private object GetAvailableScripts(string query)
    {
        var scripts = new List<object>();
        string filter = string.Empty;
        string categoryFilter = string.Empty;
        if (!string.IsNullOrWhiteSpace(query))
        {
            var pairs = query.Split('&');
            foreach (var p in pairs)
            {
                var kv = p.Split('=');
                if (kv.Length == 2)
                {
                    if (kv[0].Equals("search", StringComparison.OrdinalIgnoreCase))
                        filter = Uri.UnescapeDataString(kv[1]).Trim();
                    else if (kv[0].Equals("category", StringComparison.OrdinalIgnoreCase))
                        categoryFilter = Uri.UnescapeDataString(kv[1]).Trim();
                }
            }
        }

        string scriptsDir = ClientFileSources.SkuaScriptsDIR;
        var existingLocalFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(scriptsDir))
        {
            try
            {
                foreach (string filePath in Directory.EnumerateFiles(scriptsDir, "*.cs", SearchOption.AllDirectories))
                {
                    existingLocalFiles.Add(Path.GetFullPath(filePath));
                }
            }
            catch { }
        }

        // Try getting scripts from IGetScriptsService or scripts_cache.json
        List<ScriptInfo>? repoScripts = null;
        var getScripts = _serviceProvider.GetService<IGetScriptsService>();
        if (getScripts?.Scripts != null && getScripts.Scripts.Count > 0)
        {
            repoScripts = getScripts.Scripts.ToList();
        }
        else if (File.Exists(ClientFileSources.SkuaScriptsCacheFile))
        {
            try
            {
                string json = File.ReadAllText(ClientFileSources.SkuaScriptsCacheFile);
                repoScripts = JsonConvert.DeserializeObject<List<ScriptInfo>>(json);
            }
            catch { }
        }

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (repoScripts != null)
        {
            foreach (var s in repoScripts)
            {
                string fileName = s.FileName ?? Path.GetFileName(s.FilePath) ?? string.Empty;
                string filePath = s.FilePath ?? string.Empty;
                string name = s.Name ?? string.Empty;

                // Filter out Core library / helper files
                if (IsCoreScript(fileName, filePath, name))
                    continue;

                string relPath = filePath.Replace('\\', '/');
                if (seenPaths.Contains(relPath))
                    continue;
                seenPaths.Add(relPath);

                string category = DetermineScriptCategory(relPath);

                if (!string.IsNullOrWhiteSpace(categoryFilter) && !categoryFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    if (!category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                string desc = !string.IsNullOrWhiteSpace(s.Description) && !s.Description.Equals("null", StringComparison.OrdinalIgnoreCase)
                    ? s.Description
                    : $"Automated {category} script.";

                string[] tagsArr = s.Tags != null && s.Tags.Length > 0 && !(s.Tags.Length == 1 && s.Tags[0] == "null")
                    ? s.Tags
                    : new[] { category };

                string tagsStr = string.Join(", ", tagsArr);

                string fullLocalPath = Path.GetFullPath(Path.Combine(scriptsDir, relPath.Replace('/', Path.DirectorySeparatorChar)));
                bool isDownloaded = existingLocalFiles.Contains(fullLocalPath);
                long lastModified = 0;
                if (isDownloaded && File.Exists(fullLocalPath))
                {
                    try { lastModified = new DateTimeOffset(new FileInfo(fullLocalPath).LastWriteTimeUtc).ToUnixTimeMilliseconds(); } catch { }
                }
                if (lastModified == 0 && s.CreationDate.HasValue && s.CreationDate.Value != default)
                {
                    try { lastModified = new DateTimeOffset(s.CreationDate.Value.ToUniversalTime()).ToUnixTimeMilliseconds(); } catch { }
                }
                string dateStr = lastModified > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(lastModified).LocalDateTime.ToString("yyyy-MM-dd") : string.Empty;

                if (!string.IsNullOrWhiteSpace(filter))
                {
                    if (!name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                        !fileName.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                        !category.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                        !desc.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                        !tagsStr.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                scripts.Add(new
                {
                    name = name,
                    fileName = fileName,
                    path = relPath,
                    category = category,
                    description = desc,
                    tags = tagsStr,
                    downloaded = isDownloaded,
                    size = s.Size,
                    last_modified = lastModified,
                    date = dateStr
                });
            }
        }

        // Also enumerate any local files in Scripts folder not already in repo cache
        if (Directory.Exists(scriptsDir))
        {
            try
            {
                foreach (string filePath in Directory.EnumerateFiles(scriptsDir, "*.cs", SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileName(filePath);
                    string relPath = Path.GetRelativePath(scriptsDir, filePath).Replace(Path.DirectorySeparatorChar, '/');

                    if (IsCoreScript(fileName, relPath, fileName))
                        continue;

                    if (seenPaths.Contains(relPath))
                        continue;
                    seenPaths.Add(relPath);

                    string category = DetermineScriptCategory(relPath);

                    if (!string.IsNullOrWhiteSpace(categoryFilter) && !categoryFilter.Equals("All", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!category.Equals(categoryFilter, StringComparison.OrdinalIgnoreCase) &&
                            !categoryFilter.Equals("Local", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    string name = Path.GetFileNameWithoutExtension(fileName);
                    string desc = $"Local script in {category}.";

                    if (!string.IsNullOrWhiteSpace(filter))
                    {
                        if (!name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                            !fileName.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                            !category.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    int size = 0;
                    long lastModified = 0;
                    try
                    {
                        var fi = new FileInfo(filePath);
                        size = (int)fi.Length;
                        lastModified = new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                    }
                    catch { }

                    string dateStr = lastModified > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(lastModified).LocalDateTime.ToString("yyyy-MM-dd") : string.Empty;

                    scripts.Add(new
                    {
                        name = name,
                        fileName = fileName,
                        path = relPath,
                        category = category,
                        description = desc,
                        tags = "Local",
                        downloaded = true,
                        size = size,
                        last_modified = lastModified,
                        date = dateStr
                    });
                }
            }
            catch { }
        }

        return new { count = scripts.Count, scripts };
    }

    private static bool IsCoreScript(string fileName, string filePath, string name)
    {
        if (string.IsNullOrWhiteSpace(fileName) && string.IsNullOrWhiteSpace(filePath))
            return true;
        if (fileName.StartsWith("Core", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("Core", StringComparison.OrdinalIgnoreCase))
            return true;
        if (filePath.StartsWith("Core", StringComparison.OrdinalIgnoreCase))
            return true;
        if (filePath.Contains("/Core/", StringComparison.OrdinalIgnoreCase) || filePath.Contains("\\Core\\", StringComparison.OrdinalIgnoreCase))
            return true;
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private async Task<RemoteActionResult> HandleStartScriptAsync(string body)
    {
        var manager = _serviceProvider.GetService<IScriptManager>();
        if (manager == null)
            return RemoteActionResult.Fail("ScriptManager unavailable");

        string scriptPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("script", out var prop))
                    scriptPath = prop.GetString() ?? string.Empty;
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(scriptPath))
        {
            string fullPath = Path.IsPathRooted(scriptPath)
                ? scriptPath
                : Path.Combine(ClientFileSources.SkuaScriptsDIR, scriptPath.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(fullPath))
            {
                string legacyPath = Path.Combine(ClientFileSources.LegacySkuaDIR, "Scripts", scriptPath.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(legacyPath))
                {
                    fullPath = legacyPath;
                }
            }

            if (!File.Exists(fullPath))
            {
                // Attempt to download script automatically if not cached locally
                bool downloaded = false;
                try
                {
                    var getScripts = _serviceProvider.GetService<IGetScriptsService>();
                    if (getScripts != null)
                    {
                        var info = getScripts.Scripts?.FirstOrDefault(s =>
                            string.Equals(s.FilePath?.Replace('\\', '/'), scriptPath.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(s.FileName, Path.GetFileName(scriptPath), StringComparison.OrdinalIgnoreCase));
                        if (info != null)
                        {
                            await getScripts.DownloadScriptAsync(info).ConfigureAwait(false);
                            downloaded = File.Exists(fullPath);
                        }
                    }
                }
                catch { }

                if (!downloaded && !File.Exists(fullPath))
                {
                    try
                    {
                        string rawUrl = $"https://raw.githubusercontent.com/auqw/Scripts/refs/heads/Skua/{scriptPath.Replace('\\', '/')}";
                        using var http = new HttpClient();
                        http.Timeout = TimeSpan.FromSeconds(15);
                        byte[] content = await http.GetByteArrayAsync(rawUrl).ConfigureAwait(false);
                        string? dir = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        await File.WriteAllBytesAsync(fullPath, content).ConfigureAwait(false);
                    }
                    catch { }
                }

                if (!File.Exists(fullPath))
                    return RemoteActionResult.Fail($"Script file not found: {scriptPath}");
            }

            if (manager.ScriptRunning)
            {
                await manager.StopScript().ConfigureAwait(false);
                await Task.Delay(250).ConfigureAwait(false);
            }

            manager.SetLoadedScript(fullPath);
        }

        if (string.IsNullOrWhiteSpace(manager.LoadedScript))
            return RemoteActionResult.Fail("No script currently loaded");

        if (manager.ScriptRunning)
        {
            if (manager.ScriptPaused)
            {
                manager.ScriptPaused = false;
                return RemoteActionResult.Ok("Script resumed");
            }
            return RemoteActionResult.Ok("Script already running");
        }

        // Ensure character login session is settled before spawning script thread
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot != null && !bot.Player.LoggedIn)
        {
            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(200).ConfigureAwait(false);
                if (bot.Player.LoggedIn)
                    break;
            }
        }

        bool prevSilent = manager.SilentConfig;
        manager.SilentConfig = true;
        try
        {
            Exception? startEx = await manager.StartScript().ConfigureAwait(false);
            if (startEx != null)
            {
                return RemoteActionResult.Fail($"Script failed to start: {startEx.Message}");
            }
        }
        finally
        {
            manager.SilentConfig = prevSilent;
        }

        return RemoteActionResult.Ok($"Started script: {Path.GetFileName(manager.LoadedScript)}");
    }

    private async Task<RemoteActionResult> HandleStopScriptAsync()
    {
        var manager = _serviceProvider.GetService<IScriptManager>();
        if (manager == null)
            return RemoteActionResult.Fail("ScriptManager unavailable");

        if (!manager.ScriptRunning)
            return RemoteActionResult.Ok("Script not running");

        await manager.StopScript();
        return RemoteActionResult.Ok("Script stopped");
    }

    private RemoteActionResult HandleTogglePause()
    {
        var manager = _serviceProvider.GetService<IScriptManager>();
        if (manager == null)
            return RemoteActionResult.Fail("ScriptManager unavailable");

        if (!manager.ScriptRunning)
            return RemoteActionResult.Fail("Script is not running");

        manager.ScriptPaused = !manager.ScriptPaused;
        return RemoteActionResult.Ok(manager.ScriptPaused ? "Script paused" : "Script resumed", new { paused = manager.ScriptPaused });
    }

    private RemoteActionResult HandleLoadScript(string body)
    {
        var manager = _serviceProvider.GetService<IScriptManager>();
        if (manager == null)
            return RemoteActionResult.Fail("ScriptManager unavailable");

        string scriptPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("script", out var prop))
                    scriptPath = prop.GetString() ?? string.Empty;
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(scriptPath))
            return RemoteActionResult.Fail("Script path missing");

        string fullPath = Path.IsPathRooted(scriptPath)
            ? scriptPath
            : Path.Combine(ClientFileSources.SkuaScriptsDIR, scriptPath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(fullPath))
            return RemoteActionResult.Fail($"Script file not found: {scriptPath}");

        manager.SetLoadedScript(fullPath);
        return RemoteActionResult.Ok($"Loaded script: {Path.GetFileName(fullPath)}");
    }

    private RemoteActionResult HandleLaunchAccount(string body)
    {
        string username = string.Empty;
        string group = string.Empty;
        string server = string.Empty;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("username", out var u)) username = u.GetString() ?? string.Empty;
                else if (doc.RootElement.TryGetProperty("account", out var a)) username = a.GetString() ?? string.Empty;
                else if (doc.RootElement.TryGetProperty("user", out var usr)) username = usr.GetString() ?? string.Empty;

                if (doc.RootElement.TryGetProperty("group", out var g)) group = g.GetString() ?? string.Empty;
                if (doc.RootElement.TryGetProperty("server", out var s)) server = s.GetString() ?? string.Empty;
            }
            catch { }
        }

        if (LaunchGroupAction != null && !string.IsNullOrWhiteSpace(group))
        {
            LaunchGroupAction.Invoke(group);
            return RemoteActionResult.Ok($"Launching account group '{group}'");
        }

        if (LaunchAccountAction != null && !string.IsNullOrWhiteSpace(username))
        {
            LaunchAccountAction.Invoke(username, null, string.IsNullOrEmpty(server) ? null : server);
            return RemoteActionResult.Ok($"Launching account '{username}'");
        }

        if (!string.IsNullOrWhiteSpace(group))
        {
            var groups = _settingsService.Get<List<GroupData>>("AccountGroups");
            var grp = groups?.FirstOrDefault(g => g.Name.Equals(group, StringComparison.OrdinalIgnoreCase));
            if (grp?.Accounts != null && grp.Accounts.Count > 0)
            {
                Task.Run(async () =>
                {
                    foreach (var member in grp.Accounts)
                    {
                        SpawnAccountProcess(member, server);
                        await Task.Delay(1200).ConfigureAwait(false);
                    }
                });
                return RemoteActionResult.Ok($"Launching account group '{group}'");
            }
            return RemoteActionResult.Fail($"Group '{group}' not found");
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            SpawnAccountProcess(username, server);
            return RemoteActionResult.Ok($"Launching account '{username}'");
        }

        return RemoteActionResult.Fail("Must specify 'username' or 'group' to launch");
    }

    private void SpawnAccountProcess(string username, string? server)
    {
        try
        {
            var accs = _settingsService.Get<Dictionary<string, AccountData>>("ManagedAccounts");
            string pass = string.Empty;
            if (accs != null && accs.TryGetValue(username, out var acc))
            {
                pass = acc.Password ?? string.Empty;
                if (pass.StartsWith("enc:"))
                {
                    try { pass = Skua.Core.Utils.CryptoUtils.Decrypt(pass); } catch { }
                }
            }
            string srv = !string.IsNullOrEmpty(server) ? server : (_settingsService.Get("LastServer", "Twilly") ?? "Twilly");

            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            ProcessStartInfo psi = new ProcessStartInfo();
            if (exePath.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase) || exePath.EndsWith("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                psi.FileName = exePath;
                psi.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "VibeSkua.dll"));
            }
            else
            {
                psi.FileName = exePath;
            }
            psi.ArgumentList.Add("-u");
            psi.ArgumentList.Add(username);
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add(pass);
            psi.ArgumentList.Add("-s");
            psi.ArgumentList.Add(srv);
            psi.WorkingDirectory = AppContext.BaseDirectory;
            Process.Start(psi);
        }
        catch { }
    }

    private RemoteActionResult HandleJump(string body)
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Map == null)
            return RemoteActionResult.Fail("Map interface unavailable");

        string cell = "Enter";
        string pad = "Spawn";
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("cell", out var cellProp))
                    cell = cellProp.GetString() ?? "Enter";
                if (doc.RootElement.TryGetProperty("pad", out var padProp))
                    pad = padProp.GetString() ?? "Spawn";
            }
            catch { }
        }

        bot.Map.Jump(cell, pad);
        return RemoteActionResult.Ok($"Jumped to {cell}, {pad}", new { cell, pad });
    }

    private RemoteActionResult HandleGoto(string body)
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Player == null)
            return RemoteActionResult.Fail("Player interface unavailable");

        string target = string.Empty;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("target", out var prop))
                    target = prop.GetString() ?? string.Empty;
                else if (doc.RootElement.TryGetProperty("username", out prop))
                    target = prop.GetString() ?? string.Empty;
                else if (doc.RootElement.TryGetProperty("player", out prop))
                    target = prop.GetString() ?? string.Empty;
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(target))
            return RemoteActionResult.Fail("Target player name required");

        bot.Player.Goto(target);
        return RemoteActionResult.Ok($"Goto {target}", new { target });
    }

    private RemoteActionResult HandleJoinMap(string body)
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Map == null)
            return RemoteActionResult.Fail("Map interface unavailable");

        string map = string.Empty;
        string cell = "Enter";
        string pad = "Spawn";
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("map", out var mapProp))
                    map = mapProp.GetString() ?? string.Empty;
                else if (doc.RootElement.TryGetProperty("name", out mapProp))
                    map = mapProp.GetString() ?? string.Empty;

                if (doc.RootElement.TryGetProperty("cell", out var cellProp))
                    cell = cellProp.GetString() ?? "Enter";
                if (doc.RootElement.TryGetProperty("pad", out var padProp))
                    pad = padProp.GetString() ?? "Spawn";
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(map))
            return RemoteActionResult.Fail("Map name required");

        bot.Map.Join(map, cell, pad);
        return RemoteActionResult.Ok($"Joining map {map}", new { map, cell, pad });
    }

    private RemoteActionResult HandleRest()
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Player == null)
            return RemoteActionResult.Fail("Player interface unavailable");

        bot.Player.Rest(false);
        return RemoteActionResult.Ok("Rest triggered");
    }

    private RemoteActionResult HandleRelog(string body)
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Servers == null)
            return RemoteActionResult.Fail("Servers interface unavailable");

        string server = string.Empty;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("server", out var prop))
                    server = prop.GetString() ?? string.Empty;
            }
            catch { }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (bot.Player.Playing)
                {
                    if (!string.IsNullOrWhiteSpace(server))
                        bot.Servers.Relogin(server);
                    else
                        bot.Servers.Relogin();
                }
                else
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    if (!string.IsNullOrWhiteSpace(server))
                        await bot.Servers.EnsureRelogin(server, cts.Token);
                    else
                        await bot.Servers.EnsureRelogin(cts.Token);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"[RemoteControlService] Relogin error: {ex.Message}");
            }
        });

        return RemoteActionResult.Ok("Relogin initiated", new { server = !string.IsNullOrWhiteSpace(server) ? server : "Auto" });
    }

    private RemoteActionResult HandleAttack(string body)
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Combat == null)
            return RemoteActionResult.Fail("Combat interface unavailable");

        string monster = string.Empty;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("monster", out var prop))
                    monster = prop.GetString() ?? string.Empty;
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(monster))
        {
            bot.Combat.Attack(monster);
        }
        else if (bot.Player.HasTarget && bot.Player.Target != null)
        {
            bot.Combat.Attack(bot.Player.Target);
        }
        else
        {
            bot.Combat.Attack("*");
        }

        try
        {
            bot.Flash?.CallGameFunction("world.startAutoAttack");
            bot.Skills.UseSkill(0);
        }
        catch { }

        return RemoteActionResult.Ok("Attacking target");
    }

    private RemoteActionResult HandleUseSkill(string body, int? pathSkill = null)
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Skills == null)
            return RemoteActionResult.Fail("Skills interface unavailable");

        int skill = pathSkill ?? 1;
        if (!pathSkill.HasValue && !string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("skill", out var prop))
                    skill = prop.GetInt32();
            }
            catch { }
        }

        if (skill < 1 || skill > 5)
            return RemoteActionResult.Fail("Skill index must be between 1 and 5");

        // If player has no target, acquire cell monster target first so offensive skills connect
        if (bot.Player != null && !bot.Player.HasTarget)
        {
            try
            {
                bot.Combat?.Attack("*");
            }
            catch { }
        }

        // In AQW Flash world.actions.active:
        // [0]=Auto Attack, [1]=Skill 1, [2]=Skill 2, [3]=Skill 3, [4]=Skill 4, [5]=Skill 5/Potion
        bot.Skills.UseSkill(skill);

        try
        {
            bot.Flash?.CallGameFunction("world.testAction", bot.Flash.GetGameObject<object>($"world.actions.active[{skill}]"));
        }
        catch { }

        return RemoteActionResult.Ok($"Used skill {skill}", new { skill });
    }

    private RemoteActionResult HandleCancelTarget()
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Combat == null)
            return RemoteActionResult.Fail("Combat interface unavailable");

        bot.Combat.CancelAutoAttack();
        bot.Combat.CancelTarget();
        try
        {
            bot.Flash?.CallGameFunction("world.cancelTarget");
            bot.Flash?.CallGameFunction("world.cancelAutoAttack");
            bot.Flash?.SetGameObject("world.myAvatar.target", null);
        }
        catch { }
        return RemoteActionResult.Ok("Target cleared");
    }

    private object GetCurrentDrops()
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        var drops = new List<string>();
        if (bot?.Drops?.CurrentDrops != null)
        {
            foreach (var d in bot.Drops.CurrentDrops)
                if (!string.IsNullOrWhiteSpace(d) && !drops.Contains(d)) drops.Add(d);
        }
        if (bot?.Drops?.CurrentDropInfos != null)
        {
            foreach (var di in bot.Drops.CurrentDropInfos)
                if (di != null && !string.IsNullOrWhiteSpace(di.Name) && !drops.Contains(di.Name)) drops.Add(di.Name);
        }
        if (bot?.Flash != null && bot.Flash.IsWorldLoaded)
        {
            try
            {
                int dropCount = bot.Flash.GetGameObject<int>("world.ui.mcDropMenu.numChildren");
                for (int i = 0; i < dropCount; i++)
                {
                    string dropName = bot.Flash.GetGameObject<string>($"world.ui.mcDropMenu.getChildAt({i}).cnt.strName");
                    if (!string.IsNullOrWhiteSpace(dropName) && dropName != "null" && !drops.Contains(dropName))
                    {
                        drops.Add(dropName);
                    }
                }
            }
            catch { }
        }
        if (bot?.Drops?.ToPickup != null)
        {
            try
            {
                foreach (var tp in bot.Drops.ToPickup)
                {
                    if (!string.IsNullOrWhiteSpace(tp) && !drops.Contains(tp))
                        drops.Add(tp);
                }
            }
            catch { }
        }
        return new { drops };
    }

    private static List<object> GetActiveQuestsList(IScriptInterface? bot)
    {
        var activeQuests = new List<object>();
        if (bot?.Quests == null)
            return activeQuests;

        var tempItems = bot.TempInv?.Items?.ToList() ?? new List<ItemBase>();
        var invItems = bot.Inventory?.Items?.ToList() ?? new List<InventoryItem>();

        object FormatQuest(Quest q, string? customName = null)
        {
            bool canComplete = false;
            try { canComplete = bot.Quests.CanComplete(q.ID); } catch { }

            var reqsList = new List<object>();
            if (q.Requirements != null)
            {
                foreach (var req in q.Requirements)
                {
                    if (req == null) continue;
                    int currentQty = 0;
                    bool isTemp = false;

                    var tempMatch = tempItems.FirstOrDefault(t => t.ID == req.ID || (!string.IsNullOrEmpty(t.Name) && string.Equals(t.Name, req.Name, StringComparison.OrdinalIgnoreCase)));
                    if (tempMatch != null)
                    {
                        currentQty = tempMatch.Quantity;
                        isTemp = true;
                    }
                    else
                    {
                        var invMatch = invItems.FirstOrDefault(i => i.ID == req.ID || (!string.IsNullOrEmpty(i.Name) && string.Equals(i.Name, req.Name, StringComparison.OrdinalIgnoreCase)));
                        if (invMatch != null)
                        {
                            currentQty = invMatch.Quantity;
                            isTemp = false;
                        }
                    }

                    reqsList.Add(new
                    {
                        id = req.ID,
                        name = req.Name ?? $"Item #{req.ID}",
                        required = req.Quantity,
                        current = currentQty,
                        is_temp = isTemp
                    });
                }
            }

            return new
            {
                id = q.ID,
                name = !string.IsNullOrWhiteSpace(customName) ? customName : (!string.IsNullOrWhiteSpace(q.Name) ? q.Name : $"Quest #{q.ID}"),
                can_complete = canComplete,
                requirements = reqsList
            };
        }

        var addedIds = new HashSet<int>();

        if (bot.Quests.Active != null)
        {
            try
            {
                foreach (var q in bot.Quests.Active.ToList())
                {
                    if (q != null && addedIds.Add(q.ID))
                    {
                        activeQuests.Add(FormatQuest(q));
                    }
                }
            }
            catch { }
        }

        if (activeQuests.Count == 0 && bot.Quests.Tree != null)
        {
            try
            {
                foreach (var q in bot.Quests.Tree.Where(x => x != null && x.Active).ToList())
                {
                    if (q != null && addedIds.Add(q.ID))
                    {
                        activeQuests.Add(FormatQuest(q));
                    }
                }
            }
            catch { }
        }

        if (bot.Quests.Registered != null)
        {
            try
            {
                foreach (int qId in bot.Quests.Registered.ToList())
                {
                    if (addedIds.Contains(qId)) continue;
                    var qObj = bot.Quests.Tree?.FirstOrDefault(x => x != null && x.ID == qId);
                    if (qObj != null)
                    {
                        addedIds.Add(qId);
                        activeQuests.Add(FormatQuest(qObj, $"{qObj.Name} (Auto)"));
                    }
                    else
                    {
                        addedIds.Add(qId);
                        bool canComplete = false;
                        try { canComplete = bot.Quests.CanComplete(qId); } catch { }
                        activeQuests.Add(new
                        {
                            id = qId,
                            name = $"Quest #{qId} (Auto)",
                            can_complete = canComplete,
                            requirements = Array.Empty<object>()
                        });
                    }
                }
            }
            catch { }
        }

        return activeQuests;
    }

    private object GetCurrentQuests()
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        return new { quests = GetActiveQuestsList(bot) };
    }

    private RemoteActionResult HandleCompleteQuest(string body)
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Quests == null)
            return RemoteActionResult.Fail("Quests interface unavailable");

        int questId = 0;
        int itemId = -1;
        bool special = false;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("id", out var idProp))
                    questId = idProp.GetInt32();
                if (doc.RootElement.TryGetProperty("item_id", out var itemProp))
                    itemId = itemProp.GetInt32();
                if (doc.RootElement.TryGetProperty("special", out var specProp))
                    special = specProp.GetBoolean();
            }
            catch (Exception ex)
            {
                return RemoteActionResult.Fail($"Failed to parse quest complete payload: {ex.Message}");
            }
        }

        if (questId <= 0)
            return RemoteActionResult.Fail("Invalid quest ID");

        bool success = false;
        try
        {
            success = bot.Quests.Complete(questId, itemId, special);
        }
        catch (Exception ex)
        {
            return RemoteActionResult.Fail($"Error completing quest {questId}: {ex.Message}");
        }

        return success
            ? RemoteActionResult.Ok($"Turned in quest {questId}")
            : RemoteActionResult.Fail($"Could not complete quest {questId} (requirements may not be met)");
    }

    private RemoteActionResult HandlePickupDrop(string body)
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Drops == null)
            return RemoteActionResult.Fail("Drops interface unavailable");

        string item = string.Empty;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("item", out var prop))
                    item = prop.GetString() ?? string.Empty;
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(item))
            return RemoteActionResult.Fail("Item name required");

        bot.Drops.Pickup(item);
        return RemoteActionResult.Ok($"Picking up {item}", new { item });
    }

    private RemoteActionResult HandlePickupAllDrops()
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Drops == null)
            return RemoteActionResult.Fail("Drops interface unavailable");

        bot.Drops.PickupAll();
        return RemoteActionResult.Ok("PickupAll triggered");
    }

    private static List<object> GetTempItemsList(IScriptInterface? bot)
    {
        var list = new List<object>();
        if (bot?.TempInv?.Items != null)
        {
            try
            {
                var raw = bot.TempInv.Items.ToList();
                foreach (var it in raw)
                {
                    if (it == null) continue;
                    string name = !string.IsNullOrWhiteSpace(it.Name) ? it.Name : $"Temp #{it.ID}";
                    list.Add(new
                    {
                        id = it.ID,
                        name = name,
                        quantity = it.Quantity > 0 ? it.Quantity : 1,
                        max_quantity = it.MaxStack > 0 ? it.MaxStack : 1
                    });
                }
            }
            catch { }
        }
        return list;
    }

    private object GetTempItems()
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        var items = GetTempItemsList(bot);
        return new { count = items.Count, items };
    }

    private object GetInventoryItems()
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        var items = new List<object>();
        try
        {
            var raw = bot?.Inventory?.Items;
            if (raw != null)
            {
                var list = raw.ToList();
                foreach (var it in list)
                {
                    if (it == null) continue;
                    try
                    {
                        string name = !string.IsNullOrWhiteSpace(it.Name) ? it.Name : $"Item #{it.ID}";
                        string cat = it.Category != ItemCategory.Unknown
                            ? it.Category.ToString()
                            : (!string.IsNullOrWhiteSpace(it.CategoryString) ? it.CategoryString : "Item");

                        items.Add(new
                        {
                            id = it.ID,
                            char_id = it.CharItemID,
                            name = name,
                            quantity = it.Quantity > 0 ? it.Quantity : 1,
                            max_quantity = it.MaxStack > 0 ? it.MaxStack : 1,
                            ac = it.Coins,
                            equipped = it.Equipped,
                            category = cat,
                            upgrade = it.Upgrade
                        });
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RemoteControlService] GetInventoryItems error: {ex.Message}");
        }

        int used = bot?.Inventory?.UsedSlots ?? items.Count;
        int max = bot?.Inventory?.Slots ?? 0;
        return new { count = items.Count, used, used_slots = used, max, max_slots = max, items };
    }

    private object GetBankItems()
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        var items = new List<object>();
        if (bot?.Bank == null)
            return new { count = 0, used = 0, used_slots = 0, max = 0, max_slots = 0, loaded = false, is_loaded = false, items };

        if (!bot.Bank.Loaded && bot.Player != null && bot.Player.Playing)
        {
            try
            {
                bot.Bank.Load(true);
            }
            catch { }
        }

        try
        {
            var raw = bot.Bank.Items;
            if (raw != null)
            {
                var list = raw.ToList();
                foreach (var it in list)
                {
                    if (it == null) continue;
                    try
                    {
                        string name = !string.IsNullOrWhiteSpace(it.Name) ? it.Name : $"Item #{it.ID}";
                        string cat = it.Category != ItemCategory.Unknown
                            ? it.Category.ToString()
                            : (!string.IsNullOrWhiteSpace(it.CategoryString) ? it.CategoryString : "Item");

                        items.Add(new
                        {
                            id = it.ID,
                            char_id = it.CharItemID,
                            name = name,
                            quantity = it.Quantity > 0 ? it.Quantity : 1,
                            max_quantity = it.MaxStack > 0 ? it.MaxStack : 1,
                            ac = it.Coins,
                            category = cat,
                            upgrade = it.Upgrade
                        });
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[RemoteControlService] GetBankItems error: {ex.Message}");
        }

        int bUsed = bot.Bank.UsedSlots > 0 ? bot.Bank.UsedSlots : items.Count;
        int bMax = bot.Bank.Slots;
        bool bLoaded = bot.Bank.Loaded;
        return new { count = items.Count, used = bUsed, used_slots = bUsed, max = bMax, max_slots = bMax, loaded = bLoaded, is_loaded = bLoaded, items };
    }

    private RemoteActionResult HandleOpenBank()
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Bank == null)
            return RemoteActionResult.Fail("Bank interface unavailable");

        try
        {
            bot.Bank.Open();
            return RemoteActionResult.Ok("Bank opened in-game");
        }
        catch (Exception ex)
        {
            return RemoteActionResult.Fail($"Failed to open bank: {ex.Message}");
        }
    }

    private RemoteActionResult HandleLoadBank()
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Bank == null)
            return RemoteActionResult.Fail("Bank interface unavailable");

        try
        {
            bot.Bank.Load(true);
            return RemoteActionResult.Ok("Bank reload triggered", new { count = bot.Bank.Items?.Count ?? 0 });
        }
        catch (Exception ex)
        {
            return RemoteActionResult.Fail($"Failed to load bank: {ex.Message}");
        }
    }

    private RemoteActionResult HandleEquipItem(string body)
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Inventory == null)
            return RemoteActionResult.Fail("Inventory interface unavailable");

        string itemName = string.Empty;
        int itemId = 0;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("item", out var pName))
                    itemName = pName.GetString() ?? string.Empty;
                if (doc.RootElement.TryGetProperty("id", out var pId))
                    itemId = pId.GetInt32();
            }
            catch { }
        }

        try
        {
            if (itemId > 0)
            {
                bot.Inventory.EquipItem(itemId);
                return RemoteActionResult.Ok($"Equipped item #{itemId}");
            }
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                if (bot.Inventory.TryGetItem(itemName, out var invItem) && invItem != null)
                {
                    bot.Inventory.EquipItem(invItem.ID);
                    return RemoteActionResult.Ok($"Equipped {itemName}");
                }
                return RemoteActionResult.Fail($"Item not found in inventory: {itemName}");
            }
        }
        catch (Exception ex)
        {
            return RemoteActionResult.Fail($"Equip error: {ex.Message}");
        }

        return RemoteActionResult.Fail("Item name or ID required");
    }

    private RemoteActionResult HandleToBank(string body)
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Inventory == null)
            return RemoteActionResult.Fail("Inventory interface unavailable");

        string item = string.Empty;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("item", out var prop))
                    item = prop.GetString() ?? string.Empty;
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(item))
            return RemoteActionResult.Fail("Item name required");

        bool res = bot.Inventory.ToBank(item);
        return new RemoteActionResult { success = res, message = res ? $"Moved {item} to bank" : $"Failed to move {item} to bank", data = new { item } };
    }

    private RemoteActionResult HandleToInventory(string body)
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Bank == null)
            return RemoteActionResult.Fail("Bank interface unavailable");

        string item = string.Empty;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("item", out var prop))
                    item = prop.GetString() ?? string.Empty;
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(item))
            return RemoteActionResult.Fail("Item name required");

        bool res = bot.Bank.ToInventory(item);
        return new RemoteActionResult { success = res, message = res ? $"Moved {item} to inventory" : $"Failed to move {item} to inventory", data = new { item } };
    }

    private object GetOptionsState()
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        return new
        {
            lag_killer = bot?.Options?.LagKiller ?? false,
            auto_relogin = bot?.Options?.AutoRelogin ?? false,
            infinite_range = bot?.Options?.InfiniteRange ?? false,
            skip_cutscenes = bot?.Options?.SkipCutscenes ?? false,
            headless_mode = bot?.Options?.HeadlessMode ?? false,
            streamer_mode = bot?.Options?.StreamerMode ?? false,
            disable_fx = bot?.Options?.DisableFX ?? false,
            attack_without_target = bot?.Options?.AttackWithoutTarget ?? false,
            disable_collisions = bot?.Options?.DisableCollisions ?? false,
            hide_players = bot?.Options?.HidePlayers ?? false,
            magnetise = bot?.Options?.Magnetise ?? false,
            rest_packets = bot?.Options?.RestPackets ?? false,
            walk_speed = bot?.Options?.WalkSpeed ?? 8,
            fps = bot?.Options?.SetFPS ?? 60
        };
    }

    private RemoteActionResult HandleToggleOption(string body)
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Options == null)
            return RemoteActionResult.Fail("Options interface unavailable");

        string option = string.Empty;
        bool? boolVal = null;
        int? intVal = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("option", out var optProp))
                    option = optProp.GetString() ?? string.Empty;
                if (doc.RootElement.TryGetProperty("value", out var valProp))
                {
                    if (valProp.ValueKind == JsonValueKind.True || valProp.ValueKind == JsonValueKind.False)
                        boolVal = valProp.GetBoolean();
                    else if (valProp.ValueKind == JsonValueKind.Number && valProp.TryGetInt32(out int n))
                        intVal = n;
                }
            }
            catch { }
        }

        string cleanOption = option.Replace("_", "").Replace("-", "").ToLowerInvariant();
        switch (cleanOption)
        {
            case "lagkiller":
                bot.Options.LagKiller = boolVal ?? !bot.Options.LagKiller;
                return RemoteActionResult.Ok("LagKiller toggled", new { option = "LagKiller", value = bot.Options.LagKiller });
            case "autorelogin":
                bot.Options.AutoRelogin = boolVal ?? !bot.Options.AutoRelogin;
                return RemoteActionResult.Ok("AutoRelogin toggled", new { option = "AutoRelogin", value = bot.Options.AutoRelogin });
            case "infiniterange":
                bot.Options.InfiniteRange = boolVal ?? !bot.Options.InfiniteRange;
                return RemoteActionResult.Ok("InfiniteRange toggled", new { option = "InfiniteRange", value = bot.Options.InfiniteRange });
            case "skipcutscenes":
                bot.Options.SkipCutscenes = boolVal ?? !bot.Options.SkipCutscenes;
                return RemoteActionResult.Ok("SkipCutscenes toggled", new { option = "SkipCutscenes", value = bot.Options.SkipCutscenes });
            case "headlessmode":
            case "headless":
                bot.Options.HeadlessMode = boolVal ?? !bot.Options.HeadlessMode;
                return RemoteActionResult.Ok("HeadlessMode toggled", new { option = "HeadlessMode", value = bot.Options.HeadlessMode });
            case "streamermode":
            case "streamer":
                bot.Options.StreamerMode = boolVal ?? !bot.Options.StreamerMode;
                return RemoteActionResult.Ok("StreamerMode toggled", new { option = "StreamerMode", value = bot.Options.StreamerMode });
            case "disablefx":
            case "fx":
                bot.Options.DisableFX = boolVal ?? !bot.Options.DisableFX;
                return RemoteActionResult.Ok("DisableFX toggled", new { option = "DisableFX", value = bot.Options.DisableFX });
            case "attackwithouttarget":
            case "attackwithout":
                bot.Options.AttackWithoutTarget = boolVal ?? !bot.Options.AttackWithoutTarget;
                return RemoteActionResult.Ok("AttackWithoutTarget toggled", new { option = "AttackWithoutTarget", value = bot.Options.AttackWithoutTarget });
            case "disablecollisions":
            case "collisions":
                bot.Options.DisableCollisions = boolVal ?? !bot.Options.DisableCollisions;
                return RemoteActionResult.Ok("DisableCollisions toggled", new { option = "DisableCollisions", value = bot.Options.DisableCollisions });
            case "hideplayers":
            case "hide":
                bot.Options.HidePlayers = boolVal ?? !bot.Options.HidePlayers;
                return RemoteActionResult.Ok("HidePlayers toggled", new { option = "HidePlayers", value = bot.Options.HidePlayers });
            case "magnetise":
            case "magnet":
                bot.Options.Magnetise = boolVal ?? !bot.Options.Magnetise;
                return RemoteActionResult.Ok("Magnetise toggled", new { option = "Magnetise", value = bot.Options.Magnetise });
            case "restpackets":
            case "rest":
                bot.Options.RestPackets = boolVal ?? !bot.Options.RestPackets;
                return RemoteActionResult.Ok("RestPackets toggled", new { option = "RestPackets", value = bot.Options.RestPackets });
            case "walkspeed":
                if (intVal.HasValue) bot.Options.WalkSpeed = intVal.Value;
                return RemoteActionResult.Ok("WalkSpeed set", new { option = "WalkSpeed", value = bot.Options.WalkSpeed });
            case "fps":
                if (intVal.HasValue) bot.Options.SetFPS = intVal.Value;
                return RemoteActionResult.Ok("FPS set", new { option = "FPS", value = bot.Options.SetFPS });
            default:
                return RemoteActionResult.Fail($"Unknown option: {option}");
        }
    }

    private RemoteActionResult HandleSendChat(string body)
    {
        EnsureChatHooked();
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Send == null)
            return RemoteActionResult.Fail("Send interface unavailable");

        string message = string.Empty;
        string type = "zone";
        string target = string.Empty;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("message", out var prop))
                    message = prop.GetString() ?? string.Empty;
                if (doc.RootElement.TryGetProperty("type", out var tProp))
                    type = tProp.GetString() ?? "zone";
                if (doc.RootElement.TryGetProperty("target", out var targetProp))
                    target = targetProp.GetString() ?? string.Empty;
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(message))
            return RemoteActionResult.Fail("Message is required");

        // Verify server chat allowance (e.g. Twilly canned chat server)
        string curServer = bot.Servers?.LastName ?? string.Empty;
        bool isCannedChatServer = curServer.Equals("Twilly", StringComparison.OrdinalIgnoreCase);
        if (!isCannedChatServer && bot.Servers?.ServerList != null)
        {
            var srv = bot.Servers.ServerList.Find(s => s.Name.Equals(curServer, StringComparison.OrdinalIgnoreCase));
            if (srv != null && srv.ChatLevel == 0)
                isCannedChatServer = true;
        }

        if (isCannedChatServer)
        {
            return RemoteActionResult.Fail($"Cannot send custom chat on canned-chat server '{curServer}'. Other players cannot see free messages.");
        }

        int roomId = (bot.Map?.RoomID > 0) ? bot.Map.RoomID : 1;
        string myName = !string.IsNullOrWhiteSpace(bot.Player?.Username) && bot.Player.Username != "loginInfo.strUsername"
            ? bot.Player.Username
            : "You";

        // Parse slash commands (/w player message, /whisper player message, /p message, /party message, /g message, /guild message)
        if (message.StartsWith("/w ", StringComparison.OrdinalIgnoreCase) || message.StartsWith("/whisper ", StringComparison.OrdinalIgnoreCase))
        {
            int space1 = message.IndexOf(' ');
            int space2 = message.IndexOf(' ', space1 + 1);
            if (space2 > space1)
            {
                target = message.Substring(space1 + 1, space2 - space1 - 1).Trim();
                message = message.Substring(space2 + 1).Trim();
                type = "whisper";
            }
        }
        else if (message.StartsWith("/p ", StringComparison.OrdinalIgnoreCase) || message.StartsWith("/party ", StringComparison.OrdinalIgnoreCase))
        {
            int space1 = message.IndexOf(' ');
            message = message.Substring(space1 + 1).Trim();
            type = "party";
        }
        else if (message.StartsWith("/g ", StringComparison.OrdinalIgnoreCase) || message.StartsWith("/guild ", StringComparison.OrdinalIgnoreCase))
        {
            int space1 = message.IndexOf(' ');
            message = message.Substring(space1 + 1).Trim();
            type = "guild";
        }

        switch (type.ToLowerInvariant())
        {
            case "whisper":
                if (string.IsNullOrWhiteSpace(target))
                    return RemoteActionResult.Fail("Whisper requires a target username");
                bot.Send.Packet($"%xt%zm%whisper%1%{message}%{target}%");
                try { bot.Send.ClientWhisper(message, $"To {target}"); } catch { }
                AddChatMessage("whisper", $"To {target}", message);
                break;

            case "party":
                bot.Send.Packet($"%xt%zm%message%{roomId}%{message}%party%");
                try { bot.Flash?.Call("sendClientPacket", $"%xt%chatm%0%party~{message}%{myName}%", "str"); } catch { }
                AddChatMessage("party", myName, message);
                break;

            case "guild":
                bot.Send.Packet($"%xt%zm%message%{roomId}%{message}%guild%");
                try { bot.Send.ClientGuild(message, myName); } catch { }
                AddChatMessage("guild", myName, message);
                break;

            default:
                bot.Send.Packet($"%xt%zm%message%{roomId}%{message}%zone%");
                try { bot.Send.ClientZone(message, myName); } catch { }
                AddChatMessage("zone", myName, message);
                break;
        }

        return RemoteActionResult.Ok($"Chat sent: {message}");
    }

    private RemoteActionResult HandleSendPacket(string body)
    {
        var bot = _serviceProvider.GetService<IScriptInterface>();
        if (bot?.Send == null)
            return RemoteActionResult.Fail("Send interface unavailable");

        string packet = string.Empty;
        string type = "String";
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("packet", out var pktProp))
                    packet = pktProp.GetString() ?? string.Empty;
                if (doc.RootElement.TryGetProperty("type", out var typeProp))
                    type = typeProp.GetString() ?? "String";
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(packet))
            return RemoteActionResult.Fail("Packet is required");

        if (type.Equals("Client", StringComparison.OrdinalIgnoreCase))
            bot.Send.ClientPacket(packet, "str");
        else
            bot.Send.Packet(packet, type);

        return RemoteActionResult.Ok("Packet sent");
    }

    private static async Task SendJsonResponseAsync(NetworkStream stream, int statusCode, object data)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(data, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        string statusText = statusCode switch
        {
            200 => "OK",
            204 => "No Content",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            _ => "Internal Error"
        };
        await SendResponseAsync(stream, statusCode, statusText, "application/json; charset=utf-8", bytes).ConfigureAwait(false);
    }

    private static async Task SendResponseAsync(NetworkStream stream, int statusCode, string statusText, string contentType, byte[] bodyBytes)
    {
        var header = new StringBuilder();
        header.Append($"HTTP/1.1 {statusCode} {statusText}\r\n");
        header.Append($"Content-Type: {contentType}\r\n");
        header.Append($"Content-Length: {bodyBytes.Length}\r\n");
        header.Append("Access-Control-Allow-Origin: *\r\n");
        header.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
        header.Append("Access-Control-Allow-Headers: Content-Type, Authorization\r\n");
        header.Append("Connection: close\r\n\r\n");

        byte[] headerBytes = Encoding.UTF8.GetBytes(header.ToString());
        await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
        if (bodyBytes.Length > 0)
        {
            int offset = 0;
            const int chunkSize = 16384;
            while (offset < bodyBytes.Length)
            {
                int toWrite = Math.Min(chunkSize, bodyBytes.Length - offset);
                await stream.WriteAsync(bodyBytes, offset, toWrite).ConfigureAwait(false);
                offset += toWrite;
            }
        }
        await stream.FlushAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }

    private string GetEmbeddedWebDashboardHtml()
    {
        return """
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="utf-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
    <title>VibeSkua Remote Dashboard</title>
    <style>
        :root {
            --bg-dark: #0a0a0a;
            --surface: #121212;
            --surface-alt: #181818;
            --surface-elevated: #1e1e1e;
            --border: #282828;
            --border-strong: #3a3a3a;
            --accent: #10b981;
            --accent-hover: #059669;
            --accent-pressed: #047857;
            --text-main: #f5f5f5;
            --text-muted: #a3a3a3;
            --green: #10b981;
            --red: #ef4444;
            --red-hover: #dc2626;
            --orange: #fbbf24;
            --gold: #ffd700;
            --mana: #29b6f6;
            --nav-chip: #1c1c1c;
        }
        * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; }
        body { background: var(--bg-dark); color: var(--text-main); font-size: 14px; -webkit-tap-highlight-color: transparent; }
        .header { background: var(--surface); padding: 12px 16px; border-bottom: 1px solid var(--border); display: flex; align-items: center; justify-content: space-between; position: sticky; top: 0; z-index: 100; }
        .header h1 { font-size: 1.15rem; font-weight: 700; color: #fff; display: flex; align-items: center; gap: 8px; }
        .header .badge { font-size: 0.72rem; padding: 3px 8px; border-radius: 99px; background: rgba(16,185,129,0.2); color: #34d399; font-weight: 600; border: 1px solid rgba(16,185,129,0.4); }
        .container { max-width: 900px; margin: 0 auto; padding: 12px; }
        .tabs { display: flex; gap: 6px; overflow-x: auto; padding-bottom: 8px; margin-bottom: 12px; border-bottom: 1px solid var(--border); }
        .tab-btn { background: var(--surface-elevated); border: 1.5px solid var(--border); color: var(--text-muted); font-size: 0.9rem; font-weight: 600; padding: 8px 14px; border-radius: 8px; cursor: pointer; white-space: nowrap; transition: all 0.15s ease; }
        .tab-btn:hover { background: var(--surface-alt); border-color: var(--border-strong); color: var(--text-main); }
        .tab-btn.active { background: #14231a; border-color: #10b981; color: #ffffff; font-weight: 700; }
        .tab-content { display: none; }
        .tab-content.active { display: block; }
        .card { background: var(--surface); border: 1px solid var(--border); border-radius: 12px; padding: 14px; margin-bottom: 12px; }
        .card-title { font-size: 0.85rem; font-weight: 700; text-transform: uppercase; letter-spacing: 0.5px; color: var(--text-muted); margin-bottom: 10px; display: flex; justify-content: space-between; }
        .stat-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 10px; }
        @media(min-width: 600px) { .stat-grid { grid-template-columns: repeat(4, 1fr); } }
        .stat-box { background: var(--surface-elevated); padding: 10px; border-radius: 8px; border: 1px solid var(--border); }
        .stat-box .label { font-size: 0.75rem; color: var(--text-muted); margin-bottom: 4px; }
        .stat-box .val { font-size: 1.1rem; font-weight: 700; color: #fff; }
        .bar-wrap { background: var(--bg-dark); border-radius: 99px; height: 16px; overflow: hidden; margin-top: 4px; position: relative; border: 1px solid var(--border); }
        .bar-fill { height: 100%; transition: width 0.3s; }
        .bar-fill.hp { background: linear-gradient(90deg, #dc2626, #ef4444); }
        .bar-fill.mp { background: linear-gradient(90deg, #0284c7, #29b6f6); }
        .bar-text { position: absolute; width: 100%; text-align: center; top: 0; line-height: 16px; font-size: 0.7rem; font-weight: 700; color: #fff; text-shadow: 0 1px 2px #000; }
        .btn-row { display: flex; gap: 8px; flex-wrap: wrap; margin-top: 10px; }
        .btn { background: var(--surface-elevated); border: 1.5px solid var(--border); color: #ffffff; padding: 10px 14px; border-radius: 8px; font-weight: 600; cursor: pointer; flex: 1; text-align: center; min-width: 80px; transition: all 0.15s ease; }
        .btn:hover { background: var(--surface-alt); border-color: var(--border-strong); color: #ffffff; }
        .btn:active { transform: scale(0.98); }
        .btn-primary { background: #14231a; border-color: #10b981; color: #ffffff; font-weight: 600; }
        .btn-primary:hover { background: #1f3527; border-color: #34d399; color: #ffffff; }
        .btn-green { background: #14231a; border-color: #10b981; color: #ffffff; font-weight: 600; }
        .btn-green:hover { background: #1f3527; border-color: #34d399; color: #ffffff; }
        .btn-red { background: #231416; border-color: #ef4444; color: #ffffff; font-weight: 600; }
        .btn-red:hover { background: #351c20; border-color: #f87171; color: #ffffff; }
        .btn-orange { background: #231b10; border-color: #fbbf24; color: #ffffff; font-weight: 600; }
        .btn-orange:hover { background: #352917; border-color: #fcd34d; color: #ffffff; }
        .input-row { display: flex; gap: 8px; margin-bottom: 8px; }
        .input-row input { flex: 1; background: var(--surface-elevated); border: 1px solid var(--border); color: #fff; padding: 10px 12px; border-radius: 8px; font-size: 0.9rem; outline: none; }
        .scripts-list { max-height: 480px; overflow-y: auto; display: flex; flex-direction: column; gap: 8px; }
        .script-item { background: var(--surface-elevated); padding: 12px 14px; border-radius: 8px; display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; border: 1px solid var(--border); }
        .script-item .name { font-weight: 600; color: #f5f5f5; font-size: 0.95rem; }
        .script-item .cat { font-size: 0.75rem; color: #34d399; font-weight: 600; }
        .script-item .act-btn, .act-btn { padding: 8px 16px; border-radius: 6px; background: #14231a; color: #ffffff; border: 1.5px solid #10b981; font-weight: 600; cursor: pointer; white-space: nowrap; flex-shrink: 0; transition: all 0.15s ease; }
        .script-item .act-btn:hover, .act-btn:hover { background: #1f3527; border-color: #34d399; color: #ffffff; }
        .badge-running { background: rgba(16,185,129,0.15); color: #34d399; padding: 2px 8px; border-radius: 6px; font-weight: 600; border: 1px solid rgba(16,185,129,0.35); }
        .badge-paused { background: rgba(251,191,36,0.15); color: #fbbf24; padding: 2px 8px; border-radius: 6px; font-weight: 600; border: 1px solid rgba(251,191,36,0.35); }
        .badge-stopped { background: rgba(239,68,68,0.15); color: #f87171; padding: 2px 8px; border-radius: 6px; font-weight: 600; border: 1px solid rgba(239,68,68,0.35); }
        .toast { position: fixed; bottom: 20px; left: 50%; transform: translateX(-50%); background: var(--surface-alt); border: 1px solid var(--accent); color: #fff; padding: 8px 16px; border-radius: 8px; font-size: 0.85rem; opacity: 0; transition: opacity 0.3s; pointer-events: none; z-index: 1000; box-shadow: 0 4px 12px rgba(0,0,0,0.5); }
        .toast.show { opacity: 1; }
    </style>
</head>
<body>
    <div class="header">
        <h1>VibeSkua Remote <span class="badge" id="botServerBadge">Connected</span></h1>
        <div id="uptimeDisplay" style="color: var(--text-muted); font-size: 0.85rem;">00:00:00</div>
    </div>

    <div class="container">
        <div class="tabs">
            <button class="tab-btn active" onclick="switchTab('dashboard')">Dashboard</button>
            <button class="tab-btn" onclick="switchTab('scripts')">Scripts</button>
            <button class="tab-btn" onclick="switchTab('controls')">Controls</button>
            <button class="tab-btn" onclick="switchTab('drops')">Drops & Quests</button>
            <button class="tab-btn" onclick="switchTab('inventory')">Inventory & Bank</button>
        </div>

        <!-- DASHBOARD TAB -->
        <div id="tab-dashboard" class="tab-content active">
            <!-- Player Card -->
            <div class="card">
                <div class="card-title">
                    <span id="playerName">Connecting...</span>
                    <span id="playerClass" style="color:var(--text-muted); font-weight:normal;">Class: -</span>
                </div>
                <div>
                    <div style="display:flex; justify-content:space-between; font-size:0.8rem; margin-bottom:2px;">
                        <span>Health</span>
                        <span id="hpText">0 / 0</span>
                    </div>
                    <div class="bar-wrap">
                        <div id="hpFill" class="bar-fill hp" style="width:0%;"></div>
                        <div id="hpOverlay" class="bar-text">0%</div>
                    </div>
                </div>
                <div style="margin-top:10px;">
                    <div style="display:flex; justify-content:space-between; font-size:0.8rem; margin-bottom:2px;">
                        <span>Mana</span>
                        <span id="mpText">0 / 0</span>
                    </div>
                    <div class="bar-wrap">
                        <div id="mpFill" class="bar-fill mp" style="width:0%;"></div>
                        <div id="mpOverlay" class="bar-text">0%</div>
                    </div>
                </div>
            </div>

            <!-- Bot & Script State -->
            <div class="card">
                <div class="card-title">
                    <span>Active Script</span>
                    <span id="scriptBadge" class="badge-stopped">STOPPED</span>
                </div>
                <div style="font-size:1.05rem; font-weight:700;" id="currentScriptName">No script loaded</div>
                <div style="font-size:0.75rem; color:var(--text-muted); margin-top:2px; display:none;" id="currentScriptPath"></div>

                <div style="background:var(--surface-elevated); border:1px solid var(--border); border-radius:8px; padding:10px; margin-top:10px;">
                    <div style="font-size:0.72rem; font-weight:700; color:var(--accent); text-transform:uppercase; letter-spacing:0.5px; margin-bottom:4px;">📍 Current Step / Progress</div>
                    <div id="scriptLastLog" style="font-size:0.85rem; font-weight:600; color:var(--text-main);">No script active</div>
                    <div id="scriptLocationTarget" style="font-size:0.75rem; color:var(--text-muted); margin-top:6px; display:none;"></div>
                    <div id="scriptQuestProgress" style="font-size:0.75rem; color:var(--text-main); background:rgba(16,185,129,0.08); border:1px solid rgba(16,185,129,0.25); border-radius:6px; padding:4px 8px; margin-top:6px; display:none;"></div>
                </div>

                <div style="margin-top:10px;">
                    <div style="font-size:0.72rem; font-weight:700; color:var(--text-muted); text-transform:uppercase; letter-spacing:0.5px; margin-bottom:4px;">Recent Script Output</div>
                    <div id="scriptLogsConsole" style="background:var(--surface-elevated); border:1px solid var(--border); border-radius:8px; padding:8px 10px; max-height:130px; overflow-y:auto; font-family:monospace; font-size:0.75rem; color:var(--text-muted);">
                        Waiting for script output...
                    </div>
                </div>

                <div class="btn-row" style="margin-top:12px;">
                    <button class="btn btn-green" onclick="postAction('/api/scripts/start')">▶ Start</button>
                    <button class="btn btn-orange" onclick="postAction('/api/scripts/pause')">⏸ Pause</button>
                    <button class="btn btn-red" onclick="postAction('/api/scripts/stop')">⏹ Stop</button>
                </div>
            </div>

            <!-- Stats Grid -->
            <div class="card">
                <div class="card-title">Statistics</div>
                <div class="stat-grid">
                    <div class="stat-box"><div class="label">Kills</div><div class="val" id="statKills">0</div></div>
                    <div class="stat-box"><div class="label">Drops</div><div class="val" id="statDrops">0</div></div>
                    <div class="stat-box"><div class="label">Quests</div><div class="val" id="statQuests">0</div></div>
                    <div class="stat-box"><div class="label">Relogs</div><div class="val" id="statRelogs">0</div></div>
                    <div class="stat-box"><div class="label">Gold</div><div class="val" style="color:var(--gold);" id="statGold">0</div></div>
                    <div class="stat-box"><div class="label">ACs</div><div class="val" id="statACs">0</div></div>
                    <div class="stat-box"><div class="label">Location</div><div class="val" style="font-size:0.9rem;" id="statMap">-</div></div>
                    <div class="stat-box"><div class="label">Target</div><div class="val" style="font-size:0.9rem;" id="statTarget">None</div></div>
                </div>
            </div>
        </div>

        <!-- SCRIPTS TAB -->
        <div id="tab-scripts" class="tab-content">
            <div class="card">
                <div class="card-title">Bot Script Library</div>
                <div class="input-row">
                    <input type="text" id="scriptSearchInput" placeholder="Filter 1,800+ scripts..." oninput="filterScripts()" onkeydown="if(event.key==='Enter') filterScripts()">
                    <button class="btn" style="flex:0 0 80px;" onclick="fetchScripts()">Reload</button>
                </div>
                <div class="scripts-list" id="scriptsList">
                    <div style="text-align:center; padding:20px; color:var(--text-muted);">Loading scripts...</div>
                </div>
            </div>
        </div>

        <!-- CONTROLS TAB -->
        <div id="tab-controls" class="tab-content">
            <div class="card">
                <div class="card-title">Combat & Skills</div>
                <div class="btn-row">
                    <button class="btn btn-red" onclick="postAction('/api/combat/attack')">⚔ Attack</button>
                    <button class="btn" onclick="useSkill(1)">1: Auto</button>
                    <button class="btn" onclick="useSkill(2)">2</button>
                    <button class="btn" onclick="useSkill(3)">3</button>
                    <button class="btn" onclick="useSkill(4)">4</button>
                    <button class="btn btn-orange" onclick="useSkill(5)">5: Potion</button>
                </div>
                <div class="btn-row">
                    <button class="btn" onclick="postAction('/api/combat/cancel-target')">Clear Target</button>
                    <button class="btn btn-green" onclick="postAction('/api/player/rest')">Rest / Heal</button>
                    <button class="btn btn-primary" onclick="postAction('/api/bank/open')">🏦 Open Bank</button>
                </div>
            </div>

            <div class="card">
                <div class="card-title">Movement & Navigation</div>
                <div class="input-row">
                    <input type="text" id="jumpCellInput" placeholder="Cell (e.g. Enter, r2)" onkeydown="if(event.key==='Enter') jumpCell()">
                    <input type="text" id="jumpPadInput" placeholder="Pad (e.g. Spawn, Left)" onkeydown="if(event.key==='Enter') jumpCell()">
                    <button class="btn" style="flex:0 0 70px;" onclick="jumpCell()">Jump</button>
                </div>
                <div class="input-row">
                    <input type="text" id="gotoTargetInput" placeholder="Player name to /goto" onkeydown="if(event.key==='Enter') gotoPlayer()">
                    <button class="btn" style="flex:0 0 70px;" onclick="gotoPlayer()">Goto</button>
                </div>
                <div class="input-row">
                    <input type="text" id="joinMapInput" placeholder="Map name (e.g. yulgar)" onkeydown="if(event.key==='Enter') joinMap()">
                    <button class="btn" style="flex:0 0 70px;" onclick="joinMap()">Join</button>
                </div>
            </div>

            <div class="card">
                <div class="card-title">Options & Toggles</div>
                <div class="btn-row">
                    <button class="btn" onclick="toggleOption('LagKiller')">Toggle Lag Killer</button>
                    <button class="btn" onclick="toggleOption('AutoRelogin')">Toggle Auto Relogin</button>
                    <button class="btn" onclick="toggleOption('InfiniteRange')">Toggle Infinite Range</button>
                    <button class="btn" onclick="toggleOption('HeadlessMode')">Toggle Headless Mode</button>
                    <button class="btn" onclick="toggleOption('StreamerMode')">Toggle Streamer Mode</button>
                </div>
            </div>

            <div class="card">
                <div class="card-title">Send In-Game Message</div>
                <div class="input-row">
                    <input type="text" id="chatMessageInput" placeholder="Message to send to zone chat..." onkeydown="if(event.key==='Enter') sendChatMessage()">
                    <button class="btn" style="flex:0 0 70px;" onclick="sendChatMessage()">Send</button>
                </div>
            </div>

            <div class="card">
                <div class="card-title">Server Connection &amp; Relog</div>
                <button class="btn btn-orange" onclick="postAction('/api/player/relog')">🔄 Relog / Reconnect to Server</button>
            </div>
        </div>

        <!-- DROPS & QUESTS TAB -->
        <div id="tab-drops" class="tab-content">
            <div class="card">
                <div class="card-title">
                    <span>Drop Stack</span>
                    <button class="btn btn-green" style="padding:4px 10px; font-size:0.75rem;" onclick="postAction('/api/drops/pickup-all')">Pickup All</button>
                </div>
                <div id="dropList" style="display:flex; flex-direction:column; gap:6px;">
                    <div style="color:var(--text-muted); font-size:0.85rem;">No active drops on screen</div>
                </div>
            </div>

            <div class="card">
                <div class="card-title">
                    <span>Temporary / Quest Items</span>
                    <button class="btn btn-green" style="padding:4px 10px; font-size:0.75rem;" onclick="loadTempInv()">Refresh</button>
                </div>
                <div id="tempDropList" style="display:flex; flex-direction:column; gap:6px;">
                    <div style="color:var(--text-muted); font-size:0.85rem;">No temporary items</div>
                </div>
            </div>

            <div class="card">
                <div class="card-title">Active Quests</div>
                <div id="questsList" style="display:flex; flex-direction:column; gap:6px;">
                    <div style="color:var(--text-muted); font-size:0.85rem;">No active quests</div>
                </div>
            </div>
        </div>

        <!-- INVENTORY & BANK TAB -->
        <div id="tab-inventory" class="tab-content">
            <div class="card">
                <div class="card-title">
                    <span id="invTabTitle">Inventory Items</span>
                </div>
                <div style="display:flex; gap:8px; margin-bottom:12px;">
                    <button class="btn btn-primary" id="btnInvModeInv" style="padding:8px 14px; font-size:0.85rem; font-weight:700;" onclick="setInvMode('inventory')">Inventory</button>
                    <button class="btn" id="btnInvModeBank" style="padding:8px 14px; font-size:0.85rem; font-weight:700;" onclick="setInvMode('bank')">Bank</button>
                    <button class="btn btn-primary" style="padding:8px 16px; font-size:0.85rem; white-space:nowrap; font-weight:700; flex:1.2;" onclick="postAction('/api/bank/open')">🏦 Open Bank</button>
                </div>
                <div class="input-row">
                    <input type="text" id="invSearchInput" placeholder="Filter items by name or category..." oninput="filterInv()" onkeydown="if(event.key==='Enter') filterInv()">
                    <button class="btn" style="flex:0 0 70px;" onclick="loadInv()">Refresh</button>
                </div>
                <div style="display:flex; gap:6px; overflow-x:auto; padding-bottom:8px; margin-bottom:8px;" id="invCategoryFilter">
                    <button class="btn btn-green" style="padding:4px 8px; font-size:0.75rem;" onclick="filterInvCat('All', this)">All</button>
                    <button class="btn" style="padding:4px 8px; font-size:0.75rem;" onclick="filterInvCat('Weapons', this)">Weapons</button>
                    <button class="btn" style="padding:4px 8px; font-size:0.75rem;" onclick="filterInvCat('Classes', this)">Classes</button>
                    <button class="btn" style="padding:4px 8px; font-size:0.75rem;" onclick="filterInvCat('Armors', this)">Armors</button>
                    <button class="btn" style="padding:4px 8px; font-size:0.75rem;" onclick="filterInvCat('Helms', this)">Helms</button>
                    <button class="btn" style="padding:4px 8px; font-size:0.75rem;" onclick="filterInvCat('Capes', this)">Capes</button>
                    <button class="btn" style="padding:4px 8px; font-size:0.75rem;" onclick="filterInvCat('Pets', this)">Pets</button>
                    <button class="btn" style="padding:4px 8px; font-size:0.75rem;" onclick="filterInvCat('Misc', this)">Misc</button>
                    <button class="btn" style="padding:4px 8px; font-size:0.75rem;" onclick="filterInvCat('AC Only', this)">AC Only</button>
                </div>
                <div id="invCountLabel" style="font-size:0.8rem; color:var(--text-muted); margin-bottom:8px;">0 items</div>
                <div class="scripts-list" id="invList">
                    <div style="text-align:center; padding:20px; color:var(--text-muted);">Loading items...</div>
                </div>
            </div>
        </div>
    </div>

    <div id="toast" class="toast">Action executed</div>

    <script>
        let allScripts = [];

        function showToast(msg) {
            const t = document.getElementById('toast');
            t.innerText = msg;
            t.classList.add('show');
            setTimeout(() => t.classList.remove('show'), 2000);
        }

        function switchTab(name) {
            document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
            document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
            event.target.classList.add('active');
            document.getElementById('tab-' + name).classList.add('active');
            if (name === 'scripts' && allScripts.length === 0) fetchScripts();
            if (name === 'inventory') loadInv();
        }

        async function postAction(url, data = {}) {
            try {
                const res = await fetch(url, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(data)
                });
                const json = await res.json();
                showToast(json.message || 'Success');
                updateStatus();
            } catch (err) {
                showToast('Action failed: ' + err);
            }
        }

        async function updateStatus() {
            try {
                const res = await fetch('/api/status');
                const data = await res.json();

                document.getElementById('playerName').innerText = data.username + ' (Lv. ' + data.level + ')';
                document.getElementById('playerClass').innerText = 'Class: ' + data.current_class;
                document.getElementById('uptimeDisplay').innerText = data.uptime;

                // HP Bar
                const hpPct = data.max_hp > 0 ? Math.min(100, Math.round((data.hp / data.max_hp) * 100)) : 0;
                document.getElementById('hpFill').style.width = hpPct + '%';
                document.getElementById('hpText').innerText = data.hp.toLocaleString() + ' / ' + data.max_hp.toLocaleString();
                document.getElementById('hpOverlay').innerText = hpPct + '%';

                // MP Bar
                const mpPct = data.max_mp > 0 ? Math.min(100, Math.round((data.mp / data.max_mp) * 100)) : 0;
                document.getElementById('mpFill').style.width = mpPct + '%';
                document.getElementById('mpText').innerText = data.mp.toLocaleString() + ' / ' + data.max_mp.toLocaleString();
                document.getElementById('mpOverlay').innerText = mpPct + '%';

                // Script status
                const badge = document.getElementById('scriptBadge');
                if (data.script_running) {
                    if (data.script_paused) {
                        badge.innerText = 'PAUSED';
                        badge.className = 'badge-paused';
                    } else {
                        badge.innerText = 'RUNNING';
                        badge.className = 'badge-running';
                    }
                } else if (data.loaded_script) {
                    badge.innerText = 'LOADED';
                    badge.className = 'badge-running';
                } else {
                    badge.innerText = 'STOPPED';
                    badge.className = 'badge-stopped';
                }
                document.getElementById('currentScriptName').innerText = data.loaded_script || 'No script loaded';

                const scriptPathEl = document.getElementById('currentScriptPath');
                if (scriptPathEl) {
                    scriptPathEl.innerText = data.loaded_script_path || '';
                    scriptPathEl.style.display = data.loaded_script_path ? 'block' : 'none';
                }
                const stepEl = document.getElementById('scriptLastLog');
                if (stepEl) {
                    if (data.script_last_log) {
                        stepEl.innerText = data.script_last_log;
                    } else if (data.script_running) {
                        stepEl.innerText = `Active in ${data.map || 'world'} [${data.cell || 'cell'}]`;
                    } else {
                        stepEl.innerText = data.loaded_script ? 'Script loaded, ready to run' : 'No active script';
                    }
                }
                const locTargetEl = document.getElementById('scriptLocationTarget');
                if (locTargetEl) {
                    if (data.script_running || data.playing) {
                        const locText = `📍 ${data.map || 'world'} [${data.cell || 'Enter'}, ${data.pad || 'Spawn'}]`;
                        const targetText = data.has_target ? `⚔️ ${data.target_name} (${data.target_hp}/${data.target_max_hp})` : 'Target: none';
                        locTargetEl.innerText = `${locText} • ${targetText}`;
                        locTargetEl.style.display = 'block';
                    } else {
                        locTargetEl.style.display = 'none';
                    }
                }
                const questProgEl = document.getElementById('scriptQuestProgress');
                if (questProgEl) {
                    if (data.active_quests && data.active_quests.length > 0) {
                        const qLines = data.active_quests.slice(0, 3).map(q => {
                            const reqsStr = (q.requirements && q.requirements.length > 0)
                                ? q.requirements.map(r => `${r.name}: ${r.current}/${r.required}`).join(', ')
                                : (q.can_complete ? 'Ready to turn in!' : 'In progress');
                            return `<div>📜 <strong>${q.name}</strong>: ${reqsStr}</div>`;
                        }).join('');
                        questProgEl.innerHTML = qLines;
                        questProgEl.style.display = 'block';
                    } else {
                        questProgEl.style.display = 'none';
                    }
                }
                const logsConsole = document.getElementById('scriptLogsConsole');
                if (logsConsole && data.script_logs) {
                    if (data.script_logs.length > 0) {
                        logsConsole.innerHTML = data.script_logs.map(l => `<div style="line-height:1.35; border-bottom:1px solid rgba(255,255,255,0.04); padding:2px 0;">› ${l}</div>`).join('');
                        logsConsole.scrollTop = logsConsole.scrollHeight;
                    } else {
                        logsConsole.innerHTML = '<div style="color:var(--text-muted);">Waiting for script output...</div>';
                    }
                }

                // Stats
                document.getElementById('statKills').innerText = data.kills.toLocaleString();
                document.getElementById('statDrops').innerText = data.drops.toLocaleString();
                document.getElementById('statQuests').innerText = data.quests_completed.toLocaleString();
                document.getElementById('statRelogs').innerText = data.relogins.toLocaleString();
                document.getElementById('statGold').innerText = data.gold.toLocaleString();
                document.getElementById('statACs').innerText = data.coins.toLocaleString();
                document.getElementById('statMap').innerText = data.map + ' (' + data.cell + ')';
                document.getElementById('statTarget').innerText = data.has_target ? data.target_name : 'None';

                // Drops list
                const dropDiv = document.getElementById('dropList');
                if (dropDiv) {
                    if (data.current_drops && data.current_drops.length > 0) {
                        dropDiv.innerHTML = data.current_drops.map(d => `
                            <div class="script-item">
                                <span class="name">${d}</span>
                                <button class="act-btn" onclick="postAction('/api/drops/pickup', { item: '${d.replace(/'/g, "\\'")}' })">Pickup</button>
                            </div>
                        `).join('');
                    } else {
                        dropDiv.innerHTML = '<div style="color:var(--text-muted); font-size:0.85rem;">No active drops</div>';
                    }
                }

                // Temp items list
                const tempDiv = document.getElementById('tempDropList');
                if (tempDiv) {
                    if (data.temp_items && data.temp_items.length > 0) {
                        tempDiv.innerHTML = data.temp_items.map(t => `
                            <div class="script-item">
                                <span class="name">${t.name}</span>
                                <span class="cat" style="color:var(--gold);">Qty: ${t.quantity} / ${t.max_quantity || 1}</span>
                            </div>
                        `).join('');
                    } else {
                        tempDiv.innerHTML = '<div style="color:var(--text-muted); font-size:0.85rem;">No temporary items</div>';
                    }
                }

                // Quests list
                const qDiv = document.getElementById('questsList');
                if (qDiv) {
                    if (data.active_quests && data.active_quests.length > 0) {
                        qDiv.innerHTML = data.active_quests.map(q => {
                            const reqsHtml = (q.requirements && q.requirements.length > 0)
                                ? '<div style="margin-top:4px; font-size:0.75rem; color:#94a3b8;">' +
                                  q.requirements.map(r => `• ${r.name}: <span style="color:${r.current >= r.required ? '#34d399' : '#f59e0b'}; font-weight:600;">${r.current} / ${r.required}</span> ${r.is_temp ? '<span style="color:#38bdf8;">(Temp)</span>' : ''}`).join('<br>') +
                                  '</div>'
                                : '';
                            const turnInBtn = q.can_complete
                                ? `<button class="act-btn" style="margin-left:8px;" onclick="postAction('/api/quests/complete', { id: ${q.id} })">Turn In</button>`
                                : '';
                            return `
                                <div class="script-item" style="flex-direction:column; align-items:flex-start; gap:4px;">
                                    <div style="display:flex; justify-content:space-between; align-items:center; width:100%;">
                                        <span class="name" style="font-weight:600;">${q.name}</span>
                                        <div style="display:flex; align-items:center; gap:6px;">
                                            <span class="cat">ID: ${q.id}</span>
                                            ${turnInBtn}
                                        </div>
                                    </div>
                                    ${reqsHtml}
                                </div>
                            `;
                        }).join('');
                    } else {
                        qDiv.innerHTML = '<div style="color:var(--text-muted); font-size:0.85rem;">No active quests</div>';
                    }
                }
            } catch (err) {
                document.getElementById('botServerBadge').innerText = 'Offline';
            }
        }

        async function fetchScripts() {
            try {
                const res = await fetch('/api/scripts');
                const data = await res.json();
                allScripts = data.scripts || [];
                renderScripts(allScripts);
            } catch (err) {
                showToast('Failed to load scripts: ' + err);
            }
        }

        function renderScripts(list) {
            const container = document.getElementById('scriptsList');
            if (list.length === 0) {
                container.innerHTML = '<div style="text-align:center; padding:20px; color:var(--text-muted);">No scripts found</div>';
                return;
            }
            container.innerHTML = list.slice(0, 100).map(s => {
                const tagsHtml = (s.tags && s.tags.length > 0)
                    ? `<div style="display:flex; flex-wrap:wrap; gap:4px; margin-top:6px;">` +
                        s.tags.slice(0, 4).map(t => `<span style="background:rgba(255,255,255,0.06); color:#94a3b8; font-size:0.7rem; padding:1px 6px; border-radius:4px;">#${t}</span>`).join('') +
                      `</div>`
                    : '';
                const descHtml = s.description
                    ? `<div style="font-size:0.8rem; color:#94a3b8; margin-top:4px; line-height:1.3;">${s.description}</div>`
                    : '';
                const dlBadge = s.downloaded
                    ? `<span style="background:rgba(16,185,129,0.15); color:#34d399; font-size:0.7rem; padding:2px 6px; border-radius:4px; font-weight:600;">Downloaded</span>`
                    : `<span style="background:rgba(255,255,255,0.08); color:#94a3b8; font-size:0.7rem; padding:2px 6px; border-radius:4px;">Cloud</span>`;

                return `
                <div class="script-item">
                    <div style="flex:1; min-width:0;">
                        <div style="display:flex; align-items:center; gap:8px; flex-wrap:wrap;">
                            <span class="name">${s.name}</span>
                            <span class="cat">${s.category}</span>
                            ${dlBadge}
                        </div>
                        ${descHtml}
                        ${tagsHtml}
                    </div>
                    <button class="act-btn" onclick="startScript('${s.path.replace(/'/g, "\\'")}')">▶ Run</button>
                </div>`;
            }).join('');
        }

        function filterScripts() {
            const q = document.getElementById('scriptSearchInput').value.toLowerCase();
            const filtered = allScripts.filter(s =>
                s.name.toLowerCase().includes(q) ||
                s.category.toLowerCase().includes(q) ||
                (s.description && s.description.toLowerCase().includes(q)) ||
                (s.tags && s.tags.some(t => t.toLowerCase().includes(q)))
            );
            renderScripts(filtered);
        }

        function startScript(path) {
            postAction('/api/scripts/start', { script: path });
        }

        function useSkill(num) {
            postAction('/api/combat/skill', { skill: num });
        }

        function jumpCell() {
            const cell = document.getElementById('jumpCellInput').value.trim();
            const pad = document.getElementById('jumpPadInput').value.trim() || 'Spawn';
            if (!cell) return showToast('Cell name required');
            postAction('/api/player/jump', { cell, pad });
        }

        function gotoPlayer() {
            const target = document.getElementById('gotoTargetInput').value.trim();
            if (!target) return showToast('Target required');
            postAction('/api/player/goto', { target });
        }

        function joinMap() {
            const map = document.getElementById('joinMapInput').value.trim();
            if (!map) return showToast('Map required');
            postAction('/api/player/join', { map });
        }

        function toggleOption(opt) {
            postAction('/api/options/toggle', { option: opt });
        }

        function sendChatMessage() {
            const msg = document.getElementById('chatMessageInput').value.trim();
            if (!msg) return;
            postAction('/api/chat/send', { message: msg });
            document.getElementById('chatMessageInput').value = '';
        }

        let allInv = [];
        let currentInvMode = 'inventory';
        let currentInvCat = 'All';

        function setInvMode(mode) {
            currentInvMode = mode === 'bank' ? 'bank' : 'inventory';
            const titleMap = { 'inventory': 'Inventory Items', 'bank': 'Bank Items' };
            document.getElementById('invTabTitle').innerText = titleMap[currentInvMode] || 'Inventory Items';
            const invBtn = document.getElementById('btnInvModeInv');
            const bankBtn = document.getElementById('btnInvModeBank');
            if (invBtn) invBtn.className = currentInvMode === 'inventory' ? 'btn btn-primary' : 'btn';
            if (bankBtn) bankBtn.className = currentInvMode === 'bank' ? 'btn btn-primary' : 'btn';
            loadInv();
        }

        async function loadTempInv() {
            try {
                const res = await fetch('/api/temp-items');
                const data = await res.json();
                const tempDiv = document.getElementById('tempDropList');
                const list = data.items || [];
                if (list.length > 0) {
                    tempDiv.innerHTML = list.map(t => `
                        <div class="script-item">
                            <span class="name">${t.name}</span>
                            <span class="cat" style="color:var(--gold);">Qty: ${t.quantity} / ${t.max_quantity || 1}</span>
                        </div>
                    `).join('');
                } else {
                    tempDiv.innerHTML = '<div style="color:var(--text-muted); font-size:0.85rem;">No temporary items</div>';
                }
            } catch (e) { }
        }

        async function loadInv() {
            try {
                let endpoint = currentInvMode === 'bank' ? '/api/bank' : '/api/inventory';

                const res = await fetch(endpoint);
                const data = await res.json();
                allInv = data.items || [];
                const label = document.getElementById('invCountLabel');
                if (label) {
                    if (currentInvMode === 'inventory') {
                        label.innerText = `Total: ${allInv.length} items • ${data.used || allInv.length} / ${data.max || '?'} slots`;
                    } else {
                        label.innerText = `Total: ${allInv.length} items in Bank • ${data.used || allInv.length} / ${data.max || '?'} slots ${data.loaded ? '(Loaded)' : ''}`;
                    }
                }
                filterInv();
            } catch (e) {
                showToast('Failed to load items');
            }
        }

        function filterInvCat(cat, btn) {
            currentInvCat = cat;
            const container = document.getElementById('invCategoryFilter');
            if (container) {
                container.querySelectorAll('.btn').forEach(b => {
                    b.className = 'btn';
                });
            }
            if (btn) btn.className = 'btn btn-green';
            filterInv();
        }

        function isItemInCat(item, cat) {
            if (cat === 'All') return true;
            if (cat === 'AC Only') return !!item.ac;
            const c = (item.category || '').toLowerCase();
            if (cat === 'Weapons') {
                return ['sword', 'axe', 'dagger', 'gun', 'handgun', 'rifle', 'bow', 'mace', 'gauntlet', 'polearm', 'staff', 'wand', 'whip', 'weapon'].some(w => c.includes(w));
            }
            if (cat === 'Classes') return c.includes('class');
            if (cat === 'Armors') return c.includes('armor');
            if (cat === 'Helms') return c.includes('helm');
            if (cat === 'Capes') return c.includes('cape');
            if (cat === 'Pets') return c.includes('pet');
            if (cat === 'Misc') return !isItemInCat(item, 'Weapons') && !isItemInCat(item, 'Classes') && !isItemInCat(item, 'Armors') && !isItemInCat(item, 'Helms') && !isItemInCat(item, 'Capes') && !isItemInCat(item, 'Pets');
            return c.includes(cat.toLowerCase());
        }

        function renderInv(items) {
            const container = document.getElementById('invList');
            if (!items || items.length === 0) {
                container.innerHTML = '<div style="text-align:center; padding:20px; color:var(--text-muted);">No items found</div>';
                return;
            }
            container.innerHTML = items.map(i => {
                let actionBtn = '';
                if (currentInvMode === 'bank') {
                    actionBtn = `<button class="act-btn" onclick="postAction('/api/inventory/unbank', { item: '${i.name.replace(/'/g, "\\'")}' })">To Inv</button>`;
                } else if (currentInvMode === 'inventory') {
                    const isEquipable = ['class', 'armor', 'helm', 'cape', 'pet', 'sword', 'axe', 'dagger', 'gun', 'bow', 'mace', 'staff', 'wand', 'polearm', 'weapon'].some(k => (i.category || '').toLowerCase().includes(k));
                    const equipBtn = isEquipable
                        ? (i.equipped
                            ? `<button class="act-btn" style="background:var(--surface-elevated); border:1.5px solid var(--border); color:var(--text-muted); cursor:default; margin-right:6px;">Equipped</button>`
                            : `<button class="act-btn" style="margin-right:6px;" onclick="postAction('/api/inventory/equip', { id: ${i.id} })">Equip</button>`)
                        : '';
                    actionBtn = `${equipBtn}<button class="act-btn" onclick="postAction('/api/inventory/bank', { item: '${i.name.replace(/'/g, "\\'")}' })">To Bank</button>`;
                }

                return `
                <div class="script-item">
                    <div>
                        <div class="name">${i.name} ${i.ac ? '<span style="color:var(--gold); font-size:0.7rem; font-weight:bold;">[AC]</span>' : ''} ${i.equipped ? '<span style="color:#10b981; font-size:0.7rem; font-weight:bold;">[EQUIPPED]</span>' : ''}</div>
                        <div class="cat">Qty: ${i.quantity} / ${i.max_quantity || 1} • ${i.category || 'Item'}</div>
                    </div>
                    <div style="display:flex; align-items:center;">
                        ${actionBtn}
                    </div>
                </div>`;
            }).join('');
        }

        function filterInv() {
            const q = (document.getElementById('invSearchInput').value || '').toLowerCase();
            const filtered = allInv.filter(i => {
                const matchesQuery = !q || (i.name && i.name.toLowerCase().includes(q)) || (i.category && i.category.toLowerCase().includes(q));
                const matchesCat = isItemInCat(i, currentInvCat);
                return matchesQuery && matchesCat;
            });
            renderInv(filtered);
        }

        setInterval(updateStatus, 1500);
        updateStatus();
    </script>
</body>
</html>
""";
    }
}
