using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Interfaces.Services;
using Skua.Core.Options;
using Skua.Core.Scripts;
using Skua.Core.Services;
using Skua.Manager.Avalonia.AppStartup;
using Skua.Manager.Avalonia.Services;
using Skua.Manager.Avalonia.ViewModels;
using Skua.Shared.Avalonia.Services;
using Skua.Shared.Avalonia.Controls.Theming;
using System;

namespace Skua.Manager.Avalonia;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();

        services.AddCommonServices();
        services.AddTransient(Skua.Core.AppStartup.Services.CreateCompiler);
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton(new Lazy<IScriptInterface>(() => null!));
        services.AddSingleton(new Lazy<IScriptHandlers>(() => null!));
        services.AddSingleton(new Lazy<IScriptSkill>(() => null!));
        services.AddSingleton(new Lazy<IScriptDrop>(() => null!));
        services.AddSingleton(new Lazy<IScriptWait>(() => null!));
        services.AddSingleton(new Lazy<IAuraMonitorService>(() => null!));
        services.AddSingleton<IScriptManager, ScriptManager>();
        services.AddTransient<IScriptOptionContainer, ScriptOptionContainer>();
        services.AddTransient<IOptionContainer, OptionContainer>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDispatcherService, DispatcherService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<IClientFilesService, ClientFilesService>();
        services.AddSingleton<IThemeService, Skua.Shared.Avalonia.Services.AvaloniaThemeService>();
        services.AddSingleton<BackgroundThemeService>();
        services.AddSkuaManagerViewModels();

        Ioc.Default.ConfigureServices(services.BuildServiceProvider());
        Ioc.Default.GetRequiredService<IDiagnosticsService>().Start();
        Ioc.Default.GetRequiredService<ISettingsService>().SetApplicationVersion();
        ColorSpectrumFix.Initialize();
        IThemeService themeService = Ioc.Default.GetRequiredService<IThemeService>();
        themeService.ThemeChanged += OnThemeChanged;
        themeService.SchemeChanged += OnSchemeChanged;
        ApplyThemeFromService(themeService);

        IClientFilesService clientFiles = Ioc.Default.GetRequiredService<IClientFilesService>();
        clientFiles.CreateDirectories();
        clientFiles.CreateFiles();

        System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                IGetScriptsService? getScripts = Ioc.Default.GetService<IGetScriptsService>();
                if (getScripts is null)
                    return;

                await getScripts.GetScriptsAsync(null, default);

                ISettingsService settings = Ioc.Default.GetRequiredService<ISettingsService>();
                if (settings.Get<bool>("CheckBotScriptsUpdates") && settings.Get<bool>("AutoUpdateBotScripts")
                    && (getScripts.Missing > 0 || getScripts.Outdated > 0))
                {
                    await ScriptStartupUpdater.DownloadMissingAndOutdatedAsync(getScripts, Ioc.Default.GetRequiredService<IDialogService>());
                }
            }
            catch { }
        });

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Startup += OnStartup;
            desktop.Exit += OnDesktopExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void OnStartup(object? sender, ControlledApplicationLifetimeStartupEventArgs e)
    {
        var mainWindow = new Views.MainWindow();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = mainWindow;
        mainWindow.Show();

        if (Ioc.Default.GetRequiredService<ISettingsService>().Get("CheckClientUpdates", true))
            _ = Ioc.Default.GetRequiredService<ClientUpdatesViewModel>().Refresh();
    }

    private async void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        try
        {
            if (Ioc.Default.GetService<IThemeService>() is AvaloniaThemeService ats)
                ats.FlushPendingSave();
        }
        catch { }

        await Ioc.Default.GetRequiredService<IDiagnosticsService>().DisposeAsync();
    }

    // Full theme changes can include base theme + multiple palette slots, so we always
    // republish the complete resource map here (see ThemeResourceApplicator).
    private void OnThemeChanged(object? theme)
    {
        IThemeService themeService = Ioc.Default.GetRequiredService<IThemeService>();
        ThemeVariant targetVariant = themeService.IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        if (RequestedThemeVariant != targetVariant)
            RequestedThemeVariant = targetVariant;

        ThemeResourceApplicator.ApplyTitleBarTheme(this, themeService.IsDarkTheme);
        if (themeService is AvaloniaThemeService ats)
        {
            ThemeResourceApplicator.ApplyAccentBrushes(
                this,
                ats.PrimaryColor,
                ats.PrimaryForegroundColor,
                ats.SecondaryColor,
                ats.SecondaryForegroundColor,
                isDarkTheme: themeService.IsDarkTheme);
        }
        else
        {
            string? accentHex = theme?.ToString();
            ThemeResourceApplicator.ApplyAccentBrushes(this, accentHex, isDarkTheme: themeService.IsDarkTheme);
        }
    }

    // Single-scheme edits still republish all known colors when AvaloniaThemeService is active.
    // This keeps Material and Skua resources in lockstep and prevents partial updates.
    private void OnSchemeChanged(Core.Models.ColorScheme scheme, object? color)
    {
        IThemeService themeService = Ioc.Default.GetRequiredService<IThemeService>();
        bool isDark = themeService.IsDarkTheme;
        ThemeVariant targetVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        if (RequestedThemeVariant != targetVariant)
            RequestedThemeVariant = targetVariant;

        if (themeService is AvaloniaThemeService ats)
        {
            ThemeResourceApplicator.ApplyAccentBrushes(
                this,
                ats.PrimaryColor,
                ats.PrimaryForegroundColor,
                ats.SecondaryColor,
                ats.SecondaryForegroundColor,
                isDarkTheme: isDark);
            return;
        }

        // Non-AvaloniaThemeService fallback can only target one path at a time.
        if (scheme == Core.Models.ColorScheme.PrimaryForeground)
            ThemeResourceApplicator.ApplyAccentBrushes(this, accentForegroundHex: color?.ToString(), isDarkTheme: isDark);
        else
            ThemeResourceApplicator.ApplyAccentBrushes(this, accentHex: color?.ToString(), isDarkTheme: isDark);
    }

    // Apply theme resources before first window render so root styles and previews do not
    // flash default colors on startup.
    private void ApplyThemeFromService(IThemeService themeService)
    {
        bool isDark = themeService.IsDarkTheme;
        RequestedThemeVariant = isDark ? ThemeVariant.Dark : ThemeVariant.Light;
        ThemeResourceApplicator.ApplyTitleBarTheme(this, isDark);
        if (themeService is AvaloniaThemeService ats)
        {
            ThemeResourceApplicator.ApplyAccentBrushes(
                this,
                ats.PrimaryColor,
                ats.PrimaryForegroundColor,
                ats.SecondaryColor,
                ats.SecondaryForegroundColor,
                isDarkTheme: isDark);
            return;
        }

        string? accentHex = themeService.SelectedColor?.ToString();
        ThemeResourceApplicator.ApplyAccentBrushes(this, accentHex, isDarkTheme: isDark);
    }

    private void TrayShowManager_Click(object? sender, EventArgs e)
    {
        StrongReferenceMessenger.Default.Send<ShowMainWindowMessage>();
    }

    private void TrayIcon_Clicked(object? sender, EventArgs e)
    {
        StrongReferenceMessenger.Default.Send<ShowMainWindowMessage>();
    }

    private void TrayLaunchClient_Click(object? sender, EventArgs e)
    {
        LauncherViewModel launcher = Ioc.Default.GetRequiredService<LauncherViewModel>();
        _ = launcher.LaunchSkua();
    }

    private void TrayUpdateScripts_Click(object? sender, EventArgs e)
    {
        StrongReferenceMessenger.Default.Send<UpdateScriptsMessage>(new(false));
    }

    private void TrayResetScripts_Click(object? sender, EventArgs e)
    {
        StrongReferenceMessenger.Default.Send<UpdateScriptsMessage>(new(true));
    }

    private void TrayCheckUpdate_Click(object? sender, EventArgs e)
    {
        StrongReferenceMessenger.Default.Send<CheckClientUpdateMessage>();
    }

    private async void TrayExit_Click(object? sender, EventArgs e)
    {
        LauncherViewModel launcher = Ioc.Default.GetRequiredService<LauncherViewModel>();
        // Wait for the clients to close themselves (or be killed) before the manager exits.
        await launcher.KillAllSkuaProcesses();
        StrongReferenceMessenger.Default.Send<ExitManagerMessage>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
