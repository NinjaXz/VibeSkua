using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using Newtonsoft.Json;
using Skua.Core.Flash;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.Models.Items;
using Skua.Core.Models.Quests;
using Skua.Core.Utils;

namespace Skua.Core.Scripts;

public partial class ScriptQuest : ObservableRecipient, IScriptQuest
{
    public ScriptQuest(
        Lazy<IFlashUtil> flash,
        Lazy<IScriptWait> wait,
        Lazy<IScriptOption> options,
        Lazy<IScriptPlayer> player,
        Lazy<IScriptSend> send,
        Lazy<IScriptInventory> inventory,
        Lazy<IScriptTempInv> tempInv,
        Lazy<IScriptInventoryHelper> invHelper,
        Lazy<IScriptLite> lite,
        Lazy<IScriptMap> map,
        Lazy<IScriptDrop> drop)
    {
        _lazyFlash = flash;
        _lazyWait = wait;
        _lazyOptions = options;
        _lazyPlayer = player;
        _lazySend = send;
        _lazyInventory = inventory;
        _lazyInvHelper = invHelper;
        _lazyLite = lite;
        _lazyMap = map;
        _lazyDrop = drop;

        StrongReferenceMessenger.Default.Register<ScriptQuest, ScriptStoppedMessage, int>(this, (int)MessageChannels.ScriptStatus, OnScriptStopped);
        StrongReferenceMessenger.Default.Register<ScriptQuest, LogoutMessage, int>(this, (int)MessageChannels.GameEvents, OnLogout);
    }

    private readonly Lazy<IFlashUtil> _lazyFlash;
    private readonly Lazy<IScriptWait> _lazyWait;
    private readonly Lazy<IScriptOption> _lazyOptions;
    private readonly Lazy<IScriptPlayer> _lazyPlayer;
    private readonly Lazy<IScriptSend> _lazySend;
    private readonly Lazy<IScriptInventory> _lazyInventory;
    private readonly Lazy<IScriptInventoryHelper> _lazyInvHelper;
    private readonly Lazy<IScriptLite> _lazyLite;
    private readonly Lazy<IScriptMap> _lazyMap;
    private readonly Lazy<IScriptDrop> _lazyDrop;
    private IFlashUtil Flash => _lazyFlash.Value;
    private IScriptWait Wait => _lazyWait.Value;
    private IScriptOption Options => _lazyOptions.Value;
    private IScriptPlayer Player => _lazyPlayer.Value;
    private IScriptSend Send => _lazySend.Value;
    private IScriptInventory Inventory => _lazyInventory.Value;
    private IScriptInventoryHelper InvHelper => _lazyInvHelper.Value;
    private IScriptLite Lite => _lazyLite.Value;
    private IScriptMap Map => _lazyMap.Value;
    private IScriptDrop Drop => _lazyDrop.Value;

    private Thread? _questThread;
    private CancellationTokenSource? _questsCTS;
    private CancellationTokenSource? _loadQuestsCTS;
    private readonly object _cacheLockObj = new();
    private volatile bool _cacheLoaded = false;

    public int RegisterCompleteInterval { get; set; } = 2000;

    [ObjectBinding("world.questTree", Default = "new()")]
    private Dictionary<int, Quest> _quests = new();

    private List<Quest>? _cachedTree;
    private int _lastTreeUpdate;
    public List<Quest> Tree
    {
        get
        {
            int currentTick = Environment.TickCount;
            if (_cachedTree == null || currentTick - _lastTreeUpdate > 100)
            {
                _cachedTree = Quests.Values.ToList();
                _lastTreeUpdate = currentTick;
            }
            return _cachedTree;
        }
    }
    public List<Quest> Active => Tree.FindAll(x => x.Active);
    public List<Quest> Completed => Tree.FindAll(x => x.Status == "c");
    public List<QuestData> Cached
    {
        get => CachedDictionary.Values.ToList();
        set
        {
            CachedDictionary.Clear();
            if (value != null)
            {
                foreach (QuestData quest in value)
                    CachedDictionary[quest.ID] = quest;
            }
        }
    }
    public Dictionary<int, QuestData> CachedDictionary { get; set; } = new();
    private SynchronizedList<int> _registered = new();
    private Dictionary<int, int> _registeredRewards = new();
    private Dictionary<int, int> _questCompleteCooldowns = new();
    private Dictionary<int, int> _questAcceptCooldowns = new();
    private readonly object _registeredLock = new();
    public IEnumerable<int> Registered
    {
        get
        {
            lock (_registeredLock)
                return _registered.Items.ToArray();
        }
    }

