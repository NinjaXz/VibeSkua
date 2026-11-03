using Skua.Core.Interfaces;
using Skua.Core.Models.Players;
using Skua.Core.Models.Skills;

namespace ButlerV5;

/// <summary>Live option accessors for the follower; values come from the plugin's options.</summary>
public class FollowerSettings
{
    public required Func<bool> PassiveAttack { get; init; }
    public required Func<int> GotoDelayMs { get; init; }
    public required Func<int> RescueThreshold { get; init; }
    public required Func<bool> LeechMode { get; init; }
    public required Func<ParkSpot> Park { get; init; }
    public required Func<bool> InstantWarnings { get; init; }
    public required Func<bool> QuestBypasses { get; init; }
    public required Func<string> CustomBypassIds { get; init; }
    public required Func<bool> LevelFake { get; init; }
    public required Func<bool> QuestClone { get; init; }
    public required Func<bool> QuestSync { get; init; }

    /// <summary>Keep issuing goto even when already in the master's map and cell.</summary>
    public required Func<bool> AlwaysGoto { get; init; }

    /// <summary>Mirror the master's movement while already in the same room and cell.</summary>
    public required Func<bool> CopyWalk { get; init; }

    /// <summary>Nuclear bypass: max every quest slot client-side. Supersedes QuestClone and QuestBypasses.</summary>
    public required Func<bool> UnlockAllQuests { get; init; }
    public required Func<bool> ScriptRunning { get; init; }

    /// <summary>Use Skua's AntiLag (Lag Killer + animation cuts + hidden mobs) while summoned (Ordered).</summary>
    public required Func<bool> AntiLagSummoned { get; init; }

    /// <summary>Use Skua's AntiLag while manually following (Manual).</summary>
    public required Func<bool> AntiLagFollowing { get; init; }

    /// <summary>Called once when a script suspends the follow (shows the warning box).</summary>
    public Action<string>? ScriptSuspendNotice { get; init; }
}

public enum FollowSource
{
    None,

    /// <summary>User clicked Follow in this account's roster window.</summary>
    Manual,

    /// <summary>A master put this account in its followers= list.</summary>
    Ordered,
}

/// <summary>
/// Follows a master account. Core behavior is Butler v3's: rate-limited Goto plus a
/// packet listener for "Locked zone" / "is full" / "ignoring goto" warnings. Where v3
/// walked a hardcoded locked-maps list, v5 reads the master's sync file and joins the
/// exact map-room directly when goto is disabled or cannot reach the master.
/// </summary>
public class Follower
{
    private const int LoopDelayMs = 250;
    private const int JumpMinIntervalMs = 500;
    private const string ParkMap = "whitemap-100000";

    private readonly IScriptInterface _bot;
    private readonly FollowerSettings _settings;
    // Detailed diagnostic text first; anonymous Skua script-log status second.
    private readonly Action<string, string> _log;

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private DateTime _lastGoto = DateTime.MinValue;
    private DateTime _lastSnapshot = DateTime.MinValue;
    private DateTime _lastJumpTime = DateTime.MinValue;
    private DateTime _lastWalkCommandTime = DateTime.MinValue;
    private int _lastWalkTargetX;
    private int _lastWalkTargetY;
    private bool _hasWalkTarget;
    private DateTime _lastJoinTime = DateTime.MinValue;
    private string? _lastJoinTarget;
    private int _gotoAttempts;
    private int _directJoinAttempts;
    private string? _directJoinTarget;
    private bool? _lastReportedOffGoto;
    private bool _packetStreamLogged;
    private string? _skillClass;
    private ClassUseMode _skillMode = ClassUseMode.Base;
    private bool _skillsActive;
    private bool _skillsPaused;  // engine started but casting paused (rotation position kept)
    private string? _lastBypassMap;
    private string? _lastPreApplyMap;
    private DateTime _lastCloneFileTime = DateTime.MinValue;
    private DateTime _lastQuestSyncCheck = DateTime.MinValue;
    private DateTime _lastQuestSyncFileTime = DateTime.MinValue;
    private readonly HashSet<int> _syncedQuestIds = new();
    private string? _lastRescueMap;
    private int _rescueRounds;
    private string? _blockedMap;
    private int _roomFullAttempts;
    private string? _fullCountRoom;  // the room we're currently counting full-warnings for
    private string? _blockedRoom;    // a specific full room-instance we've parked on
    private int _badReads;
    private bool _suspended;
    private bool _wasLoggedIn;
    private bool? _lastWithMaster;
    private volatile bool _parked;

    private volatile bool _lockedZone;
    private volatile bool _roomFull;
    private volatile bool _gotoOff;
    private volatile bool _isParking;
    private bool _antiLagApplied;  // we turned AntiLag on for this follow, so we revert it on stop
    private AntiLagSnapshot? _antiLagPrior;  // the user's AntiLag settings before we forced them on
    private bool _butlerOptsApplied;         // we turned the butler QoL options on for this follow
    private (bool InfiniteRange, bool SkipCutscenes)? _butlerOptsPrior;  // their values before we did
    private bool _autoStopLogged;  // logged the "stopped Skua Auto" notice once per on->off transition
    private string? _ownHouseUsername;  // cache belongs to this logged-in account only
    private string? _ownHouseRoom;      // house-<CharID>, not ScriptPlayer.ID (session uid)

    /// <summary>True while mid-park (walking home) - the master shouldn't re-summon yet.</summary>
    public bool IsParking => _isParking;

    /// <summary>
    /// True while parked and waiting (master offline or on a different server). The order
    /// is still held - we're just idle at home, not actively following - so the master's
    /// roster should show a "summoned" chip, not "following me".
    /// </summary>
    public bool IsParked => _parked;

    public string? Master { get; private set; }
    public FollowSource Source { get; private set; } = FollowSource.None;
    public bool IsRunning => _loop is { IsCompleted: false };

    public Follower(IScriptInterface bot, FollowerSettings settings, Action<string, string> log)
    {
        _bot = bot;
        _settings = settings;
        _log = log;
    }

    public void Start(string master, FollowSource source)
    {
        Stop();

        Master = master;
        Source = source;
        _lastReportedOffGoto = null;
        _lastQuestSyncCheck = DateTime.MinValue;
        _lastQuestSyncFileTime = DateTime.MinValue;
        _lastWalkCommandTime = DateTime.MinValue;
        _lastWalkTargetX = 0;
        _lastWalkTargetY = 0;
        _hasWalkTarget = false;
        _directJoinTarget = null;
        _directJoinAttempts = 0;
        _suspended = false;
        _parked = false;
        _lastWithMaster = null;
        _lastPreApplyMap = null;
        _lastRescueMap = null;
        _rescueRounds = 0;
        _blockedMap = null;
        _roomFullAttempts = 0;
        _fullCountRoom = null;
        _blockedRoom = null;
        _lockedZone = _roomFull = _gotoOff = false;
        DebugLog.Log("Follower", $"Start following {master} ({source})");

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;

        _bot.Events.ExtensionPacketReceived += PacketListener;
        try { _bot.Flash.FlashCall += OnFlashCall; } catch { }
        _packetStreamLogged = false;
        DebugLog.Log("Follower",
            $"options in THIS client: passiveAttack={_settings.PassiveAttack()} leech={_settings.LeechMode()} " +
            $"park={_settings.Park()} gotoDelay={_settings.GotoDelayMs()}ms alwaysGoto={_settings.AlwaysGoto()} copyWalk={_settings.CopyWalk()} " +
            $"questSync={_settings.QuestSync()} rescue={_settings.RescueThreshold()} " +
            $"instantWarnings={_settings.InstantWarnings()} questBypass={_settings.QuestBypasses()} questClone={_settings.QuestClone()}");
        _log($"Following {master} ({source}).", $"Following the master ({source}).");

        _loop = Task.Run(() => Run(master, token), token);
    }

    public void Stop(bool park = false)
    {
        if (_cts == null)
        {
            ClearSyncedQuestRegistrations();
            Source = FollowSource.None;
            return;
        }

        _bot.Events.ExtensionPacketReceived -= PacketListener;
        try { _bot.Flash.FlashCall -= OnFlashCall; } catch { }
        _cts.Cancel();
        try { _loop?.Wait(3000); } catch { }
        _cts.Dispose();
        _cts = null;
        _loop = null;
        ClearSyncedQuestRegistrations();

        StopSkills();
        StopAttacking(deaggroJump: true);
        RevertAntiLag();
        RevertButlerOptions();
        // A parked butler may still be in someone else's house after an unreachable
        // direct join. On release, only skip the house transfer if it is truly home.
        if (park && (!_parked ||
            (_settings.Park() == ParkSpot.House && !IsInOwnHouse(GetMyMapRoom()))))
            Park("Released by master.", "Released by master.");
        else if (park)
            DebugLog.Log("Follower", "released while already parked - staying put");

        _log(Master != null ? $"Stopped following {Master}." : "Follow stopped.",
            Master != null ? "Stopped following the master." : "Follow stopped.");
        Master = null;
        Source = FollowSource.None;
    }

