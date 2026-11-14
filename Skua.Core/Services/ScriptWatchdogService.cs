using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Messaging;
using System.Diagnostics;

namespace Skua.Core.Services;

public class ScriptWatchdogService : IScriptWatchdogService, IDisposable
{
    private readonly Lazy<IScriptManager> _lazyManager;
    private readonly Lazy<IScriptOption> _lazyOptions;
    private readonly Lazy<IScriptPlayer> _lazyPlayer;
    private readonly Lazy<IScriptCombat> _lazyCombat;
    private readonly Lazy<IScriptMap> _lazyMap;
    private readonly Lazy<IScriptServers> _lazyServers;
    private readonly Lazy<IFlashUtil> _lazyFlash;
    private readonly ILogService _logger;
    private readonly IDiscordWebhookService _discord;
    private readonly IDiagnosticsService _diagnostics;

    private IScriptManager Manager => _lazyManager.Value;
    private IScriptOption Options => _lazyOptions.Value;
    private IScriptPlayer Player => _lazyPlayer.Value;
    private IScriptCombat Combat => _lazyCombat.Value;
    private IScriptMap Map => _lazyMap.Value;
    private IScriptServers Servers => _lazyServers.Value;
    private IFlashUtil Flash => _lazyFlash.Value;

    private readonly object _stateLock = new();
    private CancellationTokenSource? _timerCts;
    private Task? _monitorTask;
    private long _lastProgressTimestamp;
    private long _lastPacketTimestamp;
    private int _currentTier;
    private bool _isRunning;
    private bool _disposed;

    public bool IsRunning
    {
        get { lock (_stateLock) return _isRunning; }
        private set { lock (_stateLock) _isRunning = value; }
    }

    public int SecondsSinceLastProgress
    {
        get
        {
            long last = Volatile.Read(ref _lastProgressTimestamp);
            if (last == 0)
                return 0;
            return (int)((Stopwatch.GetTimestamp() - last) / (double)Stopwatch.Frequency);
        }
    }

    public int CurrentTier => Volatile.Read(ref _currentTier);

    public ScriptWatchdogService(
        Lazy<IScriptManager> manager,
        Lazy<IScriptOption> options,
        Lazy<IScriptPlayer> player,
        Lazy<IScriptCombat> combat,
        Lazy<IScriptMap> map,
        Lazy<IScriptServers> servers,
        Lazy<IFlashUtil> flash,
        ILogService logger,
        IDiscordWebhookService discord,
        IDiagnosticsService diagnostics)
    {
        _lazyManager = manager;
        _lazyOptions = options;
        _lazyPlayer = player;
        _lazyCombat = combat;
        _lazyMap = map;
        _lazyServers = servers;
        _lazyFlash = flash;
        _logger = logger;
        _discord = discord;
        _diagnostics = diagnostics;

        RegisterMessages();
    }

