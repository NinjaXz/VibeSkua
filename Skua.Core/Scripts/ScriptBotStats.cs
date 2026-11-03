using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using System.Diagnostics;

namespace Skua.Core.Scripts;

public partial class ScriptBotStats : ObservableObject, IScriptBotStats, IAsyncDisposable
{
    private readonly Lazy<IScriptInventory> _lazyInventory;
    private readonly Lazy<IScriptBank> _lazyBank;

    private IScriptInventory Inventory => _lazyInventory.Value;
    private IScriptBank Bank => _lazyBank.Value;

    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly Stopwatch _sessionStopwatch = new();
    private readonly PeriodicTimer _timer = new(TimeSpan.FromMilliseconds(1000));
    private readonly CancellationTokenSource _ctsTimer = new();
    private readonly Task _taskTimer;

    private bool _hasGameplaySnapshot;
    private int _lastExperience;
    private int _lastRequiredExperience;
    private int _lastLevel;
    private int _lastGold;
    private int _lastKills;
    private int _lastDeaths;
    private int _lastDrops;
    private long _sessionKills;
    private long _sessionDeaths;
    private long _sessionItems;
    private long _earnedExperience;
    private long _earnedGold;

    public ScriptBotStats(Lazy<IScriptInventory> inventory, Lazy<IScriptBank> bank)
    {
        _lazyInventory = inventory;
        _lazyBank = bank;
        _taskTimer = HandleTimerAsync(_timer, _ctsTimer.Token);
    }

    [ObservableProperty]
    private int _kills;

    [ObservableProperty]
    private int _questsAccepted;

    [ObservableProperty]
    private int _questsCompleted;

    [ObservableProperty]
    private int _deaths;

    [ObservableProperty]
    private int _relogins;

    [ObservableProperty]
    private int _drops;

    [ObservableProperty]
    private int _inventorySpace;

    [ObservableProperty]
    private int _inventoryFilledSpace;

    [ObservableProperty]
    private int _inventoryFreeSpace;

    [ObservableProperty]
    private int _bankSpace;

    [ObservableProperty]
    private int _bankFilledSpace;

    [ObservableProperty]
    private int _bankFreeSpace;

    [ObservableProperty]
    private TimeSpan _time;

    [ObservableProperty]
    private TimeSpan _sessionTime;

    [ObservableProperty]
    private int _experiencePerHour;

    [ObservableProperty]
    private int _goldPerHour;

    [ObservableProperty]
    private double _killsPerMinute;

    [ObservableProperty]
    private double _deathsPerMinute;

    [ObservableProperty]
    private int _itemsPerHour;

    [ObservableProperty]
    private TimeSpan? _timeToLevel;

    public void Reset()
    {
        Kills = 0;
        QuestsAccepted = 0;
        QuestsCompleted = 0;
        Deaths = 0;
        Relogins = 0;
        Drops = 0;
        _sw.Restart();
        ResetGameplayMetrics();
    }

    public void GetSpace()
    {
        InventoryFreeSpace = Inventory.FreeSlots;
        InventoryFilledSpace = Inventory.UsedSlots;
        InventorySpace = Inventory.Slots;
        BankFreeSpace = Bank.FreeSlots;
        BankFilledSpace = Bank.UsedSlots;
        BankSpace = Bank.Slots;
    }

    /// <summary>
    /// Updates live gameplay metrics from the current player snapshot. Positive XP and gold changes are counted as gains;
    /// spending is excluded from the gold rate. Counter deltas come from game events and therefore work without a script.
    /// </summary>
    public void UpdateGameplaySnapshot(int experience, int requiredExperience, int level, int gold, bool connected)
    {
        if (!connected)
        {
            _sessionStopwatch.Stop();
            _hasGameplaySnapshot = false;
            SessionTime = _sessionStopwatch.Elapsed;
            return;
        }

        if (!_sessionStopwatch.IsRunning)
            _sessionStopwatch.Start();

        if (!_hasGameplaySnapshot)
        {
            SetGameplayBaseline(experience, requiredExperience, level, gold);
            return;
        }

        int experienceGained = 0;
        if (level == _lastLevel)
        {
            experienceGained = Math.Max(0, experience - _lastExperience);
        }
        else if (level > _lastLevel)
        {
            // intExpToLevel is the current level's XP requirement; carry the remainder across level-ups.
            experienceGained = Math.Max(0, _lastRequiredExperience - _lastExperience) + Math.Max(0, experience);
        }

        if (experienceGained > 0)
            _earnedExperience += experienceGained;

        if (gold > _lastGold)
            _earnedGold += gold - _lastGold;

        _sessionKills += Math.Max(0, Kills - _lastKills);
        _sessionDeaths += Math.Max(0, Deaths - _lastDeaths);
        _sessionItems += Math.Max(0, Drops - _lastDrops);

        SetGameplayBaseline(experience, requiredExperience, level, gold);

        TimeSpan elapsed = _sessionStopwatch.Elapsed;
        SessionTime = elapsed;

        if (elapsed.TotalSeconds < 5)
            return;

        double hours = elapsed.TotalHours;
        double minutes = elapsed.TotalMinutes;
        ExperiencePerHour = (int)Math.Round(_earnedExperience / hours);
        GoldPerHour = (int)Math.Round(_earnedGold / hours);
        KillsPerMinute = _sessionKills / minutes;
        DeathsPerMinute = _sessionDeaths / minutes;
        ItemsPerHour = (int)Math.Round(_sessionItems / hours);

        int experienceRemaining = Math.Max(0, requiredExperience - experience);
        TimeToLevel = ExperiencePerHour > 0
            ? TimeSpan.FromHours(experienceRemaining / (double)ExperiencePerHour)
            : null;
    }

    private void SetGameplayBaseline(int experience, int requiredExperience, int level, int gold)
    {
        _lastExperience = experience;
        _lastRequiredExperience = requiredExperience;
        _lastLevel = level;
        _lastGold = gold;
        _lastKills = Kills;
        _lastDeaths = Deaths;
        _lastDrops = Drops;
        _hasGameplaySnapshot = true;
    }

    private void ResetGameplayMetrics()
    {
        _sessionStopwatch.Reset();
        _hasGameplaySnapshot = false;
        _sessionKills = 0;
        _sessionDeaths = 0;
        _sessionItems = 0;
        _earnedExperience = 0;
        _earnedGold = 0;
        SessionTime = TimeSpan.Zero;
        ExperiencePerHour = 0;
        GoldPerHour = 0;
        KillsPerMinute = 0;
        DeathsPerMinute = 0;
        ItemsPerHour = 0;
        TimeToLevel = null;
    }

    private async Task HandleTimerAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(token))
                Time = _sw.Elapsed;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected during application shutdown.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _ctsTimer.Cancel();
        await _taskTimer;
        _timer.Dispose();
        _ctsTimer.Dispose();
        GC.SuppressFinalize(this);
    }
}