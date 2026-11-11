using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Newtonsoft.Json;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using Skua.Core.Threading;
using Skua.Core.Utils;
using System.Diagnostics;

namespace Skua.Core.Scripts;

public class ScriptInterface : IScriptInterface, IScriptInterfaceManager, IDisposable
{
    private CancellationTokenSource? ScriptInterfaceCTS;
    private readonly Thread ScriptInterfaceThread;
    private GameStateChannel? _stateChannel;
    private GameApiThreadSafeWrapper? _apiWrapper;
    private const int _timerDelay = 20;
    private readonly TimeLimiter _limit = new();
    private readonly ILogService _logger;
    private readonly IDialogService _dialogService;
    private readonly ISettingsService _settingsService;
    private bool _playerDead;

    public bool ShouldExit => Manager.ShouldExit;
    public Version Version { get; }

    public IScriptStatus Manager { get; }
    public IFlashUtil Flash { get; }
    public IScriptAuto Auto { get; }
    public IMessenger Messenger { get; }
    public IScriptBoost Boosts { get; }
    public IScriptBotStats Stats { get; }
    public IScriptSelfAuras Self { get; }
    public IScriptTargetAuras Target { get; }
    public IAuraMonitorService AuraMonitor { get; }
    public IUltraBossHelper UltraBossHelper { get; }
    public IScriptCombat Combat { get; }
    public IScriptKill Kill { get; }
    public IScriptHunt Hunt { get; }
    public IScriptDrop Drops { get; }
    public IScriptEvent Events { get; }
    public IScriptFaction Reputation { get; }
    public IScriptHouseInv House { get; }
    public IScriptInventory Inventory { get; }
    public IScriptTempInv TempInv { get; }
    public IScriptBank Bank { get; }
    public IScriptInventoryHelper InvHelper { get; }
    public IScriptLite Lite { get; }
    public IScriptOption Options { get; }
    public IScriptMap Map { get; }
    public IScriptMonster Monsters { get; }
    public IScriptPlayer Player { get; }
    public IScriptQuest Quests { get; }
    public IScriptSend Send { get; }
    public IScriptShop Shops { get; }
    public IScriptSkill Skills { get; }
    public IScriptWait Wait { get; }
    public IScriptServers Servers { get; }
    public IScriptHandlers Handlers { get; }
    public ICaptureProxy GameProxy { get; }
    public IScriptAccounts Accounts { get; }
    public IScriptOptionContainer? Config => Manager.RunningConfig ?? Manager.Config;
    public Random Random { get; set; } = new Random();

