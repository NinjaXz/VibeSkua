using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;

namespace Skua.Core.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "VibeSkua V1.8.3";

    private System.Timers.Timer? _titleUpdateTimer;
    private IScriptPlayer? _player;
    private ISettingsService? _settingsService;
    private string? _lastUsername;
    private bool _showUsernameInTitle;

    public bool ShowUsernameInTitle
    {
        get => _showUsernameInTitle;
        set
        {
            if (SetProperty(ref _showUsernameInTitle, value))
            {
                UpdateTitle();
            }
        }
    }

    public MainViewModel(IEnumerable<TabItemViewModel> tabs, IDialogService dialogService)
    {
        _title = "VibeSkua V1.8.3";
        Ioc.Default.GetRequiredService<IDiscordWebhookService>().Initialize();

        try
        {
            _player = Ioc.Default.GetService<IScriptPlayer>();
            _settingsService = Ioc.Default.GetService<ISettingsService>();

            if (_settingsService != null)
            {
                _showUsernameInTitle = _settingsService.Get<bool>("ShowUsernameInTitle");
            }
        }
        catch { }

        StartTitleTimer();
        UpdateTitle();
    }

    private void StartTitleTimer()
    {
        if (_titleUpdateTimer == null)
        {
            _titleUpdateTimer = new System.Timers.Timer(1000);
            _titleUpdateTimer.Elapsed += (s, e) => CheckAndUpdateTitle();
            _titleUpdateTimer.AutoReset = true;
            _titleUpdateTimer.Start();
        }
    }

    private string? GetPlayerUsername()
    {
        if (_player == null) return null;
        try
        {
            string? u = _player.Username;
            if (string.IsNullOrWhiteSpace(u) || u == "null" || u == "undefined")
            {
                var flash = Ioc.Default.GetService<IFlashUtil>();
                if (flash != null)
                {
                    u = flash.GetGameObject<string>("world.myAvatar.objData.strUsername");
                    if (u == "null" || u == "undefined") u = null;
                }
            }
            return u;
        }
        catch
        {
            return null;
        }
    }

    private void CheckAndUpdateTitle()
    {
        var dispatcher = Ioc.Default.GetService<IDispatcherService>();
        if (dispatcher == null)
        {
            DoCheckAndUpdate();
            return;
        }

        dispatcher.Invoke(DoCheckAndUpdate);

        void DoCheckAndUpdate()
        {
            if (_settingsService == null)
                return;

            try
            {
                bool showUsername = _settingsService.Get<bool>("ShowUsernameInTitle");
                string? currentUsername = GetPlayerUsername();

                if (showUsername != _showUsernameInTitle || currentUsername != _lastUsername)
                {
                    _showUsernameInTitle = showUsername;
                    UpdateTitle();
                }
            }
            catch { }
        }
    }

    public void UpdateTitle()
    {
        string? username = GetPlayerUsername();

        _lastUsername = username;

        string title = "VibeSkua V1.8.3";

        if (_showUsernameInTitle && !string.IsNullOrWhiteSpace(username))
            title += $" : {username}";

        Title = title;

        try
        {
            string dir = Path.Combine(Path.GetTempPath(), "SkuaTabs");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"{System.Diagnostics.Process.GetCurrentProcess().Id}.txt");
            File.WriteAllText(file, username ?? "");
        }
        catch { }
    }

    [RelayCommand]
    private void ShowMainWindow()
    {
        StrongReferenceMessenger.Default.Send<ShowMainWindowMessage>();
    }
}