    public IReadOnlyDictionary<int, int> RegisteredRewards
    {
        get
        {
            lock (_registeredLock)
                return new Dictionary<int, int>(_registeredRewards);
        }
    }

    public void Load(params int[] ids)
    {
        if (ids.Length < 30)
        {
            Flash.CallGameFunction("world.showQuests", ids.Select(id => id.ToString()).Join(','), "q");
            return;
        }

        foreach (int[] idChunks in ids.Chunk(30))
        {
            Flash.CallGameFunction("world.showQuests", idChunks.Select(id => id.ToString()).Join(','), "q");
            Thread.Sleep(Options.ActionDelay);
        }
    }

    public Quest? EnsureLoad(int id)
    {
        Wait.ForTrue(() => Tree.Contains(x => x.ID == id), () => Load(id), 20, 1000);
        return Tree.Find(q => q.ID == id)!;
    }

    public bool TryGetQuest(int id, out Quest? quest)
    {
        return (quest = Tree.Find(x => x.ID == id)) != null;
    }

    public bool Abandon(int id)
    {
        if (EnsureLoad(id) is null)
            return false;

        Flash.CallGameFunction("world.abandonQuest", id);
        Wait.ForTrue(() => !IsInProgress(id), 5);
        _cachedTree = null;
        return !IsInProgress(id);
    }

    public bool Accept(int id)
    {
        Wait.ForActionCooldown(GameActions.AcceptQuest);
        Flash.CallGameFunction("world.acceptQuest", id);
        Wait.ForQuestAccept(id);
        return IsInProgress(id);
    }

    public void Accept(params int[] ids)
    {
        foreach (int t in ids)
        {
            Accept(t);
            Thread.Sleep(Options.ActionDelay);
        }
    }

    public bool EnsureAccept(int id)
    {
        for (int i = 0; i < Options.QuestAcceptAndCompleteTries; i++)
        {
            Accept(id);
            if (IsInProgress(id))
                break;
            Thread.Sleep(Options.ActionDelay);
        }
        return IsInProgress(id);
    }

    public void EnsureAccept(params int[] ids)
    {
        foreach (int t in ids)
        {
            EnsureAccept(t);
            Thread.Sleep(Options.ActionDelay);
        }
    }

    public bool Complete(int id, int itemId = -1, bool special = false)
    {
        Wait.ForActionCooldown(GameActions.TryQuestComplete);
        Flash.CallGameFunction("world.tryQuestComplete", id, itemId, special);
        Wait.ForQuestComplete(id);
        return !IsInProgress(id);
    }

    public void Complete(params int[] ids)
    {
        foreach (int t in ids)
        {
            Complete(t);
            Thread.Sleep(Options.ActionDelay);
        }
    }

    public bool EnsureComplete(int id, int itemId = -1, bool special = false)
    {
        _EnsureComplete(id, itemId, special);
        return !IsInProgress(id);
    }

    private void _EnsureComplete(int id, int itemId = -1, bool special = false)
    {
        if (id == 0)
            return;
        for (int i = 0; i < Options.QuestAcceptAndCompleteTries; i++)
        {
            Complete(id, itemId, special);
            if (!IsInProgress(id))
                break;
            Thread.Sleep(Options.ActionDelay);
        }
    }

    public void EnsureComplete(params int[] ids)
    {
        foreach (int t in ids)
        {
            EnsureComplete(t);
            Thread.Sleep(Options.ActionDelay);
        }
    }

    [MethodCallBinding("world.isQuestInProgress", GameFunction = true)]
    private bool _isInProgress(int id)
    {
        return false;
    }

    public bool UpdateQuest(int id)
    {
        Quest? quest = EnsureLoad(id);
        if (quest is null)
            return false;
        Send.ClientPacket("{\"t\":\"xt\",\"b\":{\"r\":-1,\"o\":{\"cmd\":\"updateQuest\",\"iValue\":" + quest.Value + ",\"iIndex\":" + quest.Slot + "}}}", "json");
        return true;
    }

