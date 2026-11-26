using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Skua.App.Avalonia.ViewModels;
using Skua.Shared.Avalonia.ViewModels;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Services;

public sealed class SkuaStartupHandler : IDisposable
{
    private readonly IScriptInterface _bot;
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly StartupOptions _options;
    private readonly bool _optionsParsed;
    private bool _clientLoadRequested;
    private bool _loginTriggered;
    private bool _disposed;

    public SkuaStartupHandler(IScriptInterface bot, ISettingsService settingsService, IThemeService themeService)
    {
        _bot = bot;
        _settingsService = settingsService;
        _themeService = themeService;
        (_optionsParsed, _options) = StartupOptions.Parse(Environment.GetCommandLineArgs());
    }

    public void Execute()
    {
        _bot.Flash.FlashCall -= LoadGame;
        _bot.Flash.FlashCall += LoadGame;
        _bot.Flash.FlashCall -= Login;

        if (!_optionsParsed)
            return;

        if (!string.IsNullOrWhiteSpace(_options.GitHubToken))
            _settingsService.Set("UserGitHubToken", _options.GitHubToken!);

        if (!string.IsNullOrWhiteSpace(_options.UseTheme) && !string.Equals(_options.UseTheme, "no-theme", StringComparison.OrdinalIgnoreCase))
            _themeService.SetCurrentTheme(_options.UseTheme!);

        string? targetScript = !string.IsNullOrWhiteSpace(_options.Script) ? _options.Script : _options.LoadScript;
        if (!string.IsNullOrWhiteSpace(targetScript))
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var scriptLoader = Ioc.Default.GetRequiredService<ScriptLoaderViewModel>();
                    scriptLoader.LoadScript(targetScript);
                }
                catch { }
            });
        }

        if (!string.IsNullOrWhiteSpace(_options.Username))
        {
            _bot.Servers.SetLoginInfo(_options.Username!, _options.Password ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(_options.Password))
                _bot.Flash.FlashCall += Login;
        }

        if (_options.ExitOnScriptStop)
        {
            if (!string.IsNullOrWhiteSpace(_options.RunPlaylist))
            {
                StrongReferenceMessenger.Default.Register<SkuaStartupHandler, PlaylistFinishedMessage, int>(this, (int)MessageChannels.ScriptStatus, async (r, m) =>
                {
                    await Task.Delay(2000);
                    try { _bot.Servers.Logout(); } catch { }
                    await Task.Delay(1000);
                    Environment.Exit(0);
                });
            }
            else
            {
                StrongReferenceMessenger.Default.Register<SkuaStartupHandler, ScriptStoppedMessage, int>(this, (int)MessageChannels.ScriptStatus, async (r, m) =>
                {
                    await Task.Delay(2000);
                    try { _bot.Servers.Logout(); } catch { }
                    await Task.Delay(1000);
                    Environment.Exit(0);
                });
            }
        }
    }

    private void LoadGame(string function, params object[] args)
    {
        if (function != "requestLoadGame" || _clientLoadRequested)
            return;

        _clientLoadRequested = true;
        _bot.Flash.Call("loadClient");
        _bot.Flash.FlashCall -= LoadGame;
    }

    private void Login(string function, params object[] args)
    {
        if (_loginTriggered || function != "loaded")
            return;

        _loginTriggered = true;
        _bot.Flash.FlashCall -= Login;

        Task.Factory.StartNew(async () =>
        {
            _bot.Servers.EnsureRelogin(string.IsNullOrWhiteSpace(_options.Server) ? "Twilly" : _options.Server!);

            // Wait for player to be in-game and world to load (up to 60 seconds)
            int wait = 0;
            while (!_bot.Player.Playing && wait < 240)
            {
                await Task.Delay(250);
                wait++;
            }

            if (!_bot.Player.Playing)
            {
                if (_options.ExitOnScriptStop)
                {
                    System.Diagnostics.Trace.WriteLine("[SkuaStartupHandler] Login timed out (could not enter world in 60s). Exiting with code 101.");
                    try { _bot.Servers.Logout(); } catch { }
                    await Task.Delay(500);
                    Environment.Exit(101);
                }
                return;
            }

            await Task.Delay(1000);

            if (!string.IsNullOrWhiteSpace(_options.RunPlaylist))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        var scheduler = Ioc.Default.GetRequiredService<ScriptSchedulerViewModel>();
                        if (scheduler.LoadPlaylistFromFile(_options.RunPlaylist))
                        {
                            scheduler.StartQueueDirect();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.WriteLine($"Failed to launch startup playlist: {ex.Message}");
                    }
                });
            }
            else if (!string.IsNullOrWhiteSpace(_options.Script))
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        var scriptLoader = Ioc.Default.GetRequiredService<ScriptLoaderViewModel>();
                        if (!string.IsNullOrWhiteSpace(_options.ScriptOptionsStorage))
                        {
                            scriptLoader.ScriptManager.OverrideStorage = _options.ScriptOptionsStorage;
                            scriptLoader.ScriptManager.SilentConfig = true;
                        }
                        scriptLoader.LoadScript(_options.Script);
                        await scriptLoader.StartScriptAsync(_options.Script);
                    }
                    catch { }
                });
            }
            else if (!string.IsNullOrWhiteSpace(_options.LoadScript))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    try
                    {
                        var scriptLoader = Ioc.Default.GetRequiredService<ScriptLoaderViewModel>();
                        scriptLoader.LoadScript(_options.LoadScript);
                    }
                    catch { }
                });
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _bot.Flash.FlashCall -= LoadGame;
        _bot.Flash.FlashCall -= Login;
        StrongReferenceMessenger.Default.Unregister<ScriptStoppedMessage, int>(this, (int)MessageChannels.ScriptStatus);
        StrongReferenceMessenger.Default.Unregister<PlaylistFinishedMessage, int>(this, (int)MessageChannels.ScriptStatus);
        GC.SuppressFinalize(this);
    }

    private sealed class StartupOptions
    {
        public string? Username { get; private set; }
        public string? Password { get; private set; }
        public string? Server { get; private set; } = "Twilly";
        public string? Script { get; private set; }
        public string? RunPlaylist { get; private set; }
        public string? ScriptOptionsStorage { get; private set; }
        public string? LoadScript { get; private set; }
        public string? UseTheme { get; private set; }
        public string? GitHubToken { get; private set; }
        public bool ExitOnScriptStop { get; private set; }

        public static (bool Parsed, StartupOptions Options) Parse(string[] args)
        {
            StartupOptions options = new();
            if (args.Length <= 1)
                return (true, options);

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("-"))
                    continue;

                string? NextValue()
                {
                    if (i + 1 >= args.Length)
                        return null;
                    i++;
                    return args[i];
                }

                switch (arg)
                {
                    case "-u":
                    case "--user":
                        options.Username = NextValue();
                        break;
                    case "-p":
                    case "--password":
                        options.Password = NextValue();
                        break;
                    case "-s":
                    case "--server":
                        options.Server = NextValue() ?? options.Server;
                        break;
                    case "--run-script":
                        options.Script = NextValue();
                        break;
                    case "--run-playlist":
                        options.RunPlaylist = NextValue();
                        break;
                    case "--script-options-storage":
                        options.ScriptOptionsStorage = NextValue();
                        break;
                    case "--load-script":
                        options.LoadScript = NextValue();
                        break;
                    case "--use-theme":
                        options.UseTheme = NextValue();
                        break;
                    case "--gh-token":
                        options.GitHubToken = NextValue();
                        break;
                    case "--exit-on-script-stop":
                    case "--exit-on-finish":
                        options.ExitOnScriptStop = true;
                        break;
                    case "--embed":
                    case "--host-pid":
                        NextValue();
                        break;
                }
            }

            return (true, options);
        }
    }
}