    private void Run(string master, CancellationToken token)
    {
        try
        {
            Arm(master, freshLogin: false);
            _wasLoggedIn = _bot.Player?.LoggedIn == true;

            while (!token.IsCancellationRequested)
            {
                if (_bot.Player?.LoggedIn != true || !_bot.Player.Alive)
                {
                    // Track logout (not mere death) so we can re-arm on relogin.
                    if (_bot.Player?.LoggedIn != true)
                        _wasLoggedIn = false;
                    Wait(token, 1000);
                    continue;
                }

                // Auto-relogin re-arm: the skill engine, equipped class and quest
                // state all die on disconnect. LoggedIn/Alive flip true a beat before
                // the class/inventory finish loading, so re-arming instantly leaves the
                // skill engine running with no real rotation (only skill 0). Wait for
                // the character to actually settle, then re-arm with a class re-equip.
                if (!_wasLoggedIn)
                {
                    _wasLoggedIn = true;
                    DebugLog.Log("Follower", "re-login detected - waiting for character to load");
                    WaitForCharacterReady(token);
                    Arm(master, freshLogin: true);
                }

                // Suspend & resume: a script started on THIS client takes over the
                // character; pause everything and resume when the script stops.
                if (_settings.ScriptRunning())
                {
                    if (!_suspended)
                    {
                        _suspended = true;
                        ClearSyncedQuestRegistrations();
                        StopSkills();
                        StopAttacking(deaggroJump: true);
                        _log($"A script is running - following {master} is suspended until it stops.",
                            "A script is running - following the master is suspended until it stops.");
                        DebugLog.Log("Follower", "suspended (script running in this client)");
                        try { _settings.ScriptSuspendNotice?.Invoke(master); } catch { }
                    }
                    Wait(token, 1000);
                    continue;
                }
                if (_suspended)
                {
                    _suspended = false;
                    _gotoAttempts = 0;
                    _log($"Script stopped - resuming follow of {master}.",
                        "Script stopped - resuming follow of the master.");
                    DebugLog.Log("Follower", "resuming after script");
                }

                // Keep Skua's Auto Attack/Hunt off for the whole follow (best-effort mirror
                // of Skua's "no Auto while a script runs"). Cheap bool read per tick.
                StopUserAuto();
                if (!_settings.QuestSync())
                    ClearSyncedQuestRegistrations();

                // Parking is an enforced no-combat state. Keep both Skua aggro modes off
                // while waiting too, so another component cannot re-enable them after the
                // initial deaggro and strand this account in combat again.
                if (_parked)
                    ForceParkingAggroOff();

                SyncData? m = SyncFile.ReadUser(master);

                if ((DateTime.UtcNow - _lastSnapshot).TotalSeconds >= 10)
                {
                    _lastSnapshot = DateTime.UtcNow;

                    // Re-clone when the master's quest file changed (they completed
                    // something new mid-session). Skipped when "unlock all" is on - our
                    // slots are already maxed, so there's nothing to pick up.
                    if (!_settings.UnlockAllQuests() && _settings.QuestClone())
                    {
                        DateTime fileTime = QuestClone.GetFileTime(master);
                        if (fileTime > _lastCloneFileTime)
                        {
                            _lastCloneFileTime = fileTime;
                            DebugLog.Log("QuestClone", $"{master}'s quest file changed - re-cloning");
                            QuestClone.ApplyFromMaster(_bot, master);
                        }
                    }

                    DebugLog.Log("Follower",
                        $"tick: me map={_bot.Map?.Name} cell={_bot.Player?.Cell} pad={_bot.Player?.Pad} " +
                        $"server={GameServer.CurrentName(_bot)} parked={_parked} " +
                        $"flags locked={_lockedZone} full={_roomFull} gotoOff={_gotoOff} | master {DebugLog.Describe(m)}" +
                        (m != null ? $" pidAlive={SyncFile.IsProcessAlive(m.Pid)}" : ""));
                }

                // Debounced: a single failed read (writer caught mid-rewrite) must not
                // park the butler - require 3 consecutive bad reads (~3s).
                if (m == null || !SyncFile.IsOnline(m))
                {
                    _badReads++;
                    if (_badReads >= 3)
                    {
                        ClearSyncedQuestRegistrations();
                        ParkOnce(m == null
                            ? $"No ButlerV5 file for {master} at {SyncFile.PathFor(master)} - is their plugin installed?"
                            : $"{master} looks offline (loggedin={(m.LoggedIn ? 1 : 0)}, pid={m.Pid}, " +
                              $"pidAlive={SyncFile.IsProcessAlive(m.Pid)}). Parked, waiting for them to return.",
                            m == null
                                ? "No ButlerV5 file for the master - is their plugin installed? Parked."
                                : "The master looks offline. Parked, waiting for them to return.");
                    }
                    Wait(token, 1000);
                    continue;
                }
                _badReads = 0;

                // A server rejection may arrive before the broadcaster updates its
                // file. Keep using direct joins until the master explicitly publishes
                // that goto has been enabled again.
                if (_lastReportedOffGoto == true && !m.OffGoto)
                {
                    _gotoOff = false;
                    _gotoAttempts = 0;
                    _directJoinAttempts = 0;
                    _directJoinTarget = null;
                    _blockedMap = null;
                    _blockedRoom = null;
                    DebugLog.Log("Follower", $"{master} enabled goto again");
                }
                _lastReportedOffGoto = m.OffGoto;

                if (!SameServer(m))
                {
                    ClearSyncedQuestRegistrations();
                    ParkOnce($"{master} is on a different server (theirs='{m.Server}' mine='{GameServer.CurrentName(_bot)}'). Parked.",
                        "The master is on a different server. Parked.");
                    Wait(token, 2000);
                    continue;
                }

                if (_settings.QuestSync())
                    SyncRegisteredQuests(master);

                // Circuit breaker: a map we repeatedly failed to enter (entry gate kept
                // bouncing us to battleon) is blocked until the master moves elsewhere.
                if (_blockedMap != null)
                {
                    if (_blockedMap.Equals(m.Map, StringComparison.OrdinalIgnoreCase))
                    {
                        Wait(token, 2000);
                        continue;
                    }
                    DebugLog.Log("Follower", $"master left blocked map {_blockedMap} - trying again");
                    _blockedMap = null;
                }

                // A specific full room we gave up on. Unlike a locked map, ANY room change
                // (even same map) frees us - the new room may have space.
                if (_blockedRoom != null)
                {
                    if (_blockedRoom.Equals(m.MapWithRoom, StringComparison.OrdinalIgnoreCase))
                    {
                        Wait(token, 2000);
                        continue;
                    }
                    DebugLog.Log("Follower", $"master left full room {_blockedRoom} - trying again");
                    _blockedRoom = null;
                }

                if (_parked)
                {
                    _parked = false;
                    _log($"{master} is back - resuming follow.", "The master is back - resuming follow.");
                    // Don't start skills here - we still have to travel to the master.
                    // HandleCombat starts them on arrival (avoids the start-stop-start
                    // that breaks Skua's skill engine).
                }

                if (_lockedZone || _roomFull)
                {
                    bool full = _roomFull;
                    _lockedZone = _roomFull = false;

                    // A full PRIVATE room (>=1000) used to be re-joined every tick forever:
                    // unlike public rooms it never overflows, so the join is rejected outright
                    // each time. Count the full-warnings for this specific room; after 3, stop
                    // hammering and park until the master changes rooms (which may have space).
                    if (full)
                    {
                        if (string.Equals(m.MapWithRoom, _fullCountRoom, StringComparison.OrdinalIgnoreCase))
                            _roomFullAttempts++;
                        else
                        {
                            _fullCountRoom = m.MapWithRoom;
                            _roomFullAttempts = 1;
                        }

                        if (_roomFullAttempts >= 3)
                        {
                            _blockedRoom = m.MapWithRoom;
                            _roomFullAttempts = 0;
                            _fullCountRoom = null;
                            ParkOnce($"{m.MapWithRoom} is full after 3 attempts - parked until {master} changes rooms.",
                                "The master's room is full after 3 attempts - parked until the master changes rooms.");
                            Wait(token, 2000);
                            continue;
                        }
                    }

                    _log(full
                        ? $"Room full - joining {m.MapWithRoom} directly (attempt {_roomFullAttempts})."
                        : $"Locked zone - joining {m.MapWithRoom} from {master}'s file.",
                        full
                            ? $"Room full - joining the master's room directly (attempt {_roomFullAttempts})."
                            : "Locked zone - joining the master's room from their file.");
                    JoinMasterRoom(m, token);
                    Wait(token, LoopDelayMs);
                    continue;
                }

                // "Am I in the master's room?" - compare our own room title with the
                // master's map-room from their file. Both come from the same game UI
                // source, so this is an exact match. PlayerExists is NOT used for this:
                // testing showed it flapping false while standing next to the master.
                string myRoom = GetMyMapRoom();
                if (string.IsNullOrEmpty(myRoom))
                {
                    // Own map still loading/transitioning - try again shortly.
                    Wait(token, LoopDelayMs);
                    continue;
                }

                // Re-apply quest access whenever we land on a new map (beats entry gates).
                // "Unlock all" re-maxes; otherwise the per-map bypass runs.
                string myMapName = _bot.Map?.Name ?? "";
                if ((_settings.UnlockAllQuests() || _settings.QuestBypasses()) &&
                    !string.Equals(myMapName, _lastBypassMap, StringComparison.OrdinalIgnoreCase))
                {
                    _lastBypassMap = myMapName;
                    if (_settings.UnlockAllQuests())
                        QuestClone.UnlockAll(_bot);
                    else
                        QuestBypass.ApplyForMap(_bot, myMapName);
                }

                bool sameRoom = string.Equals(myRoom, m.MapWithRoom, StringComparison.OrdinalIgnoreCase);
                if (sameRoom != _lastWithMaster)
                {
                    _lastWithMaster = sameRoom;
                    DebugLog.Log("Follower", $"room check: mine={myRoom} master={m.MapWithRoom} -> sameRoom={sameRoom}");
                }

                if (sameRoom)
                {
                    _directJoinAttempts = 0;
                    _directJoinTarget = null;
                    _rescueRounds = 0;
                    _lastRescueMap = null;
                    _roomFullAttempts = 0;
                    _fullCountRoom = null;
                }

                if (!sameRoom)
                {
                    // Pause the skill engine while traveling: it re-acquires targets on
                    // its own, dragging the butler back into combat and delaying the
                    // goto for many seconds after the master leaves the map.
                    if (_skillsActive && !_skillsPaused)
                    {
                        DebugLog.Log("Follower", "pausing skill engine to travel");
                        PauseSkills();
                    }
                    StopAttacking(deaggroJump: true);

                    // Entry gates are checked the moment you enter a map - faking after
                    // landing is too late (doomvaultb black-screens and force-joins
                    // battleon). Pre-apply the target map's fakes BEFORE moving, using
                    // the goto-delay wait that exists anyway.
                    // "Unlock all" keeps every slot maxed persistently, so the target-map
                    // gate is already beaten on arrival - no target-specific pre-apply needed.
                    if (!_settings.UnlockAllQuests() && _settings.QuestBypasses() &&
                        !string.Equals(m.Map, _lastPreApplyMap, StringComparison.OrdinalIgnoreCase))
                    {
                        _lastPreApplyMap = m.Map;
                        // Also counts as the on-arrival application: without this the
                        // arrival backstop re-fakes the same map and the quest window
                        // pops up twice (before and after the join).
                        _lastBypassMap = m.Map;
                        DebugLog.Log("Follower", $"pre-applying quest fakes for target map {m.Map}");
                        QuestBypass.ApplyForMap(_bot, m.Map);
                    }

                    if (m.OffGoto || _gotoOff)
                    {
                        // With goto disabled, use the master's exact map-room from the
                        // sync file. Count actual join attempts, not throttled ticks,
                        // so an inaccessible room does not get hammered forever.
                        _gotoAttempts = 0;
                        if (!string.Equals(_directJoinTarget, m.MapWithRoom, StringComparison.OrdinalIgnoreCase))
                        {
                            _directJoinTarget = m.MapWithRoom;
                            _directJoinAttempts = 0;
                            DebugLog.Log("Follower", $"{master} has goto off - directly joining {m.MapWithRoom}");
                        }

                        if (_directJoinAttempts >= 3)
                        {
                            bool onMasterMap = string.Equals(_bot.Map?.Name ?? "", m.Map, StringComparison.OrdinalIgnoreCase);
                            if (onMasterMap)
                            {
                                _blockedRoom = m.MapWithRoom;
                                ParkOnce(m.Map.Equals("house", StringComparison.OrdinalIgnoreCase)
                                    ? $"Can't reach {master} in {m.MapWithRoom} through their own house - they may be visiting someone else's house. Parked until they change rooms."
                                    : $"Can't enter {m.MapWithRoom} after 3 direct joins - parked until {master} changes rooms.",
                                    m.Map.Equals("house", StringComparison.OrdinalIgnoreCase)
                                        ? "Can't reach the master through their own house - they may be visiting someone else's house. Parked until they change rooms."
                                        : "Can't enter the master's room after 3 direct joins - parked until the master changes rooms.");
                            }
                            else
                            {
                                _blockedMap = m.Map;
                                ParkOnce($"Can't enter {m.Map} after 3 direct joins - parked until {master} changes maps.",
                                    "Can't enter the master's map after 3 direct joins - parked until the master changes maps.");
                            }
                            Wait(token, 2000);
                            continue;
                        }

                        if (JoinMasterRoom(m, token))
                            _directJoinAttempts++;
                    }
                    else
                    {
                        // Goto reaches player houses and remains the fastest path when
                        // allowed. After failed attempts, rescue with a direct join.
                        int rescueThreshold = Math.Clamp(_settings.RescueThreshold(), 1, 10);
                        if (_gotoAttempts >= rescueThreshold)
                        {
                            _gotoAttempts = 0;

                            if (string.Equals(m.Map, _lastRescueMap, StringComparison.OrdinalIgnoreCase))
                                _rescueRounds++;
                            else
                            {
                                _lastRescueMap = m.Map;
                                _rescueRounds = 1;
                            }

                            if (_rescueRounds >= 3)
                            {
                                // Stop hammering, but tell apart a FULL ROOM from a LOCKED MAP.
                                // /goto always blocks when the master's room is full; whether we
                                // can still reach the MAP tells the two apart. A full PUBLIC room
                                // (<1000) overflows the file-join onto the map in a different free
                                // room - so we're ON the master's map but the wrong room. That's a
                                // full room, and a ROOM change may free us, so block only this
                                // room. (A full PRIVATE room >=1000 is rejected outright and is
                                // handled by the _roomFull path above.) If we couldn't even get
                                // onto the map - an entry gate bounced us to battleon - the map is
                                // locked (a quest fake is missing); block the whole map until the
                                // master leaves it.
                                bool onMasterMap = string.Equals(_bot.Map?.Name ?? "", m.Map, StringComparison.OrdinalIgnoreCase);
                                _rescueRounds = 0;
                                if (onMasterMap)
                                {
                                    _blockedRoom = m.MapWithRoom;
                                    ParkOnce($"{m.MapWithRoom} is full after 3 rescue attempts - parked until {master} changes rooms.",
                                        "The master's room is full after 3 rescue attempts - parked until the master changes rooms.");
                                }
                                else
                                {
                                    _blockedMap = m.Map;
                                    ParkOnce($"Can't enter {m.Map} after 3 rescue attempts - a quest gate may be missing. Parked until {master} changes maps.",
                                        "Can't enter the master's map after 3 rescue attempts - a quest gate may be missing. Parked until the master changes maps.");
                                }
                                Wait(token, 2000);
                                continue;
                            }

                            DebugLog.Log("Follower", $"goto not landing after {rescueThreshold} attempts - file-join rescue (round {_rescueRounds})");
                            JoinMasterRoom(m, token);
                        }
                        else if (TryGoto(master))
                        {
                            _gotoAttempts++;
                        }
                    }
                }
                else if (_settings.LeechMode())
                {
                    _gotoAttempts = 0;

                    // Leech mode: stay in the master's room but wait at Enter and
                    // never fight. Good for dragging a weak alt along safely.
                    if (_skillsActive)
                        StopSkills();
                    StopAttacking(deaggroJump: true);
                    if (!string.Equals(_bot.Player!.Cell, "Enter", StringComparison.OrdinalIgnoreCase) &&
                        (DateTime.UtcNow - _lastJumpTime).TotalMilliseconds >= JumpMinIntervalMs)
                    {
                        DebugLog.Log("Follower", $"leech: jump {_bot.Player.Cell} -> Enter/Spawn");
                        _bot.Map!.Jump("Enter", "Spawn", autoCorrect: false);
                        _lastJumpTime = DateTime.UtcNow;
                    }
                }
                else
                {
                    _gotoAttempts = 0;

                    // Skill engine resume is handled by HandleCombat: it only runs
                    // while the butler should actually be fighting.

                    // Belt-and-braces: the cell jump below relies on client-side reads
                    // (own cell, the room title, the master's in-map position) and any of
                    // those going stale - notably right after a death/respawn - leaves the
                    // butler parked in the wrong cell with no jump ever firing. goto is
                    // resolved server-side and always lands on the master, so this option
                    // keeps issuing it even when we believe we're already in place.
                    // Throttled by the same Goto delay as travel; leech mode is a separate
                    // branch above, so it is unaffected.
                    if (_settings.AlwaysGoto() && !_settings.CopyWalk() && !m.OffGoto && !_gotoOff)
                        TryGoto(master);

                    // In the master's room: only the CELL matters. Pads are ignored on
                    // purpose - walking within a cell changes a player's pad, and
                    // chasing those changes made the follower stutter-jump on every
                    // move command. The master's pad is only used as the arrival pad.
                    string targetCell = m.Cell;
                    string targetPad = m.Pad;
                    PlayerInfo? masterInfo = null;
                    if (_bot.Map!.TryGetPlayer(master, out PlayerInfo? info) &&
                        !string.IsNullOrEmpty(info?.Cell))
                    {
                        masterInfo = info;
                        targetCell = info.Cell;
                        targetPad = string.IsNullOrEmpty(info.Pad) ? m.Pad : info.Pad;
                    }

                    if (!string.Equals(_bot.Player!.Cell, targetCell, StringComparison.OrdinalIgnoreCase) &&
                        (DateTime.UtcNow - _lastJumpTime).TotalMilliseconds >= JumpMinIntervalMs)
                    {
                        DebugLog.Log("Follower", $"jump {_bot.Player.Cell}/{_bot.Player.Pad} -> {targetCell}/{targetPad}");
                        // autoCorrect: false on every jump. It asks the client to validate
                        // and "correct" the target against the map's cell list, and a recent
                        // AQW update broke that path - clients that haven't patched it (e.g.
                        // VibeSkua) silently fail to jump at all. We never need the safety
                        // net anyway: every target here is a real cell (the master's own
                        // position, our current cell, or Enter). Butler v3 does the same.
                        _bot.Map.Jump(targetCell, targetPad, autoCorrect: false);
                        _lastJumpTime = DateTime.UtcNow;
                    }

                    if (_settings.CopyWalk() && masterInfo is not null &&
                        _bot.Player!.Cell.Equals(targetCell, StringComparison.OrdinalIgnoreCase))
                    {
                        CopyMasterWalk(masterInfo);
                    }
                    else if (!_settings.CopyWalk())
                    {
                        _hasWalkTarget = false;
                    }

                    HandleCombat(m);
                }

                Wait(token, LoopDelayMs);
            }
        }
        catch (Exception ex)
        {
            _log($"Follow loop error: {ex.Message}",
                "Follow loop error. See ButlerV5's debug log for details.");
        }
    }

