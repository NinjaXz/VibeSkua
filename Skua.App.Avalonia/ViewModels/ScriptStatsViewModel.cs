using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;

namespace Skua.App.Avalonia.ViewModels;

public class ScriptStatsViewModel : ObservableObject, IManagedWindow
{
    private static readonly string[] AvailableRateUnits = ["/hr", "/min", "/sec"];
    private const int MaximumRateSnapshots = 100;
    private const string HistoryKey = "StatsRateHistory";

    private readonly ISettingsService _settings;
    private readonly IFileDialogService _fileDialogService;
    private readonly IDialogService _dialogService;
    private readonly IScriptPlayer _player;
    private bool _advancedMode;
    private string _rateUnit;

    public ScriptStatsViewModel(IScriptBotStats scriptStats, ISettingsService settings, IFileDialogService fileDialogService, IDialogService dialogService, IScriptPlayer player)
    {
        ScriptStats = scriptStats;
        _settings = settings;
        _fileDialogService = fileDialogService;
        _dialogService = dialogService;
        _player = player;
        RateHistory = new ObservableCollection<StatsRateSnapshot>(
            settings.Get(HistoryKey, new List<StatsRateSnapshot>()).Take(MaximumRateSnapshots));
        _advancedMode = settings.Get("StatsAdvancedMode", false);

        string savedRateUnit = settings.Get("StatsRateUnit", "/hr");
        _rateUnit = AvailableRateUnits.Contains(savedRateUnit, StringComparer.Ordinal)
            ? savedRateUnit
            : "/hr";

        ResetStatsCommand = new RelayCommand(ResetStats);
        GetSpaceCommand = new RelayCommand(ScriptStats.GetSpace);
        SaveRateSnapshotCommand = new RelayCommand(SaveRateSnapshot);
        ExportHistoryCommand = new RelayCommand(ExportHistory, () => HasHistory);
        ClearRateHistoryCommand = new RelayCommand(ClearRateHistory, () => HasHistory);
        RateHistory.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasHistory));
            ExportHistoryCommand.NotifyCanExecuteChanged();
            ClearRateHistoryCommand.NotifyCanExecuteChanged();
        };
    }

    public IScriptBotStats ScriptStats { get; }
    public IRelayCommand ResetStatsCommand { get; }
    public IRelayCommand GetSpaceCommand { get; }
    public IRelayCommand SaveRateSnapshotCommand { get; }
    public IRelayCommand ExportHistoryCommand { get; }
    public IRelayCommand ClearRateHistoryCommand { get; }
    public ObservableCollection<StatsRateSnapshot> RateHistory { get; }
    public IReadOnlyList<string> RateUnits => AvailableRateUnits;
    public bool HasHistory => RateHistory.Count > 0;

    public bool AdvancedMode
    {
        get => _advancedMode;
        set
        {
            if (SetProperty(ref _advancedMode, value))
                _settings.Set("StatsAdvancedMode", value);
        }
    }

    public string RateUnit
    {
        get => _rateUnit;
        set
        {
            if (!AvailableRateUnits.Contains(value, StringComparer.Ordinal) || !SetProperty(ref _rateUnit, value))
                return;

            _settings.Set("StatsRateUnit", value);
            OnPropertyChanged(nameof(ExperienceRateLabel));
            OnPropertyChanged(nameof(GoldRateLabel));
            OnPropertyChanged(nameof(ItemsRateLabel));
            OnPropertyChanged(nameof(ClassPointsRateLabel));
            OnPropertyChanged(nameof(KillsRateLabel));
            OnPropertyChanged(nameof(DeathsRateLabel));
        }
    }

    private void ResetStats() => ScriptStats.Reset();

    /// <summary>
    /// Saves the currently displayed gameplay rates as a persistent, user-triggered history row.
    /// Values are stored in the selected unit so historical rows remain exactly as captured.
    /// </summary>
    private void SaveRateSnapshot()
    {
        StatsRateSnapshot snapshot = new(
            DateTimeOffset.Now,
            ScriptStats.SessionTime,
            RateUnit,
            ConvertHourlyRate(ScriptStats.ExperiencePerHour, RateUnit),
            ConvertHourlyRate(ScriptStats.GoldPerHour, RateUnit),
            ConvertPerMinuteRate(ScriptStats.KillsPerMinute, RateUnit),
            ConvertPerMinuteRate(ScriptStats.DeathsPerMinute, RateUnit),
            ConvertHourlyRate(ScriptStats.ItemsPerHour, RateUnit),
            ConvertHourlyRate(ScriptStats.ClassPointsPerHour, RateUnit),
            ScriptStats.CurrentClassRank)
        {
            Character = _player.LoggedIn ? _player.Username : null
        };

        // Every client writes this one shared list, so the reload, the insert and the save are one step under the
        // settings file lock: a snapshot another client saves at the same moment is kept, not overwritten.
        bool saved = _settings.TryUpdate(HistoryKey, new List<StatsRateSnapshot>(), history =>
        {
            List<StatsRateSnapshot> next = new(history.Count + 1) { snapshot };
            next.AddRange(history.Take(MaximumRateSnapshots - 1));
            return next;
        }, out List<StatsRateSnapshot> result);

        if (!saved)
        {
            _dialogService.ShowMessageBox(
                "Couldn't save the snapshot: the settings file couldn't be read or written. Nothing was changed; try again.",
                "Save Snapshot", yesAndNo: false);
            return;
        }
        ShowHistory(result);
    }

    /// <summary>
    /// Shows the history as saved now, including snapshots other clients saved or cleared since this window last
    /// looked. Keeps the table as it is when the settings file can't be read.
    /// </summary>
    public void RefreshHistory()
    {
        if (_settings.TryReloadSettings())
            ShowHistory(_settings.Get(HistoryKey, new List<StatsRateSnapshot>()));
    }

    private void ShowHistory(IEnumerable<StatsRateSnapshot> history)
    {
        List<StatsRateSnapshot> rows = history.Take(MaximumRateSnapshots).ToList();
        if (rows.SequenceEqual(RateHistory))
            return;

        RateHistory.Clear();
        foreach (StatsRateSnapshot snapshot in rows)
            RateHistory.Add(snapshot);
    }

    /// <summary>
    /// Deletes all manually saved rate snapshots after explicit user confirmation.
    /// </summary>
    private void ClearRateHistory()
    {
        if (RateHistory.Count == 0)
            return;

        bool? confirm = _dialogService.ShowMessageBox(
            $"Delete all {RateHistory.Count} saved rate snapshots? This clears them for every client and cannot be undone.",
            "Clear Rate History",
            yesAndNo: true);

        if (confirm != true)
            return;

        if (!_settings.TryUpdate(HistoryKey, new List<StatsRateSnapshot>(), _ => new List<StatsRateSnapshot>(), out List<StatsRateSnapshot> result))
        {
            _dialogService.ShowMessageBox(
                "Couldn't clear the history: the settings file couldn't be read or written. Nothing was changed; try again.",
                "Clear Rate History", yesAndNo: false);
            return;
        }
        ShowHistory(result);
    }

    private static double ConvertHourlyRate(double value, string unit) => unit switch
    {
        "/min" => value / 60d,
        "/sec" => value / 3600d,
        _ => value
    };

    private static double ConvertPerMinuteRate(double value, string unit) => unit switch
    {
        "/hr" => value * 60d,
        "/sec" => value / 60d,
        _ => value
    };

    private void ExportHistory()
    {
        // Export what is saved now, not what this window loaded earlier.
        RefreshHistory();
        if (RateHistory.Count == 0)
            return;

        System.Text.StringBuilder csv = new();
        csv.AppendLine("Saved At,Character,Session Seconds,Rate Unit,Experience Rate,Gold Rate,Kills Rate,Deaths Rate,Items Rate,Class Points Rate,Class Rank");
        foreach (StatsRateSnapshot snapshot in RateHistory)
        {
            csv.Append(snapshot.SavedAt.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(CsvField(snapshot.Character)).Append(',')
                .Append(snapshot.SessionDuration.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                .Append(snapshot.RateUnit).Append(',')
                .Append(snapshot.ExperienceRate.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(snapshot.GoldRate.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(snapshot.KillsRate.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(snapshot.DeathsRate.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(snapshot.ItemsRate.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .Append(snapshot.ClassPointsRate.ToString("F2", CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(snapshot.ClassRank.ToString(CultureInfo.InvariantCulture));
        }

        _fileDialogService.SaveText(csv.ToString());
    }

    private static string CsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.IndexOfAny([',', '"', '\n', '\r']) < 0 ? value : $"\"{value.Replace("\"", "\"\"")}\"";
    }

    public string ExperienceRateLabel => $"EXP {RateUnit}";
    public string GoldRateLabel => $"Gold {RateUnit}";
    public string ItemsRateLabel => $"Items {RateUnit}";
    public string ClassPointsRateLabel => $"CP {RateUnit}";
    public string KillsRateLabel => $"Kills {RateUnit}";
    public string DeathsRateLabel => $"Deaths {RateUnit}";

    public string Title => "Stats";
    public int Width => 480;
    public int Height => 0;
    public bool CanResize => true;
}
