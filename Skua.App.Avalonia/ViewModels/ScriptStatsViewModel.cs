using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;

namespace Skua.App.Avalonia.ViewModels;

public class ScriptStatsViewModel : ObservableObject, IManagedWindow
{
    private static readonly string[] AvailableRateUnits = ["/hr", "/min", "/sec"];

    private readonly ISettingsService _settings;
    private bool _advancedMode;
    private string _rateUnit;

    public ScriptStatsViewModel(IScriptBotStats scriptStats, ISettingsService settings)
    {
        ScriptStats = scriptStats;
        _settings = settings;
        _advancedMode = settings.Get("StatsAdvancedMode", false);

        string savedRateUnit = settings.Get("StatsRateUnit", "/hr");
        _rateUnit = AvailableRateUnits.Contains(savedRateUnit, StringComparer.Ordinal)
            ? savedRateUnit
            : "/hr";

        ResetStatsCommand = new RelayCommand(ScriptStats.Reset);
        GetSpaceCommand = new RelayCommand(ScriptStats.GetSpace);
    }

    public IScriptBotStats ScriptStats { get; }
    public IRelayCommand ResetStatsCommand { get; }
    public IRelayCommand GetSpaceCommand { get; }
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

    public string ExperienceRateLabel => $"EXP {RateUnit.ToUpperInvariant()}";
    public string GoldRateLabel => $"NET GOLD {RateUnit.ToUpperInvariant()}";
    public string ItemsRateLabel => $"ITEMS {RateUnit.ToUpperInvariant()}";
    public string ClassPointsRateLabel => $"CLASS POINTS {RateUnit.ToUpperInvariant()}";

    public string Title => "Stats";
    public int Width => 480;
    public int Height => 0;
    public bool CanResize => false;
}