    /// <summary>
    /// Prepares the character for following: equip class + start skills, apply custom
    /// quest bypasses / level fake, and clone the master's quest state. Runs at follow
    /// start and again on every relogin (all of this dies on a disconnect). On a fresh
    /// login the class is re-equipped to force the client to reload its skill data.
    /// </summary>
    private void Arm(string master, bool freshLogin)
    {
        GameServer.LogCandidates(_bot); // diagnostic: which source has the server name
        StopUserAuto(); // butler owns combat/skills - kill any user Auto Attack/Hunt first

        // A reconnect does not guarantee that Skua's skill worker exited. It may still
        // be running or paused from the previous session, so shut it down through the
        // pause-safe path before preparing the new rotation. Skills are still started
        // lazily by HandleCombat after we reach the master.
        StopSkills();

        SetupClassAndSkills(forceEquipCurrent: freshLogin);

        // Level fake is independent of quest access.
        if (_settings.LevelFake())
            QuestBypass.ApplyLevelFake(_bot);
        _lastBypassMap = null;
        _lastPreApplyMap = null;

        // Quest access precedence: "unlock all" is the nuclear option and supersedes both
        // the clone and the per-map bypass (it maxes every slot, so those would just be
        // overwritten). Only one path runs.
        if (_settings.UnlockAllQuests())
        {
            QuestClone.UnlockAll(_bot);
        }
        else
        {
            if (_settings.QuestBypasses())
                QuestBypass.ApplyCustom(_bot, QuestBypass.ParseCustomIds(_settings.CustomBypassIds()));
            if (_settings.QuestClone())
            {
                QuestClone.ApplyFromMaster(_bot, master);
                _lastCloneFileTime = QuestClone.GetFileTime(master);
            }
        }

        ApplyAntiLag();
        ApplyButlerOptions();
    }

