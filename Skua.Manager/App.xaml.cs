using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Utils;
using Skua.Core.ViewModels;
using Skua.Core.ViewModels.Manager;
using Skua.WPF.Services;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Skua.Manager;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string _uniqueEventName = "Skua.Manager";
    private EventWaitHandle? _eventWaitHandle = null;

    public App()
    {

        Services = ConfigureServices();
        Services.GetRequiredService<ISettingsService>().SetApplicationVersion();
        FlashTrustManager.EnsureTrustFile();

        InitializeComponent();
        SingleInstanceWatcher();

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string targetPath = Path.Combine(appData, "Skua");

        Services.GetRequiredService<IClientFilesService>().CreateDirectories();
        Services.GetRequiredService<IClientFilesService>().CreateFiles();

        _ = Services.GetRequiredService<IThemeService>();
        ISettingsService settings = Services.GetRequiredService<ISettingsService>();
        IGetScriptsService getScripts = Services.GetRequiredService<IGetScriptsService>();

        Dispatcher.ShutdownStarted += Dispatcher_ShutdownStarted;
        StrongReferenceMessenger.Default.Register<App, UpdateFinishedMessage>(this, CloseManager);

        if (settings.Get<bool>("CheckClientUpdates"))
        {
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500);
                    AppUpdaterViewModel updateVM = Ioc.Default.GetRequiredService<AppUpdaterViewModel>();
                    await updateVM.CheckForUpdateCommand.ExecuteAsync(null);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"Client update check failed: {ex}");
                }
            });
        }

        if (settings.Get<bool>("CheckBotScriptsUpdates"))
        {
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000);
                    await getScripts.GetScriptsAsync(null, default);

                    if ((getScripts.Missing > 0 || getScripts.Outdated > 0) && settings.Get<bool>("AutoUpdateBotScripts"))
                    {
                        int count = await getScripts.DownloadAllWhereAsync(s => !s.Downloaded || s.Outdated);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine($"Script update check failed: {ex}");
                }
            });
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool isChangeLogActivated = Services.GetRequiredService<ISettingsService>().Get<bool>("ChangeLogActivated");
        if (!isChangeLogActivated)
        {
            Ioc.Default.GetRequiredService<IWindowService>().ShowWindow<ChangeLogsViewModel>(600, 700);
            Services.GetRequiredService<ISettingsService>().Set("ChangeLogActivated", true);
        }
    }

    private void SingleInstanceWatcher()
    {
        try
        {
            _eventWaitHandle = EventWaitHandle.OpenExisting(_uniqueEventName);
            _eventWaitHandle.Set();
            Shutdown();
            return;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            _eventWaitHandle = new EventWaitHandle(false, EventResetMode.AutoReset, _uniqueEventName);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"SingleInstanceWatcher failed: {ex}");
            return;
        }

        Task.Factory.StartNew(() =>
        {
            while (_eventWaitHandle?.WaitOne() == true)
            {
                Current.Dispatcher.BeginInvoke(() =>
                {
                    CommunityToolkit.Mvvm.Messaging.StrongReferenceMessenger.Default.Send(new Skua.Core.Messaging.ShowMainWindowMessage());
                });
            }
        }, TaskCreationOptions.LongRunning);
    }

    private void Dispatcher_ShutdownStarted(object? sender, EventArgs e)
    {
        StrongReferenceMessenger.Default.Reset();
    }

    private void CloseManager(App recipient, UpdateFinishedMessage message)
    {
        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
    }

    public static new App Current => (App)Application.Current;

    public IServiceProvider Services { get; }

    private IServiceProvider ConfigureServices()
    {
        IServiceCollection services = new ServiceCollection();

        services.AddWindowsServices();

        services.AddCommonServices();

        services.AddSkuaManagerViewModels();

        services.AddSingleton<ISettingsService, SettingsService>();

        ServiceProvider provider = services.BuildServiceProvider();
        Ioc.Default.ConfigureServices(provider);

        return provider;
    }
}