using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Skua.Core.Interfaces;

namespace Skua.App.Avalonia.ViewModels;

public class ScriptStatsViewModel : ObservableObject, IManagedWindow
{
    private readonly ISettingsService _settings;
    private bool _advancedMode;

    public ScriptStatsViewModel(IScriptBotStats scriptStats, ISettingsService settings)
    {
        ScriptStats = scriptStats;
        _settings = settings;
        _advancedMode = settings.Get("StatsAdvancedMode", false);
        ResetStatsCommand = new RelayCommand(ScriptStats.Reset);
        GetSpaceCommand = new RelayCommand(ScriptStats.GetSpace);
    }

    public IScriptBotStats ScriptStats { get; }
    public IRelayCommand ResetStatsCommand { get; }
    public IRelayCommand GetSpaceCommand { get; }

    public bool AdvancedMode
    {
        get => _advancedMode;
        set
        {
            if (SetProperty(ref _advancedMode, value))
                _settings.Set("StatsAdvancedMode", value);
        }
    }

    public string Title => "Stats";
    public int Width => 480;
    public int Height => 0;
    public bool CanResize => false;
}