    /// <summary>
    /// Turns Skua's AntiLag on if the option for this follow's source is enabled. Called
    /// from Arm(), so it re-applies after a relogin too (the Flash-side toggles reset on a
    /// game reload). Only touches AntiLag when the matching option is on - it never fights
    /// a user who has enabled Lag Killer by hand for a source we aren't asked to manage.
    /// </summary>
    /// <summary>The nine AntiLag settings, snapshotted before we force them on.</summary>
    private readonly record struct AntiLagSnapshot(
        bool LagKiller, bool FreezeMonsterPosition, bool DisableMonsterAnimation,
        bool DisableDamageStrobe, bool DisableSelfAnimation, bool DisableWeaponAnimation,
        bool DisableSkillAnimation, bool DisableAuraAnimations, bool MonstersHidden);

    private void ApplyAntiLag()
    {
        bool want = (Source == FollowSource.Ordered && _settings.AntiLagSummoned())
                 || (Source == FollowSource.Manual && _settings.AntiLagFollowing());
        if (!want)
            return;

        if (!_antiLagApplied)
        {
            // Snapshot the user's settings BEFORE forcing AntiLag on, so releasing can
            // restore exactly what they had - never turning off something they'd enabled
            // by hand. Captured once; a relogin re-apply must not overwrite it.
            _antiLagPrior = CaptureAntiLagState();
            _antiLagApplied = true;
            DebugLog.Log("Follower", $"AntiLag ON (source={Source})");
        }
        SetAntiLag(true);
    }

    /// <summary>
    /// Turns on the two client options a butler always wants while following: Infinite Range
    /// (hit the master's target without walking into range) and Skip Cutscenes (never sit
    /// through a cutscene the master triggered). Applies for BOTH follow sources. The user's
    /// prior values are snapshotted so Stop() restores them instead of forcing both off.
    /// </summary>
    private void ApplyButlerOptions()
    {
        try
        {
            if (!_butlerOptsApplied)
            {
                _butlerOptsPrior = (_bot.Options.InfiniteRange, _bot.Options.SkipCutscenes);
                _butlerOptsApplied = true;
                DebugLog.Log("Follower",
                    $"butler options ON (was infiniteRange={_butlerOptsPrior.Value.InfiniteRange} " +
                    $"skipCutscenes={_butlerOptsPrior.Value.SkipCutscenes})");
            }
            SetInfiniteRange(true);
            SetSkipCutscenes(true);
        }
        catch (Exception ex)
        {
            DebugLog.Log("Follower", $"butler options apply failed: {ex.Message}");
        }
    }

