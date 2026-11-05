using Skua.Core.Interfaces;

namespace ButlerV5;

/// <summary>
/// Writes this account's {username}_butlerv5 file. Skua's MapChanged/CellChanged
/// events only fire while a script is running (confirmed by testing - a plugin alone
/// never receives them), so a 500ms watcher polls the game state instead. Disk writes
/// still only happen when the content actually changed; the poll just detects it.
/// </summary>
public class Broadcaster
{
    private readonly IScriptInterface _bot;
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _questClone;
    private readonly Func<bool> _questSync;
    private readonly Action<string> _log;
    private DateTime _lastQuestPublish = DateTime.MinValue;
    private DateTime _lastQuestSyncPublish = DateTime.MinValue;
    private DateTime _lastAttackingSeen = DateTime.MinValue;
    private volatile bool _publishing;
    private readonly object _writeLock = new();

    private CancellationTokenSource? _cts;
    private Task? _watcher;

    private string _lastUsername = "";
    private string _lastWritten = "";
    private string _lastKnownRoom = "1";
    private volatile string _lastServer = "";
    private bool _lastOffGoto;
    private string _lastClassName = "";
    private DateTime _lastClassCheck = DateTime.MinValue;

    /// <summary>This account's current server name (cached from the last broadcast).</summary>
    public string CurrentServer => _lastServer;
    private bool _wasLoggedIn;
    private bool _wasEnabled = true;

    private List<string> _followers = new();
    private string _sendAllTarget = "";
    private string _sendAllCommand = "";
    private List<string> _sendAllRecipients = new();
    private long _sendAllIssuedUtcTicks;
    private bool _sendAllPending;

    private readonly Func<string?> _followingProvider;
    private readonly Func<string?> _scriptNameProvider; // null = no script running
    private readonly Func<string?> _declinedProvider;
    private readonly Func<bool> _parkingProvider;

    public Broadcaster(IScriptInterface bot, Func<bool> enabled, Func<bool> questClone, Func<bool> questSync,
        Func<string?> followingProvider, Func<string?> scriptNameProvider,
        Func<string?> declinedProvider, Func<bool> parkingProvider, Action<string> log)
    {
        _bot = bot;
        _enabled = enabled;
        _questClone = questClone;
        _questSync = questSync;
        _followingProvider = followingProvider;
        _scriptNameProvider = scriptNameProvider;
        _declinedProvider = declinedProvider;
        _parkingProvider = parkingProvider;
        _log = log;
    }

    /// <summary>Usernames this account (as master) has ordered to follow it.</summary>
    public IReadOnlyList<string> Followers
    {
        get { lock (_writeLock) return _followers.ToList(); }
    }

    public void SetFollowers(IEnumerable<string> followers)
    {
        lock (_writeLock)
            _followers = followers.Select(f => f.Trim()).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        WriteNow();
    }

    public bool PublishSendAllGoto(string target, IEnumerable<string> recipients)
    {
        string normalizedTarget = target.Trim();
        List<string> normalizedRecipients = recipients
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (normalizedTarget.Length == 0 || normalizedRecipients.Count == 0)
            return false;

        lock (_writeLock)
        {
            _sendAllTarget = normalizedTarget;
            _sendAllCommand = Guid.NewGuid().ToString("N");
            _sendAllRecipients = normalizedRecipients;
            _sendAllIssuedUtcTicks = 0;
            _sendAllPending = true;
        }

        return WriteNow();
    }