    public ScriptInterface(
        ILogService logger,
        IScriptManager manager,
        IFlashUtil flash,
        IScriptHandlers handlers,
        IScriptServers server,
        IScriptBoost boosts,
        IScriptBotStats stats,
        IScriptSelfAuras scriptSelfAuras,
        IScriptTargetAuras scriptTargetAuras,
        IScriptCombat combat,
        IScriptDrop drops,
        IScriptEvent events,
        IScriptFaction rep,
        IScriptHouseInv house,
        IScriptInventory inventory,
        IScriptTempInv tempInv,
        IScriptBank bank,
        IScriptInventoryHelper invManager,
        IScriptLite lite,
        IScriptOption options,
        IScriptMap map,
        IScriptMonster monsters,
        IScriptPlayer player,
        IScriptQuest quests,
        IScriptSend send,
        IScriptShop shops,
        IScriptSkill skills,
        IScriptWait wait,
        IScriptKill kill,
        IScriptHunt hunt,
        ICaptureProxy gameProxy,
        IScriptAuto auto,
        IDialogService dialogService,
        ISettingsService settingsService,
        IAuraMonitorService auraMonitorService,
        IUltraBossHelper ultraBossHelper,
        IScriptAccounts accounts)
    {
        _logger = logger;
        Manager = manager;
        Boosts = boosts;
        Stats = stats;
        Self = scriptSelfAuras;
        Target = scriptTargetAuras;
        Combat = combat;
        Kill = kill;
        Hunt = hunt;
        GameProxy = gameProxy;
        Auto = auto;
        Messenger = StrongReferenceMessenger.Default;
        _dialogService = dialogService;
        Drops = drops;
        Events = events;
        Reputation = rep;
        House = house;
        Inventory = inventory;
        TempInv = tempInv;
        Bank = bank;
        InvHelper = invManager;
        Lite = lite;
        Options = options;
        Map = map;
        Monsters = monsters;
        Player = player;
        Quests = quests;
        Send = send;
        Shops = shops;
        Skills = skills;
        Wait = wait;
        Servers = server;
        Handlers = handlers;
        Flash = flash;
        AuraMonitor = auraMonitorService;
        UltraBossHelper = ultraBossHelper;
        Accounts = accounts;
        _settingsService = settingsService;

        string applicationVersion = settingsService.Get("ApplicationVersion", "0.0.0.0");
        int versionSuffix = applicationVersion.IndexOfAny(['-', '+']);
        if (versionSuffix >= 0)
            applicationVersion = applicationVersion[..versionSuffix];

        Version = Version.Parse(applicationVersion);

        _stateChannel = new GameStateChannel(_ => { }, 100);
        _stateChannel.Start();
        if (events is ScriptEvent scriptEvent)
            scriptEvent.SetStateChannel(_stateChannel);

        _apiWrapper = new GameApiThreadSafeWrapper(this, 1);
        _apiWrapper.Start();
        Flash.FlashCall += HandleFlashCall;
        Messenger.Register<ScriptInterface, LogoutRequestedMessage, int>(this, (int)MessageChannels.GameEvents, (r, m) => r.CancelRelogin());

        ScriptInterfaceThread = new(() =>
        {
            ScriptInterfaceCTS = new();
            ScriptTimer(ScriptInterfaceCTS.Token);
            ScriptInterfaceCTS?.Dispose();
            ScriptInterfaceCTS = null;
        })
        {
            Name = "ScriptInterface",
            IsBackground = true
        };

        IScriptInterface.Instance = this;
    }

    public Task Schedule(int delay, Func<IScriptInterface, Task> function)
    {
        return Task.Run(async () => { await Task.Delay(delay); await function(this); });
    }

    public Task Schedule(int delay, Action<IScriptInterface> action)
    {
        return Task.Run(async () => { await Task.Delay(delay); action(this); });
    }

    public void SellJunk()
    {
        IJunkService? junkService = Ioc.Default.GetService<IJunkService>();
        junkService?.SellAllJunk();
    }

    public void Log(string message)
    {
        CheckScriptTermination();
        _logger.ScriptLog(message);
    }

    public void Sleep(int ms)
    {
        CheckScriptTermination();

        // For longer sleeps, break them up to check for cancellation more frequently
        if (ms > 1000)
        {
            int remaining = ms;
            while (remaining > 0)
            {
                CheckScriptTermination();
                int chunk = Math.Min(500, remaining);
                Thread.Sleep(chunk);
                remaining -= chunk;
            }
        }
        else
        {
            Thread.Sleep(ms);
        }

        CheckScriptTermination();
    }

    public async Task SleepAsync(int ms)
    {
        CheckScriptTermination();
        await Task.Delay(ms);
        CheckScriptTermination();
    }