    private void RevertButlerOptions()
    {
        if (!_butlerOptsApplied)
            return;
        _butlerOptsApplied = false;
        try
        {
            if (_butlerOptsPrior is { } prior)
            {
                SetInfiniteRange(prior.InfiniteRange);
                SetSkipCutscenes(prior.SkipCutscenes);
                DebugLog.Log("Follower", "butler options OFF (restored pre-follow settings)");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Log("Follower", $"butler options restore failed: {ex.Message}");
        }
        _butlerOptsPrior = null;
    }

    // Both of these Skua properties are TOGGLES on the Flash side: the setter calls
    // Flash.Call("infiniteRange") / Flash.Call("skipCutscenes") with NO value, so assigning
    // the same value twice flips the game state back while the C# property still reports the
    // value we assigned. Only ever assign when the tracked value actually differs.
    private void SetInfiniteRange(bool on)
    {
        if (_bot.Options.InfiniteRange != on)
            _bot.Options.InfiniteRange = on;
    }

    private void SetSkipCutscenes(bool on)
    {
        if (_bot.Options.SkipCutscenes != on)
            _bot.Options.SkipCutscenes = on;
    }

    private void RevertAntiLag()
    {
        if (!_antiLagApplied)
            return;
        _antiLagApplied = false;
        RestoreAntiLag(_antiLagPrior);
        _antiLagPrior = null;
        DebugLog.Log("Follower", "AntiLag OFF (restored pre-follow settings)");
    }

    private AntiLagSnapshot? CaptureAntiLagState()
    {
        try
        {
            return new AntiLagSnapshot(
                _bot.Options.LagKiller,
                _bot.Lite.FreezeMonsterPosition,
                _bot.Lite.DisableMonsterAnimation,
                _bot.Lite.DisableDamageStrobe,
                _bot.Lite.DisableSelfAnimation,
                _bot.Lite.DisableWeaponAnimation,
                _bot.Lite.DisableSkillAnimation,
                _bot.Lite.DisableAuraAnimations,
                _bot.Flash.GetGameObject<bool>("ui.monsterIcon.redX.visible"));
        }
        catch (Exception ex)
        {
            DebugLog.Log("Follower", $"AntiLag capture failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Puts each setting back to its snapshotted value. Only what we turned on
    /// (was false, now true) gets turned off; anything the user already had on stays on.</summary>
    private void RestoreAntiLag(AntiLagSnapshot? snapshot)
    {
        // No snapshot (capture failed) - fall back to the old behavior of turning all off.
        if (snapshot is not { } s)
        {
            SetAntiLag(false);
            return;
        }

        try
        {
            _bot.Options.LagKiller = s.LagKiller;
            _bot.Lite.FreezeMonsterPosition = s.FreezeMonsterPosition;
            _bot.Lite.DisableMonsterAnimation = s.DisableMonsterAnimation;
            _bot.Lite.DisableDamageStrobe = s.DisableDamageStrobe;
            _bot.Lite.DisableSelfAnimation = s.DisableSelfAnimation;
            _bot.Lite.DisableWeaponAnimation = s.DisableWeaponAnimation;
            _bot.Lite.DisableSkillAnimation = s.DisableSkillAnimation;
            _bot.Lite.DisableAuraAnimations = s.DisableAuraAnimations;

            // Restore monster visibility to what it was (toggle only if it differs now).
            bool hidden = _bot.Flash.GetGameObject<bool>("ui.monsterIcon.redX.visible");
            if (hidden != s.MonstersHidden)
                _bot.Flash.CallGameFunction("world.toggleMonsters");
        }
        catch (Exception ex)
        {
            DebugLog.Log("Follower", $"AntiLag restore failed: {ex.Message}");
        }
    }

    /// <summary>The exact bundle CoreBots' AntiLag uses: Lag Killer, the Lite animation
    /// cuts, and hidden monster visuals.</summary>
    private void SetAntiLag(bool on)
    {
        try
        {
            _bot.Options.LagKiller = on;
            _bot.Lite.FreezeMonsterPosition = on;
            _bot.Lite.DisableMonsterAnimation = on;
            _bot.Lite.DisableDamageStrobe = on;
            _bot.Lite.DisableSelfAnimation = on;
            _bot.Lite.DisableWeaponAnimation = on;
            _bot.Lite.DisableSkillAnimation = on;
            _bot.Lite.DisableAuraAnimations = on;

            // Hide/show monster visuals. world.toggleMonsters is a TOGGLE, so only flip it
            // when the current state doesn't match; redX.visible is true when hidden.
            bool hidden = _bot.Flash.GetGameObject<bool>("ui.monsterIcon.redX.visible");
            if (hidden != on)
                _bot.Flash.CallGameFunction("world.toggleMonsters");
        }
        catch (Exception ex)
        {
            DebugLog.Log("Follower", $"AntiLag set {on} failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Equips the CoreBots Farm/Solo class configured for this account (if any) and
    /// starts Skua's skill engine for it - the same StartAdvanced call CoreBots makes,
    /// which is where Butler v3's skill usage actually came from. Without this the
    /// butler only ever auto-attacks (skill 0).
    /// </summary>
    private void SetupClassAndSkills(bool forceEquipCurrent)
    {
        try
        {
            string? me = _bot.Player?.Username;
            if (string.IsNullOrEmpty(me))
                return;

            _skillClass = null;
            _skillMode = ClassUseMode.Base;

            ButlerClassType type = AccountConfig.GetClassType(me);
            string? classToEquip = null;

            if (type != ButlerClassType.None)
            {
                classToEquip = AccountConfig.GetCboClassName(me, type);
                if (string.IsNullOrEmpty(classToEquip))
                    _log($"No CoreBots {type} class configured for {me} - keeping current class.",
                        $"No CoreBots {type} class configured for this account - keeping current class.");
                else if (Enum.TryParse(AccountConfig.GetCboClassMode(me, type), ignoreCase: true, out ClassUseMode mode))
                    _skillMode = mode;
            }
            else if (forceEquipCurrent)
            {
                // No configured class: after a relogin, re-equip whatever's currently
                // worn to force the client to reload the class's skill data (without
                // this the skill engine starts with no rotation - only skill 0 fires).
                classToEquip = _bot.Player?.CurrentClass?.Name;
            }

            if (!string.IsNullOrEmpty(classToEquip))
            {
                _log($"Equipping class: {classToEquip}", $"Equipping class: {classToEquip}");
                _bot.Inventory.EquipItem(classToEquip);

                // Poll for the equip to actually take (this also serves as the settle
                // wait). If it never matches, the class isn't in this account's inventory
                // (a misconfigured class type) - fall back to whatever's actually worn so
                // the skill engine runs a real rotation instead of a phantom class, which
                // would silently only auto-attack.
                bool equipped = false;
                for (int i = 0; i < 6; i++) // up to ~1.5s
                {
                    Thread.Sleep(250);
                    string? worn = _bot.Player?.CurrentClass?.Name;
                    if (!string.IsNullOrEmpty(worn) &&
                        worn.Trim().Equals(classToEquip.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        equipped = true;
                        break;
                    }
                }

                if (equipped)
                {
                    _skillClass = classToEquip;
                }
                else
                {
                    string? worn = _bot.Player?.CurrentClass?.Name;
                    _log($"Couldn't equip '{classToEquip}' - not in {me}'s inventory? Keeping current class ({worn ?? "unknown"}).",
                        $"Couldn't equip '{classToEquip}' - not in this account's inventory? Keeping current class ({worn ?? "unknown"}).");
                    _skillClass = worn;
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog.Log("Follower", $"class equip failed: {ex.Message}");
        }

        // NOTE: skills are NOT started here. Skua's auto-skill engine breaks if it's
        // started, stopped, then started again within a few seconds - which is exactly
        // what happens if we start skills now and then immediately pause to travel to
        // the master. Skills start lazily in HandleCombat, only once we're in the
        // master's room and actually fighting, so there's no rapid start-stop-start.
    }

    /// <summary>Waits (bounded) for the character to finish loading after a relogin.</summary>
    private void WaitForCharacterReady(CancellationToken token)
    {
        for (int i = 0; i < 40 && !token.IsCancellationRequested; i++) // up to ~10s
        {
            try
            {
                if (_bot.Player?.LoggedIn == true && _bot.Player.Alive &&
                    _bot.Map?.Loaded == true &&
                    !string.IsNullOrEmpty(_bot.Player.CurrentClass?.Name))
                    break;
            }
            catch
            {
            }
            Wait(token, 250);
        }
        DebugLog.Log("Follower", $"character ready: class={_bot.Player?.CurrentClass?.Name} map={_bot.Map?.Name} loaded={_bot.Map?.Loaded}");
    }

    private void StartSkills()
    {
        RefreshSkillState();
        if (_skillsActive)
        {
            if (_skillsPaused)
                ResumeSkills();
            return;
        }

        try
        {
            string skillClass = _skillClass ?? _bot.Player?.CurrentClass?.Name ?? "generic";
            DebugLog.Log("Follower", $"starting skill engine: {skillClass} ({_skillMode})");
            _bot.Skills.StartAdvanced(skillClass, false, _skillMode);
            RefreshSkillState();
            DebugLog.Log("Follower", $"skill engine start state: TimerRunning={_skillsActive} IsPaused={_skillsPaused}");
            if (!_skillsActive)
                DebugLog.Log("Follower", "skill engine did not report a running timer after StartAdvanced");
        }
        catch (Exception ex)
        {
            RefreshSkillState();
            DebugLog.Log("Follower", $"skill engine start failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops Skua's built-in Auto Attack/Hunt if the user has it on - it fights the butler's
    /// own movement and skill engine. Mirrors what Skua does when a script takes over
    /// (ScriptManager.StartScript calls Auto.StopAsync). Called on follow-start and every
    /// tick, so re-enabling it mid-follow is undone. Logs only on the off->on transition.
    /// </summary>
    private void StopUserAuto()
    {
        try
        {
            if (_bot.Auto.IsRunning)
            {
                if (!_autoStopLogged)
                {
                    _autoStopLogged = true;
                    _log("Stopped Skua's Auto Attack/Hunt - it conflicts with following; the butler runs combat itself.",
                        "Stopped Skua's Auto Attack/Hunt - it conflicts with following; the butler runs combat itself.");
                }
                _bot.Auto.Stop();
            }
            else
            {
                _autoStopLogged = false;
            }
        }
        catch
        {
        }
    }

    private void StopSkills()
    {
        try
        {
            // ScriptSkill.Stop cancels its token but does not signal the ManualResetEvent
            // used by Pause. Stopping while paused therefore strands its worker inside
            // WaitOne(), leaves TimerRunning true, and makes every later StartAdvanced a
            // no-op. Wake the worker first so it can observe cancellation and exit.
            if (_bot.Skills is Skua.Core.Scripts.ScriptSkill ss && ss.IsPaused)
            {
                DebugLog.Log("Follower", "resuming paused skill engine before stop to release Skua's pause wait");
                ss.Resume();
            }

            _bot.Skills.Stop();
        }
        catch (Exception ex)
        {
            DebugLog.Log("Follower", $"skill engine stop failed: {ex.Message}");
        }

        RefreshSkillState();
        DebugLog.Log("Follower", $"skill engine stop state: TimerRunning={_skillsActive} IsPaused={_skillsPaused}");
    }

    /// <summary>
    /// Pauses skill CASTING without tearing the engine down - the rotation position is kept,
    /// so resuming doesn't reset it. A full Stop + StartAdvanced DOES reset it, which stalls
    /// Wait-For-Cooldown skills (they re-wait for the first skill's cooldown). Pausing also
    /// halts the engine's target re-acquisition, so the deaggro cancel still sticks. Pause/
    /// The concrete ScriptSkill also exposes its real IsPaused state, which lets Butler
    /// verify that pause/resume succeeded instead of letting its private flags drift.
    /// </summary>
    private void PauseSkills()
    {
        RefreshSkillState();
        if (!_skillsActive || _skillsPaused)
            return;
        if (_bot.Skills is Skua.Core.Scripts.ScriptSkill ss)
        {
            try
            {
                ss.Pause();
                RefreshSkillState();
                DebugLog.Log("Follower", _skillsPaused
                    ? "skill engine paused (rotation kept)"
                    : "skill engine did not enter the paused state");
            }
            catch (Exception ex)
            {
                RefreshSkillState();
                DebugLog.Log("Follower", $"skill pause failed: {ex.Message}");
            }
        }
        else
        {
            StopSkills(); // no Pause on this build - degrade to old behavior
        }
    }

    private void ResumeSkills()
    {
        RefreshSkillState();
        if (!_skillsActive)
        {
            StartSkills();
            return;
        }
        if (!_skillsPaused)
            return;

        if (_bot.Skills is Skua.Core.Scripts.ScriptSkill ss)
        {
            try
            {
                ss.Resume();
                RefreshSkillState();
                DebugLog.Log("Follower", !_skillsPaused
                    ? "skill engine resumed"
                    : "skill engine still reports paused after Resume");
            }
            catch (Exception ex)
            {
                RefreshSkillState();
                DebugLog.Log("Follower", $"skill resume failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Reconciles Butler's cached skill state with Skua's worker. Skua can terminate its
    /// timer after an internal exception, so Butler's flags must never be the sole source
    /// of truth or HandleCombat will continue basic attacks without restarting skills.
    /// </summary>
    private void RefreshSkillState()
    {
        bool running = _bot.Skills.TimerRunning;
        bool paused = running &&
            _bot.Skills is Skua.Core.Scripts.ScriptSkill ss &&
            ss.IsPaused;

        _skillsActive = running;
        _skillsPaused = paused;
    }

    /// <summary>
    /// EXPERIMENTAL instant warning detection: plugins never get ExtensionPacketReceived
    /// (no running script pumps it), but the raw flash-call stream fires regardless.
    /// String-matching the raw packet text gives v3-style immediate locked-zone /
    /// room-full / ignoring-goto reactions.
    /// </summary>
    private void OnFlashCall(string function, object[] args)
    {
        try
        {
            if (!_settings.InstantWarnings())
                return;
            // Server->client packets (where warnings arrive) come in under "pext";
            // "packet" is the outgoing client-command stream and never has warnings.
            if (!string.Equals(function, "pext", StringComparison.OrdinalIgnoreCase))
                return;

            if (!_packetStreamLogged)
            {
                _packetStreamLogged = true;
                DebugLog.Log("Follower", "instant warnings: packet stream active");
            }

            if (args is not { Length: > 0 })
                return;
            string text = string.Join(" ", args.Select(a => a?.ToString() ?? ""));
            if (text.Length == 0)
                return;

            if (text.Contains("a Locked zone.", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("is not available.", StringComparison.OrdinalIgnoreCase))
            {
                _lockedZone = true;
                DebugLog.Log("Follower", "instant warning: locked zone / not available");
            }

            if (text.Contains("is full", StringComparison.OrdinalIgnoreCase))
            {
                _roomFull = true;
                DebugLog.Log("Follower", "instant warning: room full");
            }

            if (text.Contains("ignoring goto", StringComparison.OrdinalIgnoreCase))
            {
                _gotoOff = true;
                DebugLog.Log("Follower", "instant warning: ignoring goto");
            }
        }
        catch
        {
        }
    }

    private void HandleCombat(SyncData master)
    {
        try
        {
            // Recover if Skua's skill worker exited silently or its pause state changed
            // independently of Butler's cached flags.
            RefreshSkillState();
            bool shouldAttack = !_settings.PassiveAttack() || master.Attacking;

            if (shouldAttack)
            {
                if (!_skillsActive)
                    StartSkills();
                else if (_skillsPaused)
                    ResumeSkills();
                _bot.Combat.Attack("*");
            }
            else
            {
                // Pause (not stop) the engine on a passive idle: a full stop + restart
                // resets the rotation, stalling Wait-For-Cooldown skills. Pausing also
                // halts target re-acquisition, so the deaggro cancel below sticks.
                if (_skillsActive && !_skillsPaused)
                    PauseSkills();
                StopAttacking(deaggroJump: true);
            }
        }
        catch
        {
        }
    }

    /// <param name="deaggroJump">
    /// Also rejoin the current cell - canceling the target alone leaves monsters
    /// aggro'd and beating on the butler; a same-cell jump resets that (v4's
    /// QuickDeaggro trick).
    /// </param>
    private void StopAttacking(bool deaggroJump = false)
    {
        try
        {
            bool wasFighting = _bot.Player?.HasTarget == true || _bot.Player?.InCombat == true;
            if (!wasFighting)
                return;

            // Both are needed: CancelTarget alone leaves the auto-attack engaged and
            // the client immediately re-targets.
            _bot.Combat.CancelAutoAttack();
            _bot.Combat.CancelTarget();

            if (deaggroJump)
            {
                DebugLog.Log("Follower", "same-cell rejoin to drop monster aggro");
                _bot.Map!.Jump(_bot.Player?.Cell ?? "Enter", _bot.Player?.Pad ?? "Spawn", autoCorrect: false);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Mirrors the master's active and registered quests. Quests already registered
    /// locally by another feature are left alone; only registrations added here are removed.
    /// </summary>
    private void SyncRegisteredQuests(string master)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastQuestSyncCheck).TotalSeconds < 1)
            return;
        _lastQuestSyncCheck = now;

        DateTime fileTime = QuestSync.GetFileTime(master);
        if (fileTime == DateTime.MinValue || fileTime <= _lastQuestSyncFileTime)
            return;

        int[]? requestedIds = QuestSync.Read(master);
        if (requestedIds is null)
            return;

        HashSet<int> requested = new(requestedIds);
        int[] removed = _syncedQuestIds.Where(id => !requested.Contains(id)).ToArray();
        if (removed.Length > 0)
        {
            _bot.Quests.UnregisterQuests(removed);
            _syncedQuestIds.ExceptWith(removed);
        }

        HashSet<int> alreadyRegistered = new(_bot.Quests.Registered);
        int[] added = requested.Where(id => !alreadyRegistered.Contains(id)).ToArray();
        if (added.Length > 0)
        {
            _bot.Quests.RegisterQuests(added);
            _syncedQuestIds.UnionWith(added);
        }

        _lastQuestSyncFileTime = fileTime;
        DebugLog.Log("QuestSync",
            $"{master}: added {added.Length}, removed {removed.Length}, tracking {requested.Count} active/registered quest(s)");
    }

    /// <summary>Unregisters only the quest IDs this follower added for its current master.</summary>
    private void ClearSyncedQuestRegistrations()
    {
        if (_syncedQuestIds.Count == 0)
        {
            _lastQuestSyncFileTime = DateTime.MinValue;
            _lastQuestSyncCheck = DateTime.MinValue;
            return;
        }

        try
        {
            int[] questIds = _syncedQuestIds.ToArray();
            _bot.Quests.UnregisterQuests(questIds);
            DebugLog.Log("QuestSync", $"removed {_syncedQuestIds.Count} mirrored quest registration(s)");
        }
        catch (Exception ex)
        {
            DebugLog.Log("QuestSync", $"could not remove mirrored quests: {ex.Message}");
        }
        finally
        {
            _syncedQuestIds.Clear();
            _lastQuestSyncFileTime = DateTime.MinValue;
            _lastQuestSyncCheck = DateTime.MinValue;
        }
    }

    /// <summary>
    /// Mirrors the master's live in-room position without waiting for the walk to finish.
    /// Room and cell transitions remain controlled by the existing goto/jump/rescue logic.
    /// </summary>
    private void CopyMasterWalk(PlayerInfo master)
    {
        IScriptPlayer? player = _bot.Player;
        if (player is null)
            return;

        int targetX = (int)Math.Round(master.X);
        int targetY = (int)Math.Round(master.Y);
        double deltaX = targetX - player.X;
        double deltaY = targetY - player.Y;

        // Avoid issuing tiny correction walks when both characters are already together.
        if (deltaX * deltaX + deltaY * deltaY <= 100)
        {
            _hasWalkTarget = false;
            return;
        }

        DateTime now = DateTime.UtcNow;
        bool targetChanged = !_hasWalkTarget ||
            Math.Abs(targetX - _lastWalkTargetX) >= 4 ||
            Math.Abs(targetY - _lastWalkTargetY) >= 4;
        double elapsedMs = (now - _lastWalkCommandTime).TotalMilliseconds;
        if (!targetChanged && elapsedMs < 1500)
            return;

        // Reuse Goto delay as the movement command throttle, with a safe 250ms floor.
        int throttleMs = Math.Clamp(_settings.GotoDelayMs(), LoopDelayMs, 2000);
        if (elapsedMs < throttleMs)
            return;

        try
        {
            int speed = player.WalkSpeed > 0 ? Math.Clamp(player.WalkSpeed, 1, 20) : 8;
            _bot.Flash.Call("walkTo", targetX, targetY, speed);

            _lastWalkTargetX = targetX;
            _lastWalkTargetY = targetY;
            _lastWalkCommandTime = now;
            _hasWalkTarget = true;
        }
        catch (Exception ex)
        {
            DebugLog.Log("Follower", $"copy walk failed: {ex.Message}");
        }
    }

    /// <returns>true if a goto was actually sent (not rate-limited).</returns>
    private bool TryGoto(string master)
    {
        // Default (500ms) matches Butler v3's loop cadence. 250ms = the loop tick, the
        // floor; the setting is the throttle for anyone who wants it gentler.
        int delayMs = Math.Clamp(_settings.GotoDelayMs(), 0, 60000);
        if ((DateTime.UtcNow - _lastGoto).TotalMilliseconds < delayMs)
            return false;

        _lastGoto = DateTime.UtcNow;
        try
        {
            DebugLog.Log("Follower", $"goto {master} (my map={_bot.Map?.Name})");
            _bot.Player.Goto(master);
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Log("Follower", $"goto failed: {ex.Message}");
            return false;
        }
    }

    /// <returns>True if a join was attempted; false when the 5-second throttle held it.</returns>
    private bool JoinMasterRoom(SyncData m, CancellationToken token)
    {
        // One join attempt per target per 5s. Re-joining every loop tick keeps
        // reloading the room, which looks like stuttering and can prevent the
        // transfer from ever completing.
        if (string.Equals(m.MapWithRoom, _lastJoinTarget, StringComparison.OrdinalIgnoreCase) &&
            (DateTime.UtcNow - _lastJoinTime).TotalMilliseconds < 5000)
            return false;

        _lastJoinTarget = m.MapWithRoom;
        _lastJoinTime = DateTime.UtcNow;

        try
        {
            DebugLog.Log("Follower", $"join {m.MapWithRoom} cell={m.Cell} pad={m.Pad} (from map={_bot.Map?.Name})");
            StopAttacking();
            if (m.Map.Equals("house", StringComparison.OrdinalIgnoreCase))
            {
                // The server ignores a normal room transfer to house-<number>.
                // Enter the master's own house by username, then verify the exact
                // instance from their sync file before the combat path may resume.
                string roomBefore = GetMyMapRoom();
                _bot.Send.Packet($"%xt%zm%house%1%{m.Username}%");

                // ForMapLoad("house") can return immediately when we started in
                // another house. Wait for the room title instead, allowing the
                // house transfer (or its post-combat cooldown) time to settle.
                for (int i = 0; i < 24 && !token.IsCancellationRequested; i++)
                {
                    Wait(token, 250);
                    string currentRoom = GetMyMapRoom();
                    if (currentRoom.Equals(m.MapWithRoom, StringComparison.OrdinalIgnoreCase))
                    {
                        DebugLog.Log("Follower", $"house join reached {currentRoom}");
                        return true;
                    }

                    if (currentRoom.StartsWith("house-", StringComparison.OrdinalIgnoreCase) &&
                        !currentRoom.Equals(roomBefore, StringComparison.OrdinalIgnoreCase))
                    {
                        // We entered the master's own house but not their recorded
                        // instance. They may be visiting another player's house.
                        _blockedRoom = m.MapWithRoom;
                        ParkOnce($"House join reached {currentRoom}, but {m.Username} is in {m.MapWithRoom}. Parked until they change rooms.",
                            "House join reached a different house from the master's room. Parked until they change rooms.");
                        return true;
                    }
                }
            }
            else
            {
                if (string.Equals(_bot.Map?.Name, m.Map, StringComparison.OrdinalIgnoreCase))
                {
                    // ScriptMap.Join's internal loop only runs when the map name
                    // changes, even with ignoreCheck=true. Send the transfer for a
                    // different room instance on the same map directly.
                    _bot.Map!.JoinPacket(m.MapWithRoom, m.Cell, m.Pad);
                }
                else
                {
                    _bot.Map!.Join(m.MapWithRoom, m.Cell, m.Pad, ignoreCheck: true);
                }

                _bot.Wait.ForMapLoad(m.Map);
            }
            DebugLog.Log("Follower", $"join done, now on map={_bot.Map?.Name} loaded={_bot.Map?.Loaded}");
        }
        catch (Exception ex)
        {
            _log($"Join {m.MapWithRoom} failed: {ex.Message}",
                "Joining the master's room failed. See ButlerV5's debug log for details.");
            DebugLog.Log("Follower", $"join {m.MapWithRoom} FAILED: {ex}");
        }
        return true;
    }

    private void ParkOnce(string reason, string scriptReason)
    {
        if (_parked)
            return;
        _parked = true;
        Park(reason, scriptReason);
    }

    private void Park(string reason, string scriptReason)
    {
        _isParking = true; // broadcast so the master's window keeps Summon disabled until home
        try
        {
            _log(reason, scriptReason);
            DebugLog.Log("Follower", $"PARK: {reason}");
            ForceParkingAggroOff();
            StopSkills();
            StopAttacking(deaggroJump: true);

            ParkSpot spot = _settings.Park();
            if (spot == ParkSpot.Stay)
            {
                DebugLog.Log("Follower", "park mode Stay - not moving");
                return;
            }

            // Skip a redundant transfer only when this is the local account's house;
            // the master and third parties also have "house-<id>" rooms.
            string already = GetMyMapRoom();
            if (spot == ParkSpot.House && IsInOwnHouse(already))
            {
                DebugLog.Log("Follower", "already home - park is a no-op");
                return;
            }
            if (spot == ParkSpot.Whitemap && already.StartsWith("whitemap-", StringComparison.OrdinalIgnoreCase))
            {
                DebugLog.Log("Follower", "already at whitemap - park is a no-op");
                return;
            }

            // The server rejects house joins (and can reject map joins) while in
            // combat - this is how "parking" used to end up in whitemap. Give the
            // combat state a moment to drop after the deaggro jump.
            for (int i = 0; i < 12 && _bot.Player?.InCombat == true; i++)
                ParkingWait(250);
            if (_bot.Player?.InCombat == true)
                DebugLog.Log("Follower", "still flagged in-combat after 3s - parking anyway");

            // Park at home: join own house when one is placed, whitemap otherwise.
            try
            {
                if (spot == ParkSpot.House &&
                    _bot.Player?.LoggedIn == true &&
                    _bot.House?.Items?.Any(h => h.Equipped) == true)
                {
                    DebugLog.Log("Follower", "parking in own house");
                    // The server enforces a "too soon after combat" cooldown on house
                    // joins that outlives the in-combat flag by several seconds, and
                    // the transfer itself can take a while. Retry across the cooldown;
                    // verify the exact local account house, not merely a changed
                    // house room (release can happen inside another player's house).
                    // The post-combat cooldown almost always eats an immediate attempt;
                    // waiting 3s first usually makes attempt 1 the one that lands.
                    ParkingWait(3000);

                    for (int attempt = 1; attempt <= 3; attempt++)
                    {
                        _bot.Send.Packet($"%xt%zm%house%1%{_bot.Player.Username}%");

                        for (int i = 0; i < 24; i++)
                        {
                            ParkingWait(250);
                            string current = GetMyMapRoom();
                            if (IsInOwnHouse(current))
                            {
                                DebugLog.Log("Follower", $"parked in own house ({current}, attempt {attempt})");
                                return;
                            }
                        }

                        if (attempt < 3)
                        {
                            DebugLog.Log("Follower", $"house join attempt {attempt} didn't land (post-combat cooldown?) - retrying in 5s");
                            ParkingWait(5000);
                        }
                    }

                    // 3 attempts failed. If we're STILL flagged in combat, this is almost
                    // certainly an auto-aggro room: the same-cell deaggro jump can't shed it,
                    // so every house join is rejected. Jump to Enter/Spawn (present in every
                    // map, usually monster-free) to drop aggro, then try the join once more.
                    if (_bot.Player?.InCombat == true)
                    {
                        DebugLog.Log("Follower", "still in combat after 3 house attempts - jumping to Enter/Spawn to shed aggro");
                        try { _bot.Map!.Jump("Enter", "Spawn", autoCorrect: false); } catch { }
                        for (int i = 0; i < 12 && _bot.Player?.InCombat == true; i++)
                            ParkingWait(250);

                        // Aggro is shed now, but the house-join cooldown outlives the combat
                        // flag by several seconds - a join right here gets rejected and drops
                        // us to whitemap. Wait the cooldown out, then retry once.
                        ParkingWait(4000);
                        _bot.Send.Packet($"%xt%zm%house%1%{_bot.Player!.Username}%");
                        for (int i = 0; i < 24; i++)
                        {
                            ParkingWait(250);
                            string current = GetMyMapRoom();
                            if (IsInOwnHouse(current))
                            {
                                DebugLog.Log("Follower", $"parked in own house ({current}, after Enter/Spawn deaggro)");
                                return;
                            }
                        }
                    }

                    // The last house transfer may have landed just after its wait.
                    // Only the local account's house fulfills the House park setting.
                    if (IsInOwnHouse(GetMyMapRoom()))
                    {
                        DebugLog.Log("Follower", "parked in own house after final wait");
                        return;
                    }
                    DebugLog.Log("Follower", "own house join didn't land - falling back to whitemap");
                }
            }
            catch (Exception ex)
            {
                DebugLog.Log("Follower", $"house park failed: {ex.Message}");
            }

            ForceParkingAggroOff();
            _bot.Map!.Join(ParkMap);
        }
        catch (Exception ex)
        {
            DebugLog.Log("Follower", $"park failed: {ex.Message}");
        }
        finally
        {
            ForceParkingAggroOff();
            _isParking = false;
        }
    }

    /// <summary>
    /// Parking always wins over user or script aggro settings. These values are
    /// intentionally not restored: a parked butler must stay passive until something
    /// explicitly turns aggro back on after it resumes normal following.
    /// </summary>
    private void ForceParkingAggroOff()
    {
        try
        {
            bool roomAggro = _bot.Options.AggroMonsters;
            bool mapAggro = _bot.Options.AggroAllMonsters;
            _bot.Options.AggroMonsters = false;
            _bot.Options.AggroAllMonsters = false;

            if (roomAggro || mapAggro)
                DebugLog.Log("Follower", $"parking forced aggro OFF (room={roomAggro} map={mapAggro})");
        }
        catch (Exception ex)
        {
            DebugLog.Log("Follower", $"parking aggro-off enforcement failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Long parking retries used to leave multi-second windows in which another
    /// component could turn aggro back on. Slice those waits and reassert the passive
    /// state at least every 100 ms.
    /// </summary>
    private void ParkingWait(int milliseconds)
    {
        int remaining = Math.Max(0, milliseconds);
        while (remaining > 0)
        {
            ForceParkingAggroOff();
            int slice = Math.Min(100, remaining);
            Thread.Sleep(slice);
            remaining -= slice;
        }
        ForceParkingAggroOff();
    }

    /// <summary>
    /// Our own "map-room" in the same format the broadcaster writes (name lowercased,
    /// room parsed from the game UI's room title). Empty while the map is loading.
    /// </summary>
    private string GetMyMapRoom()
    {
        try
        {
            if (_bot.Map?.Loaded != true)
                return "";

            string name = _bot.Map.Name?.ToLowerInvariant() ?? "";
            if (string.IsNullOrEmpty(name) || name == "-1")
                return "";

            string fullName = _bot.Map.FullName ?? "";
            int dash = fullName.LastIndexOf('-');
            if (dash > 0 && dash < fullName.Length - 1)
            {
                string room = fullName[(dash + 1)..];
                if (room.All(char.IsDigit))
                    return $"{name}-{room}";
            }
            return "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// AQW uses the owner's persistent CharID as the house room number. Skua's
    /// Player.ID reads myAvatar.uid, a session ID that is NOT the house owner ID.
    /// Cache a successful lookup for this username so parking retries do not keep
    /// crossing the Flash bridge. A generic "house-" prefix also matches others.
    /// </summary>
    private bool IsInOwnHouse(string room)
    {
        try
        {
            string? username = _bot.Player?.Username;
            if (string.IsNullOrWhiteSpace(username))
                return false;

            if (!string.Equals(username, _ownHouseUsername, StringComparison.OrdinalIgnoreCase))
            {
                _ownHouseUsername = username;
                _ownHouseRoom = null;
            }

            if (_ownHouseRoom == null)
            {
                int charId = _bot.Flash.GetGameObject<int>("world.myAvatar.objData.CharID");
                if (charId <= 0)
                    return false;
                _ownHouseRoom = $"house-{charId}";
                DebugLog.Log("Follower", $"own house room resolved from CharID: {_ownHouseRoom}");
            }

            return string.Equals(room, _ownHouseRoom, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            DebugLog.Log("Follower", $"own house CharID lookup failed: {ex.Message}");
            return false;
        }
    }

    private bool SameServer(SyncData m)
    {
        string myServer = GameServer.CurrentName(_bot); // name, not IP - sock7 is shared across worlds
        return string.IsNullOrEmpty(m.Server) ||
               string.Equals(m.Server, myServer, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normal follower waits remain a single cancellation-aware wait. While parked,
    /// wake every 100 ms to keep both aggro switches forced off for the entire idle
    /// period, including the longer offline/server-mismatch waits.
    /// </summary>
    private void Wait(CancellationToken token, int ms)
    {
        if (!_parked)
        {
            token.WaitHandle.WaitOne(ms);
            return;
        }

        int remaining = Math.Max(0, ms);
        while (remaining > 0 && !token.IsCancellationRequested)
        {
            ForceParkingAggroOff();
            int slice = Math.Min(100, remaining);
            if (token.WaitHandle.WaitOne(slice))
                break;
            remaining -= slice;
        }
        ForceParkingAggroOff();
    }

    /// <summary>Same server warnings Butler v3 reacts to, minus the locked-maps list.</summary>
    private void PacketListener(dynamic packet)
    {
        try
        {
            if (packet == null)
                return;

            dynamic? paramsObj = packet["params"];
            if (paramsObj is null)
                return;
            if (paramsObj!.type != "str")
                return;

            dynamic? dataObj = paramsObj!.dataObj;
            if (dataObj == null)
                return;

            string? cmd = dataObj[0];
            if (string.IsNullOrEmpty(cmd))
                return;

            if (cmd == "server")
            {
                string? text = dataObj[2]?.ToString();
                if (!string.IsNullOrWhiteSpace(text) &&
                    text.Contains("is ignoring goto requests", StringComparison.OrdinalIgnoreCase))
                    _gotoOff = true;
            }
            else if (cmd == "warning")
            {
                string chat = Convert.ToString(packet) ?? "";

                if (chat.Contains("a Locked zone.", StringComparison.OrdinalIgnoreCase) ||
                    chat.Contains("is not available.", StringComparison.OrdinalIgnoreCase))
                {
                    _lockedZone = true;
                    DebugLog.Log("Follower", "warning packet: locked zone / not available");
                }

                if (chat.Contains("is full", StringComparison.OrdinalIgnoreCase))
                {
                    _roomFull = true;
                    DebugLog.Log("Follower", "warning packet: room full");
                }

                if (chat.Contains("ignoring goto", StringComparison.OrdinalIgnoreCase))
                {
                    _gotoOff = true;
                    DebugLog.Log("Follower", "warning packet: ignoring goto");
                }
            }
        }
        catch
        {
        }
    }
}