    public void UpdateQuest(int value, int slot)
    {
        Send.ClientPacket("{\"t\":\"xt\",\"b\":{\"r\":-1,\"o\":{\"cmd\":\"updateQuest\",\"iValue\":" + value + ",\"iIndex\":" + slot + "}}}", "json");
    }

    public bool CanComplete(int id)
    {
        return Completed.Contains(q => q.ID == id);
    }

    public bool CanCompleteFullCheck(int id)
    {
        if (CanComplete(id))
            return true;

        Quest? quest = EnsureLoad(id);
        if (quest is null)
            return false;

        // Check if quest data is fully loaded (Requirements should not be null)
        if (quest.Requirements is null || quest.AcceptRequirements is null)
            return false;

        List<ItemBase> requirements = new();
        requirements.AddRange(quest.Requirements);
        requirements.AddRange(quest.AcceptRequirements);
        return requirements.Count == 0 || requirements.All(item => InvHelper.Check(item.ID, item.Quantity, false));
    }

    public bool IsDailyComplete(int id)
    {
        Quest? quest = EnsureLoad(id);
        return quest is not null && IsDailyComplete(quest);
    }

    public bool IsDailyComplete(Quest quest)
    {
        return Flash.CallGameFunction<int>("world.getAchievement", quest.Field, quest.Index) > 0;
    }

    public bool IsUnlocked(int id)
    {
        Quest? quest = EnsureLoad(id);
        return quest is not null && IsUnlocked(quest);
    }

    public bool IsUnlocked(Quest quest)
    {
        return quest.Slot < 0 || Flash.CallGameFunction<int>("world.getQuestValue", quest.Slot) >= quest.Value - 1;
    }

    public bool HasBeenCompleted(int id)
    {
        Quest? quest = EnsureLoad(id);
        return quest is not null && HasBeenCompleted(quest);
    }

    public bool HasBeenCompleted(Quest quest)
    {
        return quest.Slot < 0 || Flash.CallGameFunction<int>("world.getQuestValue", quest.Slot) >= quest.Value;
    }

    public bool IsAvailable(int id)
    {
        Quest? quest = EnsureLoad(id);
        return quest is not null
               && !IsDailyComplete(quest)
               && IsUnlocked(quest)
               && (!quest.Upgrade || Player.IsMember)
               && Player.Level >= quest.Level
               && (quest.RequiredClassID <= 0 || Flash.CallGameFunction<int>("world.myAvatar.getCPByID", quest.RequiredClassID) >= quest.RequiredClassPoints)
               && (quest.RequiredFactionId <= 1 || Flash.CallGameFunction<int>("world.myAvatar.getRep", quest.RequiredFactionId) >= quest.RequiredFactionRep)
               && quest.AcceptRequirements.All(r => Inventory.Contains(r.Name, r.Quantity));
    }

    public void RegisterQuests(params int[] ids)
    {
        RegisterQuests(ids.Select(id => (id, -1)).ToArray());
    }

    public void RegisterQuests(params (int questId, int rewardId)[] quests)
    {
        if (quests.Length == 0)
            return;

        lock (_registeredLock)
        {
            // Register quests immediately (without requirements)
            foreach ((int questId, int rewardId) in quests)
            {
                if (!_registered.Items.Contains(questId))
                {
                    _registered.Add(questId);
                }
                _registeredRewards[questId] = rewardId;

                // Add reward item to drops if specified
                if (rewardId > 0)
                {
                    Drop.Add(rewardId);
                }
            }
        }

        OnPropertyChanged(nameof(Registered));
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<IEnumerable<int>>(this, nameof(Registered), Registered, Registered));

        // Enable pick drops when quests are registered
        if (!Drop.Enabled)
            Drop.Start();

        // Cancel previous load task if still running
        _loadQuestsCTS?.Cancel();
        _loadQuestsCTS?.Dispose();
        _loadQuestsCTS = new CancellationTokenSource();
        CancellationToken token = _loadQuestsCTS.Token;

        // Load quest data and add requirements in background
        Task.Run(() =>
        {
            foreach ((int questId, int rewardId) in quests)
            {
                if (token.IsCancellationRequested)
                    return;

                try
                {
                    Quest? questData = EnsureLoad(questId);
                    if (questData != null)
                    {
                        // Add Requirements
                        if (questData.Requirements != null && questData.Requirements.Count > 0)
                        {
                            List<int> requirementIds = new();
                            foreach (ItemBase r in questData.Requirements)
                            {
                                if (r != null && !r.Temp)
                                    requirementIds.Add(r.ID);
                            }
                            if (requirementIds.Count > 0)
                                Drop.Add(requirementIds.ToArray());
                        }

                        // Add AcceptRequirements
                        if (questData.AcceptRequirements != null && questData.AcceptRequirements.Count > 0)
                        {
                            List<int> acceptReqIds = new();
                            foreach (ItemBase r in questData.AcceptRequirements)
                            {
                                if (r != null && !r.Temp)
                                    acceptReqIds.Add(r.ID);
                            }
                            if (acceptReqIds.Count > 0)
                                Drop.Add(acceptReqIds.ToArray());
                        }
                    }
                }
                catch { /* Quest failed to load, ignore */ }
            }
        }, token);
        if (!_questThread?.IsAlive ?? true)
        {
            _questThread = new(async () =>
            {
                _questsCTS = new();
                try
                {
                    await _Poll(_questsCTS.Token);
                }
                catch { /* ignored */ }
                _questsCTS?.Dispose();
                _questsCTS = null;
            })
            {
                Name = "Quest Thread"
            };
            _questThread.Start();
        }
    }

    public void UnregisterQuests(params int[] ids)
    {
        lock (_registeredLock)
        {
            _registered.Remove(ids.Contains);
            foreach (int id in ids)
            {
                _registeredRewards.Remove(id);
                _questCompleteCooldowns.Remove(id);
                _questAcceptCooldowns.Remove(id);
            }
        }

        OnPropertyChanged(nameof(Registered));
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<IEnumerable<int>>(this, nameof(Registered), Registered, Registered));
    }

    public void UnregisterAllQuests()
    {
        lock (_registeredLock)
        {
            _registered.Clear();
            _registeredRewards.Clear();
            _questCompleteCooldowns.Clear();
            _questAcceptCooldowns.Clear();
        }
        OnPropertyChanged(nameof(Registered));
        WeakReferenceMessenger.Default.Send(new PropertyChangedMessage<IEnumerable<int>>(this, nameof(Registered), Registered, Registered));

        // Cancel load quests task
        _loadQuestsCTS?.Cancel();
        _loadQuestsCTS?.Dispose();
        _loadQuestsCTS = null;

        if (_questThread?.IsAlive ?? false)
        {
            _questsCTS?.Cancel();
            Wait.ForTrue(() => _questsCTS is null, 20);
        }
    }

    private async Task _Poll(CancellationToken token)
    {
        _lastComplete = Environment.TickCount;

        while (!token.IsCancellationRequested)
        {
            try
            {
                if (Player.Playing)
                {
                    int[] registered;
                    lock (_registeredLock)
                        registered = _registered.Items.ToArray();

                    if (registered.Length > 0)
                        await _CompleteQuest(registered, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Quest processing iteration failed; it will retry: {ex}");
            }

            await Task.Delay(RegisterCompleteInterval, token);
        }
    }

    private int _lastComplete;

    private async Task _CompleteQuest(IEnumerable<int> registered, CancellationToken token)
    {
        int currentTick = Environment.TickCount;

        // Periodic cleanup: remove stale cooldowns every 60 seconds to prevent memory bloat
        if (currentTick - _lastComplete > 60000)
        {
            lock (_registeredLock)
            {
                HashSet<int> registeredSet = new(_registered.Items);

                // Remove cooldowns for quests that are no longer registered
                List<int> staleComplete = new();
                foreach (int k in _questCompleteCooldowns.Keys)
                {
                    if (!registeredSet.Contains(k))
                        staleComplete.Add(k);
                }
                List<int> staleAccept = new();
                foreach (int k in _questAcceptCooldowns.Keys)
                {
                    if (!registeredSet.Contains(k))
                        staleAccept.Add(k);
                }

                foreach (int key in staleComplete)
                    _questCompleteCooldowns.Remove(key);
                foreach (int key in staleAccept)
                    _questAcceptCooldowns.Remove(key);
            }
        }

        foreach (int quest in registered)
        {
            // Check cooldown - don't try to complete if we just tried within the last 3 seconds
            lock (_registeredLock)
            {
                if (_questCompleteCooldowns.TryGetValue(quest, out int lastAttempt) &&
                    currentTick - lastAttempt < 3000)
                {
                    continue;
                }
            }

            // Ensure quest is accepted
            if (!IsInProgress(quest))
            {
                // Check accept cooldown
                lock (_registeredLock)
                {
                    if (_questAcceptCooldowns.TryGetValue(quest, out int lastAccept) &&
                        currentTick - lastAccept < 2000)
                    {
                        continue;
                    }
                }

                Wait.ForActionCooldown(GameActions.AcceptQuest);
                Accept(quest);
                lock (_registeredLock)
                    _questAcceptCooldowns[quest] = Environment.TickCount;

                // Invalidate cache to force fresh read
                _cachedTree = null;

                // Wait for quest to be accepted
                await Wait.ForTrueAsync(() => IsInProgress(quest), 20, token: token);
                await Task.Delay(Options.ActionDelay, token);
            }

            // Check if requirements are met
            if (!CanComplete(quest))
                continue;

            Quest? questData = Tree.Find(q => q.ID == quest);
            if (questData == null)
                continue;

            int turnIns = questData.Once || !string.IsNullOrEmpty(questData.Field) ? 1 :
                Flash.CallGameFunction<int>("world.maximumQuestTurnIns", quest);

            int rewardId;
            lock (_registeredLock)
                rewardId = _registeredRewards.TryGetValue(quest, out int reward) ? reward : -1;

            Wait.ForActionCooldown(GameActions.TryQuestComplete);

            // Snapshot check: ensure items still in inventory right before packet send
            if (!CanComplete(quest))
                continue;

            Send.Packet($"%xt%zm%tryQuestComplete%{Map.RoomID}%{quest}%{rewardId}%false%{turnIns}%wvz%");

            lock (_registeredLock)
                _questCompleteCooldowns[quest] = Environment.TickCount;

            // Invalidate cache to force fresh read
            _cachedTree = null;

            // Wait for quest to be turned in (no longer in progress)
            await Wait.ForTrueAsync(() => !IsInProgress(quest), 20, token: token);
            await Task.Delay(Options.ActionDelay, token);

            // Re-accept if ReacceptQuest is disabled (otherwise game auto-accepts)
            if (!Lite.ReacceptQuest)
            {
                Wait.ForActionCooldown(GameActions.AcceptQuest);
                Accept(quest);
                lock (_registeredLock)
                    _questAcceptCooldowns[quest] = Environment.TickCount;

                // Invalidate cache to force fresh read
                _cachedTree = null;

                // Wait for quest to be accepted again
                await Wait.ForTrueAsync(() => IsInProgress(quest), 20, token: token);
                await Task.Delay(Options.ActionDelay, token);
            }

            _lastComplete = Environment.TickCount;

            // Only process one quest turn-in per cycle (game can only handle one at a time)
            break;
        }
    }

    public void LoadCachedQuests()
    {
        if (_cacheLoaded)
            return;

        lock (_cacheLockObj)
        {
            if (_cacheLoaded)
                return;

            string skuaQuestFile = File.ReadAllText(ClientFileSources.SkuaQuestsFile);
            List<QuestData>? questList = JsonConvert.DeserializeObject<List<QuestData>>(skuaQuestFile);
            if (questList != null)
            {
                CachedDictionary = questList.ToDictionary(x => x.ID, x => x);
            }
            _cacheLoaded = true;
        }
    }

    public List<QuestData> GetCachedQuests(int start, int count)
    {
        if (!_cacheLoaded)
            LoadCachedQuests();

        return CachedDictionary.Values.Skip(start).Take(count).ToList();
    }

    public List<QuestData> GetCachedQuests(params int[] ids)
    {
        if (!_cacheLoaded)
            LoadCachedQuests();

        List<QuestData> quests = new();
        foreach (int id in ids)
        {
            if (CachedDictionary.TryGetValue(id, out QuestData? value))
                quests.Add(value);
        }
        return quests;
    }

    private void OnScriptStopped(ScriptQuest recipient, ScriptStoppedMessage message)
    {
        recipient.UnregisterAllQuests();
    }

    private void OnLogout(ScriptQuest recipient, LogoutMessage message)
    {
        recipient.UnregisterAllQuests();
    }
}
