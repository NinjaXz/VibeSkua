using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;
using Skua.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;

namespace Skua.App.Avalonia.ViewModels;

public class ScriptStatsViewModel : ObservableObject, IManagedWindow
{
    private static readonly string[] AvailableRateUnits = ["/hr", "/min", "/sec"];

    private readonly ISettingsService _settings;
    private readonly IFileDialogService _fileDialogService;
    private bool _advancedMode;
    private string _rateUnit;

    public ScriptStatsViewModel(IScriptBotStats scriptStats, ISettingsService settings, IFileDialogService fileDialogService)
    {
        ScriptStats = scriptStats;
        _settings = settings;
        _fileDialogService = fileDialogService;
        StatsHistory = new ObservableCollection<StatsSessionRecord>(settings.Get("StatsSessionHistory", new System.Collections.Generic.List<StatsSessionRecord>()).Take(10));
        _advancedMode = settings.Get("StatsAdvancedMode", false);

        string savedRateUnit = settings.Get("StatsRateUnit", "/hr");
        _rateUnit = AvailableRateUnits.Contains(savedRateUnit, StringComparer.Ordinal)
            ? savedRateUnit
            : "/hr";

        ResetStatsCommand = new RelayCommand(ResetStats);
        GetSpaceCommand = new RelayCommand(ScriptStats.GetSpace);
        ExportHistoryCommand = new RelayCommand(ExportHistory);
    }

    public IScriptBotStats ScriptStats { get; }
    public IRelayCommand ResetStatsCommand { get; }
    public IRelayCommand GetSpaceCommand { get; }
    public IRelayCommand ExportHistoryCommand { get; }
    public ObservableCollection<StatsSessionRecord> StatsHistory { get; }
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

    private void ResetStats()
    {
        ScriptStats.Reset();
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        StatsHistory.Clear();
        foreach (StatsSessionRecord record in _settings.Get("StatsSessionHistory", new System.Collections.Generic.List<StatsSessionRecord>()).Take(10))
            StatsHistory.Add(record);
    }

    private void ExportHistory()
    {
        System.Collections.Generic.List<StatsSessionRecord> history = _settings.Get("StatsSessionHistory", new System.Collections.Generic.List<StatsSessionRecord>());
        if (history.Count == 0)
            return;

        System.Text.StringBuilder csv = new();
        csv.AppendLine("Ended At,Duration Seconds,Experience Gained,Net Gold Change,Kills,Deaths,Items Collected,Class Points Gained");
        foreach (StatsSessionRecord record in history)
        {
            csv.Append(record.EndedAt.ToString("O", CultureInfo.InvariantCulture)).Append(',')
                .Append(record.Duration.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)).Append(',')
                .Append(record.ExperienceGained.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(record.NetGoldChange.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(record.Kills.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(record.Deaths.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(record.ItemsCollected.ToString(CultureInfo.InvariantCulture)).Append(',')
                .AppendLine(record.ClassPointsGained.ToString(CultureInfo.InvariantCulture));
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
    public bool CanResize => false;
}