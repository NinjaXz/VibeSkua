using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using System.Diagnostics;

namespace Skua.Core.Scripts;

public partial class ScriptBotStats : ObservableObject, IScriptBotStats, IAsyncDisposable
{
    private readonly Lazy<IScriptInventory> _lazyInventory;
    private readonly Lazy<IScriptBank> _lazyBank;
    private readonly object _metricsLock = new();

    private IScriptInventory Inventory => _lazyInventory.Value;
    private IScriptBank Bank => _lazyBank.Value;

    private const int ClassPointsForRankTen = 302_500;

    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly Stopwatch _sessionStopwatch = new();
    private readonly Stopwatch _classStopwatch = new();
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
    private int _lastClassId;
    private int _lastClassPoints;
    private int _lastClassPointsToRank;
    private int _lastClassRank;
    private long _earnedClassPoints;

    // Cumulative point thresholds for class ranks 1-10, with rank 10 at 302,500 CP.
    private static readonly int[] ClassRankCumulativePoints = [0, 900, 3_600, 10_000, 22_500, 44_100, 78_400, 129_600, 202_500, 302_500];
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
    private double _experienceProgress;

    [ObservableProperty]
    private string _experienceProgressText = "0 / 0 XP";

    [ObservableProperty]
    private double _classRank10Progress;

    [ObservableProperty]
    private string _classRank10ProgressText = $"0 / {ClassPointsForRankTen:N0} CP";

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
    private int _currentClassPoints;

    [ObservableProperty]
    private int _currentClassRank;

    [ObservableProperty]
    private int _classPointsPerHour;

    [ObservableProperty]
    private TimeSpan? _timeToLevel;

    [ObservableProperty]
    private TimeSpan? _timeToClassRank10;