    public void Start()
    {
        DebugLog.Log("Broadcaster", "Start: polling watcher + Logout event");
        _cts = new CancellationTokenSource();
        _bot.Events.Logout += OnLogout;

        CancellationToken token = _cts.Token;
        _watcher = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    bool enabled = _enabled();
                    if (enabled != _wasEnabled)
                    {
                        _wasEnabled = enabled;
                        DebugLog.Log("Broadcaster", $"ButlerV5 enabled -> {enabled}");
                        if (!enabled)
                            WriteOffline(); // master switch off: mark offline, then go idle
                    }

                    // Master switch off: write nothing at all (no sync file, no quest
                    // publish) until it's turned back on.
                    if (!enabled)
                    {
                        token.WaitHandle.WaitOne(250);
                        continue;
                    }

                    bool loggedIn = _bot.Player?.LoggedIn == true;
                    if (loggedIn != _wasLoggedIn)
                    {
                        _wasLoggedIn = loggedIn;
                        DebugLog.Log("Broadcaster", $"login state flipped -> {(loggedIn ? "logged in as " + _bot.Player?.Username : "logged out")}");
                        if (!loggedIn)
                            WriteOffline();
                    }

                    if (loggedIn)
                    {
                        WriteNow(); // no-op unless something actually changed
                        string publishUser = _bot.Player!.Username;

                        // Publish active/registered quest IDs cheaply and frequently.
                        // Followers register these quests locally for their own drops and turn-ins.
                        if (_questSync() &&
                            (DateTime.UtcNow - _lastQuestSyncPublish).TotalSeconds >= 2)
                        {
                            _lastQuestSyncPublish = DateTime.UtcNow;
                            QuestSync.Publish(_bot, publishUser);
                        }

                        // EXPERIMENTAL quest-state publishing: at login, then every 2
                        // minutes. Runs on a background task - the 800 flash-call read
                        // can take 5-20s and must never stall the sync-file writes.
                        if (_questClone() &&
                            !_publishing &&
                            _bot.Map?.Loaded == true &&
                            (DateTime.UtcNow - _lastQuestPublish).TotalSeconds >= 120)
                        {
                            _lastQuestPublish = DateTime.UtcNow;
                            _publishing = true;
                            Task.Run(() =>
                            {
                                try { QuestClone.Publish(_bot, publishUser); }
                                finally { _publishing = false; }
                            });
                        }
                    }
                }
                catch
                {
                }
                token.WaitHandle.WaitOne(250);
            }
        }, token);
    }

    public void Stop()
    {
        _bot.Events.Logout -= OnLogout;
        _cts?.Cancel();
        try { _watcher?.Wait(2000); } catch { }
        _cts?.Dispose();
        _cts = null;
    }

    private void OnLogout()
    {
        DebugLog.Log("Broadcaster", "Logout event");
        WriteOffline();
    }

    public bool WriteNow()
    {
        try
        {
            if (!_enabled())
                return false;

            IScriptPlayer? player = _bot.Player;
            if (player?.LoggedIn != true || string.IsNullOrEmpty(player.Username))
                return false;

            // Mid-transition the map reports unloaded or a bogus name ("-1"); keep the
            // previous file contents until the new map has settled.
            IScriptMap? map = _bot.Map;
            string mapName = map?.Name?.ToLowerInvariant() ?? "";
            if (map?.Loaded != true || string.IsNullOrEmpty(mapName) || mapName == "-1")
                return false;

            // Hysteresis: report attacking immediately (passive followers engage fast),
            // but only report NOT attacking after ~4s of calm. Farming is fight-pause-
            // fight with 1-3s gaps between pulls; a short window flipped attacking (and
            // rewrote the file) on every gap. 4s merges a farming streak into one
            // continuous "attacking", so it only drops on a genuine sustained stop.
            bool rawAttacking = player.InCombat || player.HasTarget;
            if (rawAttacking)
                _lastAttackingSeen = DateTime.UtcNow;
            bool attacking = rawAttacking || (DateTime.UtcNow - _lastAttackingSeen).TotalMilliseconds < 4000;

            string server = GameServer.CurrentName(_bot); // server NAME, not IP (sock7 is shared)
            _lastServer = server;

            // AQW stores the "Enable Goto" social preference here. A nullable read
            // distinguishes a transient Flash read failure from an actual false value.
            bool? gotoEnabled = _bot.Flash.GetGameObject<bool?>("uoPref.bGoto");
            if (gotoEnabled.HasValue)
                _lastOffGoto = !gotoEnabled.Value;

            lock (_writeLock)
            {
                // Start the command's expiry window only when it can actually be
                // published. A map transition must not consume its 60-second lifetime.
                if (_sendAllPending)
                    _sendAllIssuedUtcTicks = DateTime.UtcNow.Ticks;

                if (!player.Username.Equals(_lastUsername, StringComparison.OrdinalIgnoreCase))
                {
                    _lastClassName = "";
                    _lastClassCheck = DateTime.MinValue;
                }
                _lastUsername = player.Username;

                // CurrentClass scans inventory; sample at roster cadence rather than
                // on every 250ms location poll. A transient read failure keeps the
                // last known class until the next sample.
                if ((DateTime.UtcNow - _lastClassCheck).TotalSeconds >= 2)
                {
                    _lastClassCheck = DateTime.UtcNow;
                    try { _lastClassName = player.CurrentClass?.Name ?? _lastClassName; }
                    catch { }
                }

                SyncData data = new()
                {
                    Username = player.Username,
                    Pid = Environment.ProcessId,
                    Server = server,
                    ClassName = _lastClassName,
                    Map = mapName,
                    Room = ResolveRoom(),
                    Cell = player.Cell ?? "Enter",
                    Pad = player.Pad ?? "Spawn",
                    LoggedIn = true,
                    Attacking = attacking,
                    OffGoto = _lastOffGoto,
                    Followers = _followers.ToList(),
                    Following = SafeGet(_followingProvider) ?? "",
                    ScriptOn = SafeGet(_scriptNameProvider) != null,
                    ScriptName = SafeGet(_scriptNameProvider) ?? "",
                    Declined = SafeGet(_declinedProvider) ?? "",
                    Parking = SafeGetBool(_parkingProvider),
                    SendAllTarget = _sendAllTarget,
                    SendAllCommand = _sendAllCommand,
                    SendAllRecipients = _sendAllRecipients.ToList(),
                    SendAllIssuedUtcTicks = _sendAllIssuedUtcTicks,
                };

                bool written = WriteIfChanged(data);
                if (written && _sendAllPending)
                {
                    _sendAllPending = false;
                    DebugLog.Log("Broadcaster", $"published send-all goto to {_sendAllRecipients.Count} accounts: {_sendAllTarget}");
                }

                return written;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Log("Broadcaster", $"sync write exception: {ex.Message}");
            return false;
        }
    }

    /// <summary>Written on logout, in-client shutdown (ProcessExit) and plugin unload.</summary>
    public void WriteOffline()
    {
        try
        {
            lock (_writeLock)
            {
                if (string.IsNullOrEmpty(_lastUsername))
                {
                    DebugLog.Log("Broadcaster", "offline write skipped: never logged in this session");
                    return;
                }

                SyncData data = new()
                {
                    Username = _lastUsername,
                    Pid = Environment.ProcessId,
                    LoggedIn = false,
                    Followers = _followers.ToList(),
                };

                WriteIfChanged(data);
                _lastClassName = "";
                _lastClassCheck = DateTime.MinValue;
            }
        }
        catch
        {
        }
    }

    private static string? SafeGet(Func<string?> provider)
    {
        try { return provider(); }
        catch { return null; }
    }

    private static bool SafeGetBool(Func<bool> provider)
    {
        try { return provider(); }
        catch { return false; }
    }

    private bool WriteIfChanged(SyncData data)
    {
        string serialized = data.Serialize();
        if (serialized == _lastWritten)
            return true;

        if (SyncFile.Write(data))
        {
            _lastWritten = serialized;
            DebugLog.Log("Broadcaster", $"wrote: {DebugLog.Describe(data)}");
            return true;
        }

        DebugLog.Log("Broadcaster", $"WRITE FAILED: {DebugLog.Describe(data)}");
        return false;
    }

    /// <summary>
    /// The joinable room number isn't a direct property; Map.FullName reads the room
    /// title from the game UI (e.g. "ultradage-100000"). Falls back to the last room
    /// we managed to read, so a mid-transition write doesn't corrupt the value.
    /// </summary>
    private string ResolveRoom()
    {
        try
        {
            string fullName = _bot.Map?.FullName ?? "";
            int dash = fullName.LastIndexOf('-');
            if (dash > 0 && dash < fullName.Length - 1)
            {
                string room = fullName[(dash + 1)..];
                if (room.All(char.IsDigit))
                {
                    _lastKnownRoom = room;
                    return room;
                }
            }
        }
        catch
        {
        }
        return _lastKnownRoom;
    }
}
