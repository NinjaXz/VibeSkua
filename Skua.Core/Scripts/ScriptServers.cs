using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Newtonsoft.Json;
using Skua.Core.Flash;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models.Servers;
using Skua.Core.Utils;

namespace Skua.Core.Scripts;

public partial class ScriptServers : ObservableRecipient, IScriptServers
{
    public ScriptServers(
        Lazy<IFlashUtil> flash,
        Lazy<IScriptPlayer> player,
        Lazy<IScriptWait> wait,
        Lazy<IScriptOption> options,
        Lazy<IScriptBotStats> stats,
        Lazy<IScriptManager> manager,
        Lazy<ILogService> logger)
    {
        _lazyFlash = flash;
        _lazyPlayer = player;
        _lazyWait = wait;
        _lazyOptions = options;
        _lazyStats = stats;
        _lazyManager = manager;
        _lazyLogger = logger;
    }

    private readonly Lazy<IFlashUtil> _lazyFlash;
    private readonly Lazy<IScriptPlayer> _lazyPlayer;
    private readonly Lazy<IScriptWait> _lazyWait;
    private readonly Lazy<IScriptOption> _lazyOptions;
    private readonly Lazy<IScriptBotStats> _lazyStats;
    private readonly Lazy<IScriptManager> _lazyManager;
    private readonly Lazy<ILogService> _lazyLogger;

    private IFlashUtil Flash => _lazyFlash.Value;
    private IScriptPlayer Player => _lazyPlayer.Value;
    private IScriptWait Wait => _lazyWait.Value;
    private IScriptOption Options => _lazyOptions.Value;
    private IScriptBotStats Stats => _lazyStats.Value;
    private IScriptManager Manager => _lazyManager.Value;
    private ILogService Logger => _lazyLogger.Value;

    private bool _loginInfoSetted = false;
    private string _username;
    private string _password;

