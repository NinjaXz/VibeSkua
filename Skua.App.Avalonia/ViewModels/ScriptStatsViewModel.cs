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

    private readonly ISettingsService _settings;
    private readonly IFileDialogService _fileDialogService;
    private readonly IDialogService _dialogService;
    private bool _advancedMode;
    private string _rateUnit;

    public ScriptStatsViewModel(IScriptBotStats scriptStats, ISettingsService settings, IFileDialogService fileDialogService, IDialogService dialogService)
    {
        ScriptStats = scriptStats;
        _settings = settings;
        _fileDialogService = fileDialogService;
        _dialogService = dialogService;
        RateHistory = new ObservableCollection<StatsRateSnapshot>(
            settings.Get("StatsRateHistory", new List<StatsRateSnapshot>()).Take(MaximumRateSnapshots));
        _advancedMode = settings.Get("StatsAdvancedMode", false);

        string savedRateUnit = settings.Get("StatsRateUnit", "/hr");
        _rateUnit = AvailableRateUnits.Contains(savedRateUnit, StringComparer.Ordinal)
            ? savedRateUnit
            : "/hr";

        ResetStatsCommand = new RelayCommand(ResetStats);
        GetSpaceCommand = new RelayCommand(ScriptStats.GetSpace);
        SaveRateSnapshotCommand = new RelayCommand(SaveRateSnapshot);
        ExportHistoryCommand = new RelayCommand(ExportHistory);
        ClearRateHistoryCommand = new RelayCommand(ClearRateHistory);
    }

    public IScriptBotStats ScriptStats { get; }
    public IRelayCommand ResetStatsCommand { get; }
    public IRelayCommand GetSpaceCommand { get; }
    public IRelayCommand SaveRateSnapshotCommand { get; }
    public IRelayCommand ExportHistoryCommand { get; }
    public IRelayCommand ClearRateHistoryCommand { get; }
    public ObservableCollection<StatsRateSnapshot> RateHistory { get; }
    public IReadOnlyList<string> RateUnits => AvailableRateUnits;

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
            ScriptStats.CurrentClassRank);

        RateHistory.Insert(0, snapshot);
        while (RateHistory.Count > MaximumRateSnapshots)
            RateHistory.RemoveAt(RateHistory.Count - 1);

        _settings.Set("StatsRateHistory", RateHistory.ToList());
    }

    /// <summary>
    /// Deletes all manually saved rate snapshots after explicit user confirmation.
    /// </summary>
    private void ClearRateHistory()
    {
        if (RateHistory.Count == 0)
            return;

        bool? confirm = _dialogService.ShowMessageBox(
            $"Delete all {RateHistory.Count} saved rate snapshots? This cannot be undone.",
            "Clear Rate History",
            yesAndNo: true);

        if (confirm != true)
            return;

        RateHistory.Clear();
        _settings.Set("StatsRateHistory", new List<StatsRateSnapshot>());
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
        if (RateHistory.Count == 0)
            return;

        System.Text.StringBuilder csv = new();
        csv.AppendLine("Saved At,Session Seconds,Rate Unit,Experience Rate,Gold Rate,Kills Rate,Deaths Rate,Items Rate,Class Points Rate,Class Rank");
        foreach (StatsRateSnapshot snapshot in RateHistory)
        {
            csv.Append(snapshot.SavedAt.ToString("O", CultureInfo.InvariantCulture)).Append(',')
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

    public string ExperienceRateLabel => $"EXP {RateUnit.ToUpperInvariant()}";
    public string GoldRateLabel => $"NET GOLD {RateUnit.ToUpperInvariant()}";
    public string ItemsRateLabel => $"ITEMS {RateUnit.ToUpperInvariant()}";
    public string ClassPointsRateLabel => $"CLASS POINTS {RateUnit.ToUpperInvariant()}";

    public string Title => "Stats";
    public int Width => 480;
    public int Height => 0;
    public bool CanResize => true;
}