    public void Reset()
    {
        lock (_metricsLock)
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
    /// Updates live gameplay metrics from the current player snapshot. Positive XP deltas and net gold balance changes,
    /// including spending, are counted. Game-event counters work without a running script.
    /// </summary>
    public void UpdateGameplaySnapshot(int experience, int requiredExperience, int level, int gold, int classId, int classPoints, int classPointsToRank, int classRank, bool connected)
    {
        lock (_metricsLock)
        {
            if (!connected)
            {
                _sessionStopwatch.Stop();
                _classStopwatch.Stop();
                _hasGameplaySnapshot = false;
                SessionTime = _sessionStopwatch.Elapsed;
                return;
            }
    
            if (!_sessionStopwatch.IsRunning)
                _sessionStopwatch.Start();
    
            ExperienceProgress = requiredExperience > 0
                ? Math.Clamp(experience / (double)requiredExperience * 100d, 0d, 100d)
                : 0d;
            ExperienceProgressText = $"{Math.Max(0, experience):N0} / {Math.Max(0, requiredExperience):N0} XP";

            if (!_hasGameplaySnapshot)
            {
                SetGameplayBaseline(experience, requiredExperience, level, gold);
                UpdateClassMetrics(classId, classPoints, classPointsToRank, classRank);
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
    
            _earnedGold += (long)gold - _lastGold;
    
            _sessionKills += Math.Max(0, Kills - _lastKills);
            _sessionDeaths += Math.Max(0, Deaths - _lastDeaths);
            SetGameplayBaseline(experience, requiredExperience, level, gold);
            UpdateClassMetrics(classId, classPoints, classPointsToRank, classRank);
    
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
            TimeToLevel = ExperiencePerHour > 0 && requiredExperience > experience
                ? TimeSpan.FromHours(experienceRemaining / (double)ExperiencePerHour)
                : null;
        }
    }

    private void UpdateClassMetrics(int classId, int classPoints, int classPointsToRank, int classRank)
    {
        if (classId <= 0)
        {
            _classStopwatch.Stop();
            _lastClassId = 0;
            _lastClassPoints = 0;
            _lastClassPointsToRank = 0;
            _lastClassRank = 0;
            CurrentClassPoints = 0;
            CurrentClassRank = 0;
            ClassRank10Progress = 0;
            ClassRank10ProgressText = $"0 / {ClassPointsForRankTen:N0} CP";
            ClassPointsPerHour = 0;
            TimeToClassRank10 = null;
            return;
        }

        if (_lastClassId != classId || !_classStopwatch.IsRunning && _classStopwatch.Elapsed == TimeSpan.Zero)
        {
            _classStopwatch.Restart();
            _lastClassId = classId;
            _lastClassPoints = classPoints;
            _lastClassPointsToRank = classPointsToRank;
            _lastClassRank = classRank;
            _earnedClassPoints = 0;
            ClassPointsPerHour = 0;
            TimeToClassRank10 = classRank >= 10 ? TimeSpan.Zero : null;
        }
        else if (!_classStopwatch.IsRunning)
        {
            // Resume connected-time tracking without counting class points gained while disconnected.
            _classStopwatch.Start();
            _lastClassPoints = classPoints;
            _lastClassPointsToRank = classPointsToRank;
            _lastClassRank = classRank;
        }
        else
        {
            if (classRank == _lastClassRank)
            {
                _earnedClassPoints += Math.Max(0, classPoints - _lastClassPoints);
            }
            else if (classRank > _lastClassRank)
            {
                int previousRankRequirement = _lastClassPointsToRank > 0
                    ? _lastClassPointsToRank
                    : GetClassPointsRequiredForRank(_lastClassRank);
                _earnedClassPoints += Math.Max(0, previousRankRequirement - _lastClassPoints);

                for (int rank = _lastClassRank + 1; rank < classRank; rank++)
                    _earnedClassPoints += GetClassPointsRequiredForRank(rank);

                _earnedClassPoints += Math.Max(0, classPoints);
            }

            _lastClassPoints = classPoints;
            _lastClassPointsToRank = classPointsToRank;
            _lastClassRank = classRank;
        }

        CurrentClassRank = Math.Max(0, classRank);
        CurrentClassPoints = GetClassRankBasePoints(classRank) + Math.Max(0, classPoints);
        ClassRank10Progress = Math.Clamp(CurrentClassPoints / (double)ClassPointsForRankTen * 100d, 0d, 100d);
        ClassRank10ProgressText = $"{Math.Min(CurrentClassPoints, ClassPointsForRankTen):N0} / {ClassPointsForRankTen:N0} CP";

        TimeSpan classElapsed = _classStopwatch.Elapsed;
        if (classElapsed.TotalSeconds < 5)
            return;

        ClassPointsPerHour = (int)Math.Round(_earnedClassPoints / classElapsed.TotalHours);
        int pointsRemaining = classRank >= 10 ? 0 : Math.Max(0, ClassPointsForRankTen - CurrentClassPoints);
        TimeToClassRank10 = classRank >= 10
            ? TimeSpan.Zero
            : ClassPointsPerHour > 0
                ? TimeSpan.FromHours(pointsRemaining / (double)ClassPointsPerHour)
                : null;
    }

    private static int GetClassRankBasePoints(int rank)
    {
        int index = Math.Clamp(rank - 1, 0, ClassRankCumulativePoints.Length - 1);
        return ClassRankCumulativePoints[index];
    }

    private static int GetClassPointsRequiredForRank(int rank)
    {
        if (rank < 1 || rank >= ClassRankCumulativePoints.Length)
            return 0;

        return ClassRankCumulativePoints[rank] - ClassRankCumulativePoints[rank - 1];
    }

    /// <summary>
    /// Records item quantity only after the server confirms a successful pickup. This avoids counting inventory
    /// refreshes and the separate addItems/getDrop notifications as multiple pickups.
    /// </summary>
    public void RecordItemPickup(int quantity)
    {
        if (quantity <= 0)
            return;

        lock (_metricsLock)
            _sessionItems += quantity;
    }

    private void SetGameplayBaseline(int experience, int requiredExperience, int level, int gold)
    {
        _lastExperience = experience;
        _lastRequiredExperience = requiredExperience;
        _lastLevel = level;
        _lastGold = gold;
        _lastKills = Kills;
        _lastDeaths = Deaths;
        _hasGameplaySnapshot = true;
    }

    private void ResetGameplayMetrics()
    {
        _sessionStopwatch.Reset();
        _classStopwatch.Reset();
        _hasGameplaySnapshot = false;
        _lastClassId = 0;
        _lastClassPoints = 0;
        _lastClassPointsToRank = 0;
        _lastClassRank = 0;
        _earnedClassPoints = 0;
        _sessionKills = 0;
        _sessionDeaths = 0;
        _sessionItems = 0;
        _earnedExperience = 0;
        _earnedGold = 0;
        SessionTime = TimeSpan.Zero;
        ExperienceProgress = 0;
        ExperienceProgressText = "0 / 0 XP";
        ClassRank10Progress = 0;
        ClassRank10ProgressText = $"0 / {ClassPointsForRankTen:N0} CP";
        ExperiencePerHour = 0;
        GoldPerHour = 0;
        KillsPerMinute = 0;
        DeathsPerMinute = 0;
        ItemsPerHour = 0;
        CurrentClassPoints = 0;
        CurrentClassRank = 0;
        ClassPointsPerHour = 0;
        TimeToLevel = null;
        TimeToClassRank10 = null;
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