    public string LastIP { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public bool IsIntentionalLogout { get; set; }
    public string? CachedUsername => _loginInfoSetted && !string.IsNullOrWhiteSpace(_username) ? _username : (!string.IsNullOrWhiteSpace(Player.Username) && Player.Username != "loginInfo.strUsername" ? Player.Username : null);

    // Flash Objects Binding

    [ObjectBinding("serialCmd.servers")]
    private List<Server> _serverList = new();

    [ObservableProperty]
    [NotifyPropertyChangedRecipients]
    private List<Server> _cachedServers = new();

    // Flash Methods Binding

    [MethodCallBinding("login", RunMethodPre = true, GameFunction = true)]
    private void _login(string username, string password)
    {
        IsIntentionalLogout = false;
    }

    [MethodCallBinding("logout", RunMethodPost = true, GameFunction = true)]
    private void _logout()
    {
        IsIntentionalLogout = true;
        CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger.Default.Send<Skua.Core.Messaging.LogoutRequestedMessage, int>(new(), (int)Skua.Core.Messaging.MessageChannels.GameEvents);
        DisconnectToLogin();
    }

    public void DisconnectForRelogin()
    {
        IsIntentionalLogout = false;
        DisconnectToLogin();
    }

    private void DisconnectToLogin()
    {
        try
        {
            Flash.Call("resetConnectionState");
        }
        catch { }

        try
        {
            if (IsConnected)
                Flash.CallGameFunction("sfc.disconnect");
        }
        catch { }

        try
        {
            Flash.Call("killLoginModals");
        }
        catch { }

        try
        {
            Flash.CallGameFunction("gotoAndPlay", "Login");
        }
        catch { }
    }

    [MethodCallBinding("connectTo", RunMethodPost = true, GameFunction = true)]
    private bool _connectIP(string ip)
    {
        IsIntentionalLogout = false;
        Wait.ForTrue(() => !Manager.ShouldExit && Player.Playing && Flash.IsWorldLoaded, Options.LoginTimeout / 100);
        return Player.Playing;
    }

    [MethodCallBinding("connectTo", RunMethodPost = true, GameFunction = true)]
    private bool _connectIP(string ip, int port)
    {
        IsIntentionalLogout = false;
        Wait.ForTrue(() => !Manager.ShouldExit && Player.Playing && Flash.IsWorldLoaded, Options.LoginTimeout / 100);
        return Player.Playing;
    }

    [MethodCallBinding("connectToServer", RunMethodPost = true)]
    private bool _connectToServer(string server)
    {
        IsIntentionalLogout = false;
        Wait.ForTrue(() => !Manager.ShouldExit && Player.Playing && Flash.IsWorldLoaded, Options.LoginTimeout / 100);
        return Player.Playing;
    }

    [ObjectBinding("sfc.isConnected", RequireNotNull = "sfc", Default = "false")]
    private bool _isConnected;

    public void Login()
    {
        IsIntentionalLogout = false;
        Login(Player.Username, Player.Password);
    }

    public async ValueTask<List<Server>> GetServers(bool forceUpdate = false)
    {
        if (CachedServers.Count > 0 && !forceUpdate)
            return CachedServers;

        try
        {
            string response = await ValidatedHttpExtensions.GetStringAsync(HttpClients.GetGHClient()
, $"http://content.aq.com/game/api/data/servers")
                .ConfigureAwait(false);

            List<Server>? servers = JsonConvert.DeserializeObject<List<Server>>(response);
            if (servers == null || servers.Count == 0)
                return new();

            CachedServers = servers;
            return CachedServers;
        }
        catch
        {
            return new();
        }
    }

    public void SetLoginInfo(string username, string password)
    {
        _username = username;
        _password = password;
        _loginInfoSetted = true;
    }

    public bool Reconnect(string serverName, int loginDelay = 2000)
    {
        Login(Player.Username, Player.Password);
        Thread.Sleep(loginDelay);
        return ((IScriptServers)this).Connect(serverName);
    }

    public bool Reconnect(Server server, int loginDelay = 2000)
    {
        Login(Player.Username, Player.Password);
        Thread.Sleep(loginDelay);
        return ((IScriptServers)this).Connect(server);
    }

    private void ReloginLog(string message)
    {
        Logger.ScriptLog($"[Relogin] {message}");
    }
    private static bool IsValidReloginServerData(Server server)
    {
        return !string.IsNullOrWhiteSpace(server.Name) && !string.IsNullOrWhiteSpace(server.IP);
    }

    private static bool IsServerFull(Server server)
    {
        return server.MaxPlayers > 0 && server.PlayerCount >= server.MaxPlayers;
    }

    private bool IsNonMemberAccount()
    {
        int loginUpgradeDays = Flash.GetGameObjectStatic<int>("objLogin.iUpgDays", int.MinValue);
        if (loginUpgradeDays != int.MinValue)
            return loginUpgradeDays < 0;

        int worldUpgradeDays = Flash.GetGameObject<int>("world.myAvatar.objData.iUpgDays", int.MinValue);
        if (worldUpgradeDays != int.MinValue)
            return worldUpgradeDays < 0;

        return false;
    }

    private bool IsServerBlacklistedForRelogin(Server server)
    {
        if (!IsValidReloginServerData(server))
            return true;
        string serverName = server.Name ?? string.Empty;
        bool memberOnly = server.Upgrade && IsNonMemberAccount();
        return !server.Online
            || serverName.Contains("Test", StringComparison.OrdinalIgnoreCase)
            || IsServerFull(server)
            || memberOnly;
    }

    private string? GetBlacklistReason(Server server)
    {
        if (!IsValidReloginServerData(server))
            return "invalid";
        if (!server.Online)
            return "offline";
        if ((server.Name ?? string.Empty).Contains("Test", StringComparison.OrdinalIgnoreCase))
            return "test server";
        if (IsServerFull(server))
            return "full";
        if (server.Upgrade && IsNonMemberAccount())
            return "member-only";
        return null;
    }

    private List<Server> BuildReloginCandidates()
    {
        List<Server> candidates = new();
        AddReloginCandidates(candidates, CachedServers);
        AddReloginCandidates(candidates, ServerList);
        return candidates;
    }

    private void RefreshServersSync()
    {
        try
        {
            if (CachedServers.Count == 0)
            {
                GetServers(true).AsTask().GetAwaiter().GetResult();
                ReloginLog($"Refreshed server list ({CachedServers.Count} servers from API).");
            }
        }
        catch
        {
            ReloginLog("Failed to refresh server list; using existing cache.");
        }
    }

    private void AddReloginCandidates(List<Server> candidates, List<Server> source)
    {
        foreach (Server server in source)
        {
            if (IsServerBlacklistedForRelogin(server))
                continue;

            if (candidates.Exists(x => string.Equals(x.Name, server.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            candidates.Add(server);
        }
    }

    private static Server? FindByName(List<Server> candidates, string? serverName, bool exactMatch, HashSet<string>? attemptedServers = null)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return null;

        if (exactMatch)
        {
            return candidates.Find(x =>
                string.Equals(x.Name, serverName, StringComparison.OrdinalIgnoreCase)
                && (attemptedServers is null || !attemptedServers.Contains(x.Name ?? string.Empty)));
        }

        return candidates.Find(x =>
            (x.Name ?? string.Empty).Contains(serverName, StringComparison.OrdinalIgnoreCase)
            && (attemptedServers is null || !attemptedServers.Contains(x.Name ?? string.Empty)));
    }

    private Server? SelectReloginServer(string? preferredName = null, HashSet<string>? attemptedServers = null)
    {
        List<Server> candidates = BuildReloginCandidates();
        if (candidates.Count == 0)
            return null;

        HashSet<string> triedServers = attemptedServers ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Options is not ScriptOption { ReloginFallback: true })
        {
            // Without the Relogin Fallback option there is no hop to whichever server is free: the preferred and
            // Relogin Server picks are retried even after a failed attempt, and null makes the caller wait and retry
            Server? preferredServer = FindByName(candidates, preferredName, true);
            if (preferredServer is null && Options.AutoReloginAny)
                preferredServer = candidates.Find(x => x.IP != LastIP && !triedServers.Contains(x.Name ?? string.Empty));
            return preferredServer ?? FindByName(candidates, Options.ReloginServer, true);
        }

        Server? selectedServer = FindByName(candidates, preferredName, true, triedServers);
        if (selectedServer is not null)
            return selectedServer;

        if (Options.AutoReloginAny)
        {
            selectedServer = candidates.Find(x => x.IP != LastIP && !triedServers.Contains(x.Name ?? string.Empty));
            if (selectedServer is not null)
                return selectedServer;
        }

        selectedServer = FindByName(candidates, Options.ReloginServer, true, triedServers);
        if (selectedServer is not null)
            return selectedServer;

        selectedServer = candidates.Find(x => !triedServers.Contains(x.Name ?? string.Empty));
        if (selectedServer is not null)
            return selectedServer;

        if (attemptedServers is not null)
            attemptedServers.Clear();

        selectedServer = FindByName(candidates, preferredName, true);
        if (selectedServer is not null)
            return selectedServer;

        if (Options.AutoReloginAny)
        {
            selectedServer = candidates.Find(x => x.IP != LastIP);
            if (selectedServer is not null)
                return selectedServer;
        }

        selectedServer = FindByName(candidates, Options.ReloginServer, true);
        if (selectedServer is not null)
            return selectedServer;

        return candidates[0];
    }

    private Server? SelectReloginServerByName(string serverName, bool exactMatch, HashSet<string>? attemptedServers = null)
    {
        List<Server> candidates = BuildReloginCandidates();
        Server? selectedServer = FindByName(candidates, serverName, exactMatch, attemptedServers);
        if (selectedServer is not null)
            return selectedServer;

        return SelectReloginServer(serverName, attemptedServers);
    }

    public bool Relogin(Server? server = null)
    {
        RefreshServersSync();
        Server? reloginServer = server;
        if (reloginServer is not null)
        {
            string? blacklistReason = GetBlacklistReason(reloginServer);
            if (blacklistReason is not null)
            {
                ReloginLog($"Skipping requested server {reloginServer.Name} ({blacklistReason}).");
                reloginServer = null;
            }
        }

        if (reloginServer is null)
            reloginServer = SelectReloginServer();
        if (reloginServer is null)
        {
            ReloginLog("No eligible relogin server found.");
            return false;
        }

        return ReloginIP(reloginServer.IP);
    }

    public bool ReloginIP(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip))
        {
            ReloginLog("Cannot relogin: empty server IP.");
            return false;
        }
        bool autoRelogSwitch = Options.AutoRelogin;
        Options.AutoRelogin = false;
        ReloginLog($"Relogging using IP {ip}.");
        StrongReferenceMessenger.Default.Send<LogoutRequestedMessage, int>(new(), (int)MessageChannels.GameEvents);

        Thread.Sleep(2000);
        StrongReferenceMessenger.Default.Send<LogoutMessage, int>((int)MessageChannels.GameEvents);
        DisconnectForRelogin();

        Stats.Relogins++;
        if (_loginInfoSetted)
            Login(_username, _password);
        else
            Login(Player.Username, Player.Password);

        ReloginLog($"Waiting for server list screen before connecting to IP {ip}...");
        if (!WaitForServerListReady(CancellationToken.None).GetAwaiter().GetResult())
        {
            ReloginLog($"Authentication timed out or froze while connecting to IP {ip}.");
            Options.AutoRelogin = autoRelogSwitch;
            return false;
        }

        Thread.Sleep(100);
        try { Flash.Call("loadPolicyFile", ip, 5588); } catch { }
        try { Flash.Call("killLoginModals"); } catch { }
        ConnectIP(ip);

        Wait.ForTrue(() => Player.Playing && Flash.IsWorldLoaded, Options.LoginTimeout / 100);
        Options.AutoRelogin = autoRelogSwitch;
        bool connected = Player.Playing;
        ReloginLog(connected ? "Relogin by IP successful." : "Relogin by IP failed.");
        return connected;
    }

    public bool Relogin(string serverName)
    {
        RefreshServersSync();
        Server? requestedServer = CachedServers.Find(x => (x.Name ?? string.Empty).Contains(serverName, StringComparison.OrdinalIgnoreCase))
            ?? ServerList.Find(x => (x.Name ?? string.Empty).Contains(serverName, StringComparison.OrdinalIgnoreCase));
        if (requestedServer is not null)
        {
            string? blacklistReason = GetBlacklistReason(requestedServer);
            if (blacklistReason is not null)
                ReloginLog($"Server {requestedServer.Name} is excluded ({blacklistReason}), selecting fallback.");
        }

        Server? server = SelectReloginServerByName(serverName, false) ?? SelectReloginServer();
        if (server is null)
        {
            ReloginLog("No eligible relogin server found.");
            return false;
        }
        bool autoRelogSwitch = Options.AutoRelogin;
        Options.AutoRelogin = false;
        ReloginLog($"Relogging on server {server.Name} [{server.PlayerCount}/{server.MaxPlayers}].");

        StrongReferenceMessenger.Default.Send<LogoutRequestedMessage, int>(new(), (int)MessageChannels.GameEvents);
        StrongReferenceMessenger.Default.Send<LogoutMessage, int>((int)MessageChannels.GameEvents);
        DisconnectForRelogin();
        Stats.Relogins++;

        if (_loginInfoSetted)
            Login(_username, _password);
        else
            Login(Player.Username, Player.Password);

        ReloginLog($"Waiting for server list screen before connecting to {server.Name}...");
        if (!WaitForServerListReady(CancellationToken.None).GetAwaiter().GetResult())
        {
            ReloginLog($"Authentication timed out or froze while connecting to {server.Name}.");
            Options.AutoRelogin = autoRelogSwitch;
            return false;
        }

        Thread.Sleep(100);
        int port = server.Port > 0 ? server.Port : 5588;
        try { Flash.Call("loadPolicyFile", server.IP, port); } catch { }
        try { Flash.Call("killLoginModals"); } catch { }
        ConnectToServer(JsonConvert.SerializeObject(server));

        Wait.ForTrue(() => Player.Playing && Flash.IsWorldLoaded, Options.LoginTimeout / 100);
        Options.AutoRelogin = autoRelogSwitch;
        bool connected = Player.Playing;
        ReloginLog(connected ? $"Relogin successful on {server.Name}." : $"Relogin failed on {server.Name}.");
        return connected;
    }