    private void RegisterMessages()
    {
        // Script lifecycle
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, ScriptStartedMessage, int>(this, (int)MessageChannels.ScriptStatus, (r, _) => r.OnScriptStarted());
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, ScriptStoppedMessage, int>(this, (int)MessageChannels.ScriptStatus, (r, _) => r.OnScriptStopped());

        // Game progress indicators
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, MonsterKilledMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r.Heartbeat());
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, QuestTurninMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r.Heartbeat());
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, ItemDroppedMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r.Heartbeat());
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, TryBuyItemMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r.Heartbeat());
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, ItemBoughtMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r.Heartbeat());
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, ItemSoldMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r.Heartbeat());
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, ItemAddedToBankMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r.Heartbeat());
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, BankLoadedMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r.Heartbeat());
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, CellChangedMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r.Heartbeat());
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, MapChangedMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r.Heartbeat());

        // Network activity indicators
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, PacketMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r.OnPacketReceived());
        StrongReferenceMessenger.Default.Register<ScriptWatchdogService, ExtensionPacketMessage, int>(this, (int)MessageChannels.GameEvents, (r, _) => r.OnPacketReceived());
    }

    public void Heartbeat()
    {
        Volatile.Write(ref _lastProgressTimestamp, Stopwatch.GetTimestamp());
        Volatile.Write(ref _currentTier, 0);
    }

    private void OnPacketReceived()
    {
        Volatile.Write(ref _lastPacketTimestamp, Stopwatch.GetTimestamp());
    }

    public void Reset()
    {
        Volatile.Write(ref _lastProgressTimestamp, Stopwatch.GetTimestamp());
        Volatile.Write(ref _lastPacketTimestamp, Stopwatch.GetTimestamp());
        Volatile.Write(ref _currentTier, 0);
    }

    private void OnScriptStarted()
    {
        if (!Options.EnableScriptWatchdog)
            return;

        Start();
    }

    private void OnScriptStopped()
    {
        Stop();
    }

    public void Start()
    {
        lock (_stateLock)
        {
            if (_isRunning || _disposed)
                return;

            _isRunning = true;
            _currentTier = 0;
            _lastProgressTimestamp = Stopwatch.GetTimestamp();
            _lastPacketTimestamp = Stopwatch.GetTimestamp();

            _timerCts?.Cancel();
            _timerCts?.Dispose();
            _timerCts = new CancellationTokenSource();

            _monitorTask = Task.Run(() => MonitorLoopAsync(_timerCts.Token));
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _currentTier = 0;

            _timerCts?.Cancel();
            _timerCts?.Dispose();
            _timerCts = null;
        }
    }

    private async Task MonitorLoopAsync(CancellationToken token)
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(5));
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                    break;

                CheckWatchdogState();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[Watchdog] Error in monitor loop: {ex}");
            }
        }
    }

    private void CheckWatchdogState()
    {
        if (!Options.EnableScriptWatchdog || !Manager.ScriptRunning)
            return;

        // Grace conditions: do not trigger stall recovery during pauses or active relogins
        if (Manager.ScriptPaused || Servers.IsReloginInProgress || !Player.LoggedIn || Manager.ShouldExit)
        {
            // Keep bumping progress timestamp during legitimate pauses so unpausing doesn't stall immediately
            Volatile.Write(ref _lastProgressTimestamp, Stopwatch.GetTimestamp());
            return;
        }

        long lastProgress = Volatile.Read(ref _lastProgressTimestamp);
        if (lastProgress == 0)
        {
            Volatile.Write(ref _lastProgressTimestamp, Stopwatch.GetTimestamp());
            return;
        }

        double elapsedSeconds = (Stopwatch.GetTimestamp() - lastProgress) / (double)Stopwatch.Frequency;
        int baseTimeout = Math.Max(30, Options.ScriptWatchdogTimeout);
        int tier2Threshold = baseTimeout + 60;
        int tier3Threshold = baseTimeout + 120;

        // Tier 1: Soft Rescue (at base timeout, e.g. 300s / 5m)
        if (elapsedSeconds >= baseTimeout && _currentTier < 1)
        {
            ExecuteTier1Rescue(elapsedSeconds, baseTimeout);
        }
        // Tier 2: Room Refresh (1 minute after Tier 1, e.g. 360s / 6m)
        else if (elapsedSeconds >= tier2Threshold && _currentTier < 2)
        {
            ExecuteTier2Rescue(elapsedSeconds);
        }
        // Tier 3: Critical Restart (2 minutes after Tier 1, e.g. 420s / 7m)
        else if (elapsedSeconds >= tier3Threshold && _currentTier < 3)
        {
            ExecuteTier3Recovery(elapsedSeconds);
        }
    }

    private void ExecuteTier1Rescue(double elapsedSeconds, int baseTimeout)
    {
        Volatile.Write(ref _currentTier, 1);
        _logger.ScriptLog($"[Watchdog] Stalled for {elapsedSeconds:F0}s (threshold: {baseTimeout}s). Executing Tier 1 rescue (Revive / Target Reset)...");
        _diagnostics.RecordEvent("Watchdog", "Tier1_SoftRescue", elapsedSeconds, "seconds");

        try
        {
            if (!Player.Alive)
            {
                _logger.ScriptLog("[Watchdog] Character is dead; invoking game resurrect and clearing modals.");
                try { Flash.CallGameFunction("world.resurrect"); } catch { }
                try { Flash.Call("killLoginModals"); } catch { }
                try { Map.Jump(Player.Cell, "Spawn"); } catch { }
            }
            else
            {
                try { Combat.CancelTarget(); } catch { }
                try { Flash.Call("world.cancelTarget"); } catch { }
                try { Map.Jump(Player.Cell, Player.Pad); } catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.ScriptLog($"[Watchdog] Error executing Tier 1 rescue: {ex.Message}");
        }
    }

    private void ExecuteTier2Rescue(double elapsedSeconds)
    {
        Volatile.Write(ref _currentTier, 2);
        _logger.ScriptLog($"[Watchdog] Still stalled after {elapsedSeconds:F0}s. Executing Tier 2 rescue (Reloading Map {Map.Name})...");
        _diagnostics.RecordEvent("Watchdog", "Tier2_RoomRefresh", elapsedSeconds, "seconds");

        try
        {
            try { Combat.CancelTarget(); } catch { }
            try { Map.Reload(); } catch { }
        }
        catch (Exception ex)
        {
            _logger.ScriptLog($"[Watchdog] Error executing Tier 2 rescue: {ex.Message}");
        }
    }

    private void ExecuteTier3Recovery(double elapsedSeconds)
    {
        Volatile.Write(ref _currentTier, 3);
        _logger.ScriptLog($"[Watchdog] Critical stall detected ({elapsedSeconds:F0}s). Executing Tier 3 recovery (Script Restart)...");
        _diagnostics.RecordEvent("Watchdog", "Tier3_CriticalRestart", elapsedSeconds, "seconds");

        string currentScript = Path.GetFileName(Manager.LoadedScript);
        string currentMap = Map.Name;
        string currentCell = Player.Cell;

        if (Options.WatchdogDiscordAlerts)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _discord.SendEmbedAsync(
                        "⚠️ Script Stalled - Watchdog Auto-Restart",
                        $"Script `{currentScript}` stalled for {elapsedSeconds:F0}s in `{currentMap}` (Cell: `{currentCell}`). Automatically restarting script.",
                        0xFFA500);
                }
                catch { }
            });
        }

        if (Options.WatchdogAutoRestart)
        {
            // Reset progress timestamp immediately to grant a clean grace period to the restarted script
            Volatile.Write(ref _lastProgressTimestamp, Stopwatch.GetTimestamp());
            _ = Task.Run(async () =>
            {
                try
                {
                    await Manager.RestartScriptAsync();
                }
                catch (Exception ex)
                {
                    _logger.ScriptLog($"[Watchdog] Failed to restart script: {ex.Message}");
                }
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        StrongReferenceMessenger.Default.UnregisterAll(this);
    }
}