    private void CheckScriptTermination()
    {
        if (Thread.CurrentThread.Name == "Script Thread")
        {
            if (Manager is IScriptManager scriptManager)
            {
                try
                {
                    scriptManager.ScriptPauseEvent.Wait(scriptManager.ScriptCts?.Token ?? CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                }
            }

            if (Manager.ShouldExit)
                throw new OperationCanceledException();
        }
    }

    public bool? ShowMessageBox(string message, string caption, bool yesAndNo = false)
    {
        return _dialogService.ShowMessageBox(message, caption, yesAndNo);
    }

    public DialogResult ShowMessageBox(string message, string caption, params string[] buttons)
    {
        return _dialogService.ShowMessageBox(message, caption, buttons);
    }

    public void Initialize()
    {
        if (!ScriptInterfaceThread.IsAlive)
            ScriptInterfaceThread.Start();
    }

    public async Task StopTimerAsync()
    {
        ScriptInterfaceCTS?.Cancel();
        await Wait.ForTrueAsync(() => ScriptInterfaceCTS == null, 20);
    }

    public void Stop(bool runScriptStoppingEvent = true)
    {
        _ = Manager.StopScript(runScriptStoppingEvent);
    }

    public async Task StopAsync(bool runScriptStoppingEvent = true)
    {
        await Manager.StopScript(runScriptStoppingEvent);
    }

    private void ScriptTimer(CancellationToken token)
    {
        bool catching = false;
        int lastConnChange = 0;
        string lastConnDetail = "";
        bool wasPlaying = false;

        Stopwatch sw = new();

        while (!token.IsCancellationRequested)
        {
            try
            {
                sw.Restart();

                bool currentInGame = Flash.IsWorldLoaded && Player.LoggedIn;
                bool currentPlaying = currentInGame && Player.Alive;

                // Track connected gameplay regardless of whether a script is running.
                _limit.LimitedRun("gameplayStats", 1000, () =>
                {
                    // Wait for the character data and inventory to finish loading before taking the first
                    // snapshot. Otherwise the initial zero gold value is counted as earned gold on login.
                    if (!currentInGame || !Player.Loaded)
                    {
                        Stats.UpdateGameplaySnapshot(0, 0, 0, 0, 0, 0, 0, 0, false);
                        return;
                    }

                    InventoryItem? equippedClass = Inventory.Items?.Find(item => item is { Equipped: true, Category: ItemCategory.Class });
                    // iCurCP is progress within the current class rank; iCPToRank is that rank's point requirement.
                    int currentClassPoints = Flash.GetGameObject<int>("world.myAvatar.objData.iCurCP", 0);
                    int classPointsToRank = Flash.GetGameObject<int>("world.myAvatar.objData.iCPToRank", 0);
                    Stats.UpdateGameplaySnapshot(Player.XP, Player.RequiredXP, Player.Level, Player.Gold,
                        equippedClass?.ID ?? 0, currentClassPoints, classPointsToRank, Player.CurrentClassRank, true);
                });

                if (currentInGame)
                {
                    wasPlaying = true;
                    Servers.LastIP = Player.ServerIP ?? Servers.LastIP;
                    if (Servers.IsIntentionalLogout)
                        Servers.IsIntentionalLogout = false;

                    if (!catching)
                    {
                        Flash.Call("catchPackets");
                        catching = true;
                    }

                    if (currentPlaying)
                    {
                        if (Options.RestPackets && !Player.InCombat)
                            _limit.LimitedRun("rest", 1200, () => Send.Packet("%xt%zm%restRequest%1%%"));

                        _limit.LimitedRun("opts", 250, CheckOptions);
                    }
                }
                else
                {
                    catching = false;
                    if (wasPlaying || Manager.ScriptRunning)
                        Messenger.Send<PlayerDisconnectedMessage, int>(new(), (int)MessageChannels.GameEvents);

                    if ((wasPlaying || Manager.ScriptRunning) && !_waitForLogin && !Servers.IsIntentionalLogout && Options.AutoRelogin && WatchdogEnabled)
                    {
                        wasPlaying = false;
                        Log("Unexpected disconnection detected by watchdog. Triggering auto re-login.");
                        BeginLogout();
                    }
                    else
                    {
                        wasPlaying = false;
                    }
                }

                _limit.LimitedRun("kicked", 1000, () =>
                {
                    if (Player.Kicked && !_waitForLogin && !Servers.IsIntentionalLogout && Options.AutoRelogin)
                    {
                        Log("Player kick detected. Triggering auto re-login.");
                        BeginLogout();
                    }
                });

                _limit.LimitedRun("connDetail", 100, () => (lastConnChange, lastConnDetail) = CheckStuckonLoading(lastConnChange, lastConnDetail));

                if (Manager.ScriptRunning)
                    RunScriptHandlers();

                sw.Stop();
                Thread.Sleep(Math.Max(10, _timerDelay - (int)sw.Elapsed.TotalMilliseconds));
            }
            catch (Exception e)
            {
                Trace.WriteLine($"Error in timer thread: {e.Message}");
            }
        }
    }

    // The relogin watchdog (Application Options); read only when a disconnect would trigger it. Not the
    // default-value overload: it turns a saved false into the default. Unsaved, the model default is true.
    private bool WatchdogEnabled => _settingsService.Get<bool>("ReloginWatchdog");

    private void CheckOptions()
    {
        if (Options.LagKiller)
            Flash.Call("killLag", true);

        if (!Player.Playing)
            return;

        if (Options.Magnetise)
            Flash.Call("magnetise");
        if (Options.InfiniteRange)
            Flash.Call("infiniteRange");
        if (Options.AggroMonsters)
            Flash.CallGameFunction("world.aggroAllMon");
        if (Options.AggroAllMonsters)
            Send.Packet($"%xt%zm%aggroMon%{Map.RoomID}%{string.Join("%", Monsters.MapMonsters.Select(m => m.MapID))}%");
        if (Options.SkipCutscenes)
            Flash.Call("skipCutscenes");
        if (Options.WalkSpeed != 8)
            Player.WalkSpeed = Options.WalkSpeed;
        if (!Lite.UntargetSelf)
            Lite.UntargetSelf = true;
        if (!Lite.UntargetDead)
            Lite.UntargetDead = true;
    }

    /// <summary>
    /// Checks if the player is stuck in the loading screen.
    /// </summary>
    /// <param name="lastConnChange">Last time the loading message changed.</param>
    /// <param name="lastConnDetail">Last loading message.</param>
    /// <returns>The last loading message and its time</returns>
    private (int newTime, string newText) CheckStuckonLoading(int lastConnChange, string lastConnDetail)
    {
        string connDetail = Flash.IsNull("mcConnDetail.stage") ? "null" : Flash.GetGameObject("mcConnDetail.txtDetail.text", "null")!;
        if (connDetail == "null")
            return (Environment.TickCount, connDetail);
        if (connDetail.Contains("has been lost") && !_waitForLogin)
        {
            Messenger.Send<PlayerDisconnectedMessage, int>(new(), (int)MessageChannels.GameEvents);
            if (!Servers.IsIntentionalLogout)
            {
                Log("Connection lost detected. Triggering auto re-login.");
                BeginLogout();
            }
        }
        else if (Environment.TickCount - lastConnChange >= Options.LoadTimeout && connDetail == lastConnDetail && !_waitForLogin)
        {
            if (connDetail.Contains("loading map"))
            {
                Map.Join("battleon");
                Map.Reload();
                Handlers.RegisterOnce(500, b =>
                {
                    if (Flash.GetGameObject("mcConnDetail.txtDetail.text") == "loading map")
                    {
                        if (Options.AutoRelogin)
                            BeginLogout();
                        else
                            Servers.Logout();
                        return;
                    }
                    Map.Join(Map.LastMap);
                });
            }
            else
            {
                if (Options.AutoRelogin)
                    BeginLogout();
                else
                    Servers.Logout();
            }
        }
        if (connDetail == lastConnDetail)
            return (lastConnChange, connDetail);
        return (Environment.TickCount, connDetail);
    }

    /// <summary>
    /// Run all registered handlers, if the handler returns <see langword="false"/> it is removed from the list.
    /// </summary>
    private void RunScriptHandlers()
    {
        if (Handlers.CurrentHandlers.ToList().Count == 0)
            return;
        List<IHandler> rem = [];
        foreach (IHandler handler in Handlers.CurrentHandlers.ToList())
        {
            _limit.LimitedRun("handler_" + handler.Name, handler.Ticks * _timerDelay, () =>
            {
                if (!handler.Function(this))
                    rem.Add(handler);
            });
        }
        Handlers.Remove(rem);
    }

    private void HandleFlashCall(string name, object[]? args)
    {
        switch (name)
        {
            case "pre-load":
                Schedule(1000, _ =>
                {
                    try
                    {
                        string sBG = _settingsService.Get("sBG", "Generic2.swf");
                        string? customBackgroundPath = _settingsService.Get<string?>("CustomBackgroundPath", null);
                        Flash.Call("setBackgroundValues", sBG, customBackgroundPath ?? "");
                    }
                    catch (Exception ex)
                    {
                        Log($"Could not set background values: {ex.Message}");
                    }
                });
                break;

            case "loaded":
                Initialize();
                break;

            case "connectionLost":
                Messenger.Send<PlayerDisconnectedMessage, int>(new(), (int)MessageChannels.GameEvents);
                Log("Flash socket connection lost event received.");
                if (!_waitForLogin && !Servers.IsIntentionalLogout && Options.AutoRelogin && WatchdogEnabled)
                {
                    BeginLogout();
                }
                break;

            case "userIntentionalAction":
                if (args is { Length: > 0 } && args[0] is string action)
                {
                    switch (action)
                    {
                        case "logout":
                            Log("Intentional logout button clicked by user in game. Auto re-login disarmed.");
                            Servers.IsIntentionalLogout = true;
                            CancelRelogin();
                            if (Manager.ScriptRunning)
                                _ = Manager.StopScript();
                            break;

                        case "changeServer":
                            Log("Intentional 'Change Server' button clicked by user in game. Auto re-login disarmed.");
                            Servers.IsIntentionalLogout = true;
                            CancelRelogin();
                            if (Manager.ScriptRunning)
                                _ = Manager.StopScript();
                            break;

                        case "selectServer":
                            Log("Server chosen by user on server list. Watchdog armed.");
                            Servers.IsIntentionalLogout = false;
                            break;
                    }
                }
                break;

            case "openWebsite":
                if (args is { Length: > 0 } && args[0] is string url)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = url,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to open website: {url}. Error: {ex.Message}");
                    }
                }
                break;

            case "debug":
                Trace.WriteLine(args[0]);
                break;

            case "pext":
                dynamic packet = JsonConvert.DeserializeObject<dynamic>((string)args[0])!;
                string type = packet["params"].type;
                dynamic data = packet["params"].dataObj;
                if (type is not null and "json")
                {
                    string cmd = data.cmd;
                    switch (cmd)
                    {
                        case "event":
                            string zone = data.args?["zoneSet"]!;
                            if (zone is not null)
                                Messenger.Send<RunToAreaMessage, int>(new(zone), (int)MessageChannels.GameEvents);
                            break;

                        case "moveToArea":
                            Stats.GetSpace();
                            Messenger.Send<MapChangedMessage, int>(new(Convert.ToString(data.strMapName)), (int)MessageChannels.GameEvents);
                            Map.FilePath = Convert.ToString(data.strMapFileName);
                            Map.LastMap = Convert.ToString(data.strMapName);
                            break;

                        case "ct":
                            dynamic p = data.p?[Player.Username.ToLower()]!;
                            if (p is not null && p.intHP == 0)
                            {
                                // Combat-state packets can repeat while the player remains dead.
                                if (!_playerDead)
                                {
                                    Stats.Deaths++;
                                    Messenger.Send<PlayerDeathMessage, int>((int)MessageChannels.GameEvents);
                                }

                                _playerDead = true;
                                break;
                            }

                            if (p is not null)
                                _playerDead = false;
                            dynamic anims = data.anims?[0]!;
                            if (anims is not null)
                            {
                                string msg = anims["msg"];
                                if (msg is not null && msg.Contains("prepares a counter attack!"))
                                {
                                    Messenger.Send<CounterAttackMessage, int>(new(false), (int)MessageChannels.GameEvents);
                                    break;
                                }
                            }
                            if (data.a is not null)
                            {
                                foreach (dynamic? a in data.a)
                                {
                                    if (a is null)
                                        continue;

                                    if (a.aura is null || (string)a.aura["nam"] is not "Counter Attack")
                                    {
                                        continue;
                                    }

                                    Messenger.Send<CounterAttackMessage, int>(new(true), (int)MessageChannels.GameEvents);
                                    break;
                                }
                            }
                            break;

                        case "sellItem":
                            Stats.GetSpace();
                            Messenger.Send<ItemSoldMessage, int>(new(
                                Convert.ToInt32(data.CharItemID),
                                Convert.ToInt32(data.iQty),
                                Convert.ToInt32(data.iQtyNow),
                                Convert.ToInt32(data.intAmount),
                                Convert.ToInt32(data.bCoins) == 1),
                                (int)MessageChannels.GameEvents);
                            break;

                        case "buyItem":
                            if (data.bitSuccess == 1)
                            {
                                Messenger.Send<ItemBoughtMessage, int>(new(Convert.ToInt32(data.CharItemID)), (int)MessageChannels.GameEvents);
                                Stats.GetSpace();
                            }
                            break;

                        case "dropItem":
                            string items = Convert.ToString(data["items"]);
                            InventoryItem drop = JsonConvert.DeserializeObject<Dictionary<string, InventoryItem>>(items)!.First().Value;
                            Messenger.Send<ItemDroppedMessage, int>(new(drop), (int)MessageChannels.GameEvents);
                            break;

                        case "addItems":
                            string addItems = Convert.ToString(data["items"]);
                            Dictionary<int, dynamic> addedItem = JsonConvert.DeserializeObject<Dictionary<int, dynamic>>(addItems)!;
                            int itemID = addedItem.Keys.First()!;
                            ItemBase invItem = Inventory.GetItem(itemID) ?? TempInv.GetItem(itemID)!;
                            Stats.GetSpace();
                            if (invItem is null)
                            {
                                invItem = Bank.GetItem(itemID)!;
                                Messenger.Send<ItemAddedToBankMessage, int>(new(invItem, invItem.Quantity), (int)MessageChannels.GameEvents);
                                break;
                            }
                            if (!invItem.Temp)
                                Stats.Drops++;
                            Messenger.Send<ItemDroppedMessage, int>(new(invItem, true, Convert.ToInt32(addedItem.Values.First().iQtyNow)), (int)MessageChannels.GameEvents);
                            break;

                        case "getDrop":
                            bool toBank = Convert.ToBoolean(data.bBank);
                            Messenger.Send<DropPickupResultMessage, int>(new(Convert.ToInt32(data.ItemID), data.bSuccess == 1), (int)MessageChannels.GameEvents);
                            if (data.bSuccess == 1)
                            {
                                int pickedUpQuantity = Convert.ToInt32(data.iQty);
                                Stats.RecordItemPickup(pickedUpQuantity);
                                Stats.Drops += pickedUpQuantity;
                                Stats.GetSpace();
                            }
                            if (toBank)
                            {
                                ItemBase bankItem = Bank.GetItem(Convert.ToInt32(data.ItemID))!;
                                Messenger.Send<ItemAddedToBankMessage, int>(new(bankItem, Convert.ToInt32(data.iQtyNow)), (int)MessageChannels.GameEvents);
                            }
                            break;

                        case "addGoldExp":
                            if (data.typ == "m")
                            {
                                Stats.Kills++;
                                Messenger.Send<MonsterKilledMessage, int>(new(Convert.ToInt32(data.id)), (int)MessageChannels.GameEvents);
                            }
                            break;

                        case "ccqr":
                            if (data.bSuccess == 1)
                            {
                                Stats.QuestsCompleted++;
                                Messenger.Send<QuestTurninMessage, int>(new(Convert.ToInt32(data.QuestID)), (int)MessageChannels.GameEvents);
                            }
                            break;

                        case "loadBank":
                            Stats.GetSpace();
                            Messenger.Send<BankLoadedMessage, int>((int)MessageChannels.GameEvents);
                            break;

                        case "loadShop":
                            Stats.GetSpace();
                            Messenger.Send<ShopLoadedMessage, int>(new(new(Shops.ID, Shops.Name, Shops.Items)), (int)MessageChannels.GameEvents);
                            break;
                    }
                }
                else if (type is not null and "str")
                {
                    string cmd = data[0];
                    switch (cmd)
                    {
                        case "popup":
                            string b = Convert.ToString(packet);
                            Debug.WriteLine(b);
                            break;

                        case "uotls":
                            if (Player.Username == (string)data[2] && data[3] == "afk:true")
                                Messenger.Send<PlayerAFKMessage, int>((int)MessageChannels.GameEvents);
                            break;

                        case "loginResponse":
                            _playerDead = false;
                            if (Flash.IsWorldLoaded)
                                Stats.GetSpace();
                            Options.CustomName = string.Empty;
                            Options.CustomGuild = string.Empty;
                            Servers.IsIntentionalLogout = false;
                            _waitForLogin = false;
                            Messenger.Send<LoginMessage, int>(new(Convert.ToString(data[4])), (int)MessageChannels.GameEvents);
                            break;

                        case "chatm":
                        case "whisper":
                        case "warning":
                        case "server":
                            try
                            {
                                var partsList = new List<string>();
                                foreach (var elem in data)
                                {
                                    partsList.Add(Convert.ToString(elem) ?? string.Empty);
                                }
                                string reconstructed = "%xt%" + string.Join("%", partsList) + "%";
                                Messenger.Send<PacketMessage, int>(new(reconstructed), (int)MessageChannels.GameEvents);
                            }
                            catch { }
                            break;
                    }
                }
                Messenger.Send<ExtensionPacketMessage, int>(new(packet), (int)MessageChannels.GameEvents);
                break;

            case "packetFromServer":
                if (args is { Length: > 0 } && args[0] is string serverPacket && !string.IsNullOrWhiteSpace(serverPacket))
                {
                    Messenger.Send<PacketMessage, int>(new(serverPacket), (int)MessageChannels.GameEvents);
                }
                break;

            case "packet":
                string[] parts = ((string)args[0]).Split('%', StringSplitOptions.RemoveEmptyEntries);
                switch (parts[2])
                {
                    case "eLO":
                    case "eLO2":
                        Log("In-game exit logout packet (eLO) detected. Disarming auto re-login.");
                        Servers.IsIntentionalLogout = true;
                        CancelRelogin();
                        if (Manager.ScriptRunning)
                            _ = Manager.StopScript();
                        break;

                    case "eSL":
                    case "eSL2":
                        Log("In-game exit to server list packet (eSL) detected. Disarming auto re-login.");
                        Servers.IsIntentionalLogout = true;
                        CancelRelogin();
                        if (Manager.ScriptRunning)
                            _ = Manager.StopScript();
                        break;

                    case "moveToCell":
                        Messenger.Send<CellChangedMessage, int>(new(Map.Name, parts[4], parts[5]), (int)MessageChannels.GameEvents);
                        break;

                    case "buyItem":
                        Stats.GetSpace();
                        Messenger.Send<TryBuyItemMessage, int>(new(int.Parse(parts[5]), int.Parse(parts[4]), int.Parse(parts[6])), (int)MessageChannels.GameEvents);
                        break;

                    case "acceptQuest":
                        Stats.QuestsAccepted++;
                        Messenger.Send<QuestAcceptedMessage, int>(new(int.Parse(parts[4])), (int)MessageChannels.GameEvents);
                        break;

                    case "cmd":
                        if (parts.Length >= 5 && parts[4] == "logout")
                        {
                            if (_waitForLogin)
                            {
                                break;
                            }

                            Messenger.Send<LogoutMessage, int>((int)MessageChannels.GameEvents);
                            if (Servers.IsIntentionalLogout)
                            {
                                Log("Intentional logout confirmed. Auto re-login skipped.");
                                CancelRelogin();
                            }
                            else
                            {
                                Log($"Server logout command detected (Kicked: {Player.Kicked}). Triggering auto re-login.");
                                BeginLogout(logoutNotified: true);
                            }
                        }
                        break;
                }
                Messenger.Send<PacketMessage, int>(new((string)args[0]), (int)MessageChannels.GameEvents);
                break;
        }
    }

    private Task? _reloginTask;
    private volatile bool _waitForLogin;
    private CancellationTokenSource? _reloginCTS;

    public void CancelRelogin()
    {
        if (_reloginCTS is not null)
        {
            try
            {
                _reloginCTS.Cancel();
            }
            catch { }
            _reloginCTS = null;
        }
        _reloginTask = null;
        _waitForLogin = false;
    }

    private async Task OnLogout(bool logoutNotified)
    {
        if (!Options.AutoRelogin || _waitForLogin || Servers.IsIntentionalLogout)
            return;

        if (_reloginTask is { IsCompleted: false })
        {
            Log("Re-login task already running.");
            return;
        }

        Log("Auto re-login triggered.");
        bool wasRunning = Manager.ScriptRunning;
        CancellationTokenSource reloginCts = new();
        _reloginCTS = reloginCts;
        _waitForLogin = true;
        try
        {
            if (!logoutNotified)
                Messenger.Send<LogoutMessage, int>((int)MessageChannels.GameEvents);
            await Manager.StopScript();
            reloginCts.Token.ThrowIfCancellationRequested();
            bool kicked = Player.Kicked;
            Messenger.Send<ReloginTriggeredMessage, int>(new(kicked), (int)MessageChannels.GameEvents);

            int delay = Options.ReloginTryDelay;
            if (Options.SafeRelogin)
            {
                delay = kicked ? 20000 : Math.Max(10000, Options.ReloginTryDelay);
            }
            else
            {
                delay = Math.Max(800, Options.ReloginTryDelay);
            }
            Relogin(delay, wasRunning, reloginCts);
        }
        catch
        {
            if (ReferenceEquals(_reloginCTS, reloginCts))
            {
                _reloginCTS = null;
                _waitForLogin = false;
            }
            reloginCts.Dispose();
            throw;
        }
    }

    private async void BeginLogout(bool logoutNotified = false)
    {
        try
        {
            await OnLogout(logoutNotified);
        }
        catch (OperationCanceledException)
        {
            Log("Auto re-login cancelled.");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Auto re-login failed: {ex}");
            Log($"Auto re-login failed: {ex.Message}");
        }
    }

    private void Relogin(int delay, bool startScript, CancellationTokenSource reloginCts)
    {
        reloginCts.Token.ThrowIfCancellationRequested();
        // A login loop already running (e.g. the startup login) owns the login screen; this one queues behind it.
        if (!Servers.IsReloginInProgress)
            Servers.DisconnectForRelogin();
        Log($"Waiting {delay}ms for re-login.");
        _reloginTask = Schedule(delay, async _ =>
        {
            try
            {
                reloginCts.Token.ThrowIfCancellationRequested();
                Stats.Relogins++;
                bool relogged = await Servers.EnsureRelogin(reloginCts.Token);
                reloginCts.Token.ThrowIfCancellationRequested();
                if (startScript && relogged)
                    await Ioc.Default.GetService<IScriptManager>()!.StartScript();
                else if (startScript && !relogged)
                    Log("Skipping script restart because re-login did not succeed.");
                Log($"Re-login was {(relogged ? "successful" : "cancelled or unsuccessful")}.");
            }
            catch (OperationCanceledException) when (reloginCts.IsCancellationRequested)
            {
                Log("Auto re-login cancelled.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Auto re-login task failed: {ex}");
                Log($"Auto re-login failed: {ex.Message}");
            }
            finally
            {
                if (ReferenceEquals(_reloginCTS, reloginCts))
                {
                    _reloginCTS = null;
                    _reloginTask = null;
                    _waitForLogin = false;
                }

                reloginCts.Dispose();
            }
        });
    }

    private bool _disposed = false;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Unsubscribe from Flash events
                Flash.FlashCall -= HandleFlashCall;
                Messenger.Unregister<LogoutRequestedMessage, int>(this, (int)MessageChannels.GameEvents);

                // Cancel and clean up the script interface thread
                ScriptInterfaceCTS?.Cancel();
                if (ScriptInterfaceThread is { IsAlive: true })
                {
                    // Give the thread time to finish gracefully
                    if (!ScriptInterfaceThread.Join(1000))
                    {
                        // Force abort if it doesn't finish in time
                        try { ScriptInterfaceThread.Interrupt(); } catch {/* ignored */ }
                    }
                }
                ScriptInterfaceCTS?.Dispose();
                ScriptInterfaceCTS = null;
                _apiWrapper?.StopAsync().GetAwaiter().GetResult();
                _apiWrapper = null;

                if (_stateChannel != null)
                {
                    _stateChannel.StopAsync().GetAwaiter().GetResult();
                    _stateChannel.Dispose();
                    _stateChannel = null;
                }

                _reloginCTS?.Cancel();
                _reloginCTS?.Dispose();
                _reloginCTS = null;
                _reloginTask = null;

                // Clear the static instance reference
                if (IScriptInterface.Instance == this)
                {
                    IScriptInterface.Instance = null;
                }
            }

            _disposed = true;
        }
    }

    ~ScriptInterface()
    {
        Dispose(false);
    }
}