    public bool EnsureRelogin(string serverName)
    {
        return Task.Run(() => EnsureRelogin(serverName, CancellationToken.None)).GetAwaiter().GetResult();
    }

    public async Task<bool> EnsureRelogin(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        IsIntentionalLogout = false;
        await GetServers(true);
        token.ThrowIfCancellationRequested();
        Server? server = SelectReloginServer();
        if (server is null)
        {
            ReloginLog("No eligible servers available for auto relogin.");
            return false;
        }

        ReloginLog($"Auto relogin started. Preferred server: {server.Name}.");

        return await EnsureLogin(server, token);
    }

    public async Task<bool> EnsureRelogin(string serverName, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        IsIntentionalLogout = false;
        await GetServers(true);
        token.ThrowIfCancellationRequested();
        Server? server = SelectReloginServerByName(serverName, true) ?? SelectReloginServer();
        if (server is null)
        {
            ReloginLog($"No eligible servers available for requested relogin target {serverName}.");
            return false;
        }

        ReloginLog($"Auto relogin started for target {serverName}. Preferred server: {server.Name}.");

        return await EnsureLogin(server, token);
    }

    private async Task WaitReloginDelay(CancellationToken token)
    {
        if (Options.ReloginTryDelay <= 0)
            return;

        try
        {
            int jitter = Random.Shared.Next(400, 1000);
            await Task.Delay(Options.ReloginTryDelay + jitter, token);
        }
        catch { }
    }

    private async Task<bool> WaitForServerListReady(CancellationToken token)
    {
        // Enforce a hard watchdog limit (max 15 seconds) so client never hangs indefinitely on server list
        int timeoutMs = Math.Min(15000, Options.LoginTimeout > 0 ? Options.LoginTimeout : 15000);
        using CancellationTokenSource waitServerList = new(timeoutMs);
        try
        {
            while (!token.IsCancellationRequested && !Manager.ShouldExit && !waitServerList.IsCancellationRequested)
            {
                // Early modal check: detect error dialogs ("Account already logged in", "Invalid username", "Connection lost")
                string? modalText = Flash.Call<string>("getActiveModalMessage");
                if (!string.IsNullOrWhiteSpace(modalText) && modalText != "null")
                {
                    string trimmedModal = modalText.Trim();
                    ReloginLog($"Login modal warning detected: '{trimmedModal}'. Clearing dialog.");
                    try { Flash.Call("killLoginModals"); } catch { }
                    if (trimmedModal.Contains("banned", StringComparison.OrdinalIgnoreCase) ||
                        trimmedModal.Contains("disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        ReloginLog($"FATAL: Account lockout detected ('{trimmedModal}'). Halting auto re-login loop.");
                        IsIntentionalLogout = true;
                        throw new OperationCanceledException($"Account lockout: {trimmedModal}");
                    }
                    return false;
                }

                bool loginVisible = Flash.GetGameObject<bool>("mcLogin.visible", false);
                bool serverListExists = !Flash.IsNull("mcLogin.sl.iList");
                int serverEntries = Flash.GetGameObject<int>("mcLogin.sl.iList.numChildren", 0);
                if (loginVisible && serverListExists && serverEntries > 0 && ServerList.Exists(IsValidReloginServerData))
                {
                    try { Flash.Call("loadPolicyFiles"); } catch { }
                    return true;
                }

                await Task.Delay(250, token);
            }
        }
        catch { }
        token.ThrowIfCancellationRequested();
        bool finalLoginVisible = Flash.GetGameObject<bool>("mcLogin.visible", false);
        bool finalServerListExists = !Flash.IsNull("mcLogin.sl.iList");
        int finalServerEntries = Flash.GetGameObject<int>("mcLogin.sl.iList.numChildren", 0);
        bool success = finalLoginVisible && finalServerListExists && finalServerEntries > 0 && ServerList.Exists(IsValidReloginServerData);
        if (success)
        {
            try { Flash.Call("loadPolicyFiles"); } catch { }
        }
        else
        {
            ReloginLog("Server list screen failed to load (authentication timeout or modal lock). Resetting login UI.");
            try
            {
                Flash.Call("killLoginModals");
                Flash.Call("resetConnectionState");
                Flash.CallGameFunction("gotoAndPlay", "Login");
            }
            catch { }
        }
        return success;
    }

    private async Task<bool> EnsureLogin(Server server, CancellationToken token)
    {
        int tries = 0;
        HashSet<string> attemptedServers = new(StringComparer.OrdinalIgnoreCase);
        string? preferredServerName = server.Name;
        try
        {
            while (!token.IsCancellationRequested && !Manager.ShouldExit && !Player.Playing && tries < Options.ReloginTries)
            {
                tries++;
                await GetServers(true);
                token.ThrowIfCancellationRequested();
                ReloginLog($"Attempt {tries}/{Options.ReloginTries}: refreshed server list ({CachedServers.Count} servers).");

                Server? targetServer = SelectReloginServer(preferredServerName, attemptedServers);
                if (targetServer is null)
                {
                    ReloginLog($"Attempt {tries}/{Options.ReloginTries}: no eligible servers available.");
                    if (tries < Options.ReloginTries)
                        await WaitReloginDelay(token);
                    continue;
                }
                attemptedServers.Add(targetServer.Name ?? string.Empty);

                ReloginLog($"Attempt {tries}/{Options.ReloginTries}: trying {targetServer.Name} [{targetServer.PlayerCount}/{targetServer.MaxPlayers}].");

                // Teardown previous session and enforce ghost session backoff on retries
                if (IsConnected || tries > 1)
                {
                    DisconnectForRelogin();
                    int backoffMs = Options.SafeRelogin
                        ? Math.Max(10000 + (tries - 2) * 3000, Options.ReloginTryDelay)
                        : Math.Max(4000, Options.ReloginTryDelay);
                    ReloginLog($"Waiting {backoffMs / 1000.0:F1}s ghost session backoff before attempt {tries}...");
                    await Task.Delay(backoffMs, token);
                }

                if (_loginInfoSetted)
                    Login(_username, _password);
                else
                    Login();

                ReloginLog($"Attempt {tries}/{Options.ReloginTries}: waiting for server list screen.");
                if (!await WaitForServerListReady(token))
                {
                    ReloginLog($"Attempt {tries}/{Options.ReloginTries}: server list screen not ready.");
                    preferredServerName = null;
                    if (tries < Options.ReloginTries)
                        await WaitReloginDelay(token);
                    continue;
                }

                token.ThrowIfCancellationRequested();

                // Pre-connection security policy & modal cleanup
                int port = targetServer.Port > 0 ? targetServer.Port : 5588;
                try { Flash.Call("loadPolicyFile", targetServer.IP, port); } catch { }
                try { Flash.Call("killLoginModals"); } catch { }

                bool clicked = Flash.Call<bool>("clickServer", targetServer.Name);
                if (!clicked)
                {
                    ReloginLog($"Server {targetServer.Name} button not clickable in UI, initiating direct bridge connection to {targetServer.IP}:{port}...");
                    bool connectedDirect = false;
                    try
                    {
                        Flash.Call("connectToServer", JsonConvert.SerializeObject(targetServer));
                        connectedDirect = true;
                    }
                    catch { }

                    if (!connectedDirect)
                    {
                        if (targetServer.Port > 0)
                            ConnectIP(targetServer.IP, targetServer.Port);
                        else
                            ConnectIP(targetServer.IP);
                    }
                }

                // Hardware/Watchdog timeout (15 seconds max) to prevent infinite world loading hangs
                int watchdogTimeout = Math.Min(15000, Options.LoginTimeout > 0 ? Options.LoginTimeout : 15000);
                using CancellationTokenSource waitLogin = new(watchdogTimeout);
                try
                {
                    while ((!Player.Playing || !Flash.IsWorldLoaded) && !waitLogin.IsCancellationRequested && !token.IsCancellationRequested)
                    {
                        // Abort early if client was disconnected during load
                        string connDetail = Flash.IsNull("mcConnDetail.stage") ? "null" : (Flash.GetGameObject("mcConnDetail.txtDetail.text", "null") ?? "null");
                        if (connDetail.Contains("lost") || connDetail.Contains("banned") || connDetail.Contains("closed"))
                        {
                            ReloginLog($"Connection dropped during world entry: {connDetail}");
                            break;
                        }
                        await Task.Delay(250, token);
                    }
                }
                catch { }

                if (Player.Playing && Flash.IsWorldLoaded)
                {
                    ReloginLog($"Connected successfully to {targetServer.Name}. World loaded.");
                    return true;
                }

                ReloginLog($"Attempt {tries}/{Options.ReloginTries} failed on {targetServer.Name}. Watchdog aborting attempt.");
                preferredServerName = null;
                DisconnectForRelogin();

                if (tries < Options.ReloginTries)
                    await WaitReloginDelay(token);
            }
        }
        catch { }

        if (!Player.Playing)
            ReloginLog("Unable to reconnect within configured retries.");
        return Player.Playing;
    }
}