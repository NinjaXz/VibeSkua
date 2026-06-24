using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.App.WPF.Services;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;
using Skua.Core.Utils;
using Skua.WPF;
using Skua.WPF.Services;
using System;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Animation;
using Westwind.Scripting;
using Velopack;

namespace Skua.App.WPF;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public sealed partial class App : Application
{
    /// <summary>
    /// Gets the current <see cref="App"/> instance in use
    /// </summary>
    public static new App Current => (App)Application.Current;
    public static Mutex AppMutex;

    public IServiceProvider Services { get; private set; }
    private readonly IScriptInterface _bot;

    [DllImport("user32.dll", EntryPoint = "SetParent", SetLastError = true)]
    public static extern IntPtr SetParent_Native(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    public static extern int GetWindowLong_Native(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    public static extern int SetWindowLong_Native(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetClientRect")]
    public static extern bool GetClientRect_Native(IntPtr hWnd, out Skua.App.WPF.TabbedHostWindow.RECT lpRect);

    [DllImport("user32.dll", EntryPoint = "MoveWindow", SetLastError = true)]
    public static extern bool MoveWindow_Native(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr hProcess, int processInformationClass, ref PROCESS_POWER_THROTTLING_STATE processInformation, uint processInformationSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    public App()
    {
        try
        {
            System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.AboveNormal;
            
            PROCESS_POWER_THROTTLING_STATE powerThrottlingState = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = 1,
                ControlMask = 1, // PROCESS_POWER_THROTTLING_EXECUTION_SPEED
                StateMask = 0    // Disable
            };
            SetProcessInformation(System.Diagnostics.Process.GetCurrentProcess().Handle, 1 /* ProcessPowerThrottling */, ref powerThrottlingState, (uint)Marshal.SizeOf(typeof(PROCESS_POWER_THROTTLING_STATE)));
        }
        catch { }

        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        Services = ConfigureServices();
        Services.GetRequiredService<ISettingsService>().SetApplicationVersion();
        InitializeComponent();

        Task.Run(async () => 
        {
            await Task.Delay(1000);
            await Services.GetRequiredService<IScriptServers>().GetServers();
        });

        _bot = Services.GetRequiredService<IScriptInterface>();
        _ = Services.GetRequiredService<ILogService>();

        string[] args = Environment.GetCommandLineArgs();
        SkuaStartupHandler startup = new(args, _bot, Services.GetRequiredService<ISettingsService>(), Services.GetRequiredService<IThemeService>());
        startup.Execute();

        bool isEmbedded = args.Contains("--embed");

        if (!isEmbedded)
        {
            Task.Run(async () => 
            {
                await Task.Delay(1500);
                RoslynLifetimeManager.WarmupRoslyn();
            });
        }

        System.Timers.Timer globalGcTimer = new(TimeSpan.FromMinutes(1).TotalMilliseconds);
        globalGcTimer.Elapsed += (s, e) => 
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            try { Skua.Core.Utils.MemoryUtils.TrimWorkingSet(); } catch { }
        };
        globalGcTimer.Start();
        Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = Services.GetRequiredService<ISettingsService>().Get<int>("AnimationFrameRate") });

        Application.Current.Exit += App_Exit;
    }

    private async void App_Exit(object? sender, EventArgs e)
    {
        Services.GetRequiredService<ICaptureProxy>().Stop();

        await ((IAsyncDisposable)Services.GetRequiredService<IScriptBoost>()).DisposeAsync();
        await ((IAsyncDisposable)Services.GetRequiredService<IScriptBotStats>()).DisposeAsync();
        await ((IAsyncDisposable)Services.GetRequiredService<IScriptDrop>()).DisposeAsync();
        await Ioc.Default.GetRequiredService<IScriptManager>().StopScript();
        await ((IScriptInterfaceManager)_bot).StopTimerAsync();

        Services.GetRequiredService<IFlashUtil>().Dispose();

        WeakReferenceMessenger.Default.Cleanup();
        WeakReferenceMessenger.Default.Reset();
        StrongReferenceMessenger.Default.Reset();

        RoslynLifetimeManager.ShutdownRoslyn();
        Application.Current.Exit -= App_Exit;
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        if (!Directory.Exists(Path.Combine(AppContext.BaseDirectory, "VSCode")))
        {
            Services.GetRequiredService<ISettingsService>().Set("UseLocalVSC", false);
        }

        string[] args = Environment.GetCommandLineArgs();
        bool hasEmbedFlag = false;
        long embedHwnd = 0;
        int hostPid = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--embed" && i + 1 < args.Length)
            {
                hasEmbedFlag = true;
                long.TryParse(args[i + 1], out embedHwnd);
            }
            if (args[i] == "--host-pid" && i + 1 < args.Length)
            {
                int.TryParse(args[i + 1], out hostPid);
            }
        }

        if (!hasEmbedFlag)
        {
            Skua.Core.AppStartup.HotKeys.IsHostProcess = true;
            Task.Run(() =>
            {
                FlashTrustManager.EnsureTrustFile();
                Services.GetRequiredService<IClientFilesService>().CreateDirectories();
                Services.GetRequiredService<IClientFilesService>().CreateFiles();
            });
        }

        if (hasEmbedFlag)
        {
            Task.Run(() => Skua.WPF.Flash.FlashUtil.PreloadSwf());
            // Child instance - launched by TabbedHostWindow
            EmbeddedMainWindow mainChild = new() { WindowStartupLocation = WindowStartupLocation.Manual };
            Application.Current.MainWindow = mainChild;

            if (embedHwnd != 0)
            {
                // Legacy mode: immediately reparent into a specific HWND
                mainChild.Loaded += (s, ev) => 
                {
                    IntPtr hostHwnd = new IntPtr(embedHwnd);
                    IntPtr childHwnd = new System.Windows.Interop.WindowInteropHelper(mainChild).Handle;
                    
                    int style = GetWindowLong_Native(childHwnd, -16); // GWL_STYLE
                    SetWindowLong_Native(childHwnd, -16, (style & ~unchecked((int)0x80000000) & ~0x00C00000) | 0x40000000); // WS_CHILD
                    
                    SetParent_Native(childHwnd, hostHwnd);

                    GetClientRect_Native(hostHwnd, out var rect);
                    MoveWindow_Native(childHwnd, 0, 0, rect.Right - rect.Left, rect.Bottom - rect.Top, true);
                };
            }
            else
            {
                // No-reparent mode: start as independent top-level window off-screen.
                // The host will NEVER reparent us — it just moves us by screen coordinates.
                // Our WPF message pump stays 100% intact, so Flash initializes normally.
                mainChild.WindowStyle = WindowStyle.None;
                mainChild.Left = -32000;
                mainChild.Top = -32000;
                mainChild.Width = 800;
                mainChild.Height = 600;
                mainChild.ShowInTaskbar = false;
            }
            
            if (hostPid != 0)
            {
                Task.Run(() =>
                {
                    try
                    {
                        var parentProcess = System.Diagnostics.Process.GetProcessById(hostPid);
                        parentProcess.WaitForExit();
                    }
                    catch { } // If process doesn't exist or access denied, just exit
                    
                    System.Diagnostics.Process.GetCurrentProcess().Kill();
                });
            }
            
            mainChild.Show();
        }
        else
        {
            string extraArgs = string.Join(" ", args.Skip(1).Select(a => a.Contains(" ") ? $"\"{a}\"" : a));
            
            bool createdNew;
            AppMutex = new Mutex(true, "SkuaTabHostMutex", out createdNew);

            if (!createdNew)
            {
                using NamedPipeClientStream pipeClient = new(".", "SkuaTabHostPipe", PipeDirection.Out, PipeOptions.None, System.Security.Principal.TokenImpersonationLevel.Impersonation);
                try
                {
                    pipeClient.Connect(5000);
                    using StreamWriter sw = new(pipeClient);
                    sw.WriteLine(extraArgs);
                }
                catch { }

                Environment.Exit(0);
                return;
            }

            TabbedHostWindow host = new(extraArgs) { WindowStartupLocation = WindowStartupLocation.CenterScreen };
            Application.Current.MainWindow = host;
            host.Show();
        }
        IDialogService dialogService = Services.GetRequiredService<IDialogService>();
        IGetScriptsService getScripts = Services.GetRequiredService<IGetScriptsService>();
        if (!hasEmbedFlag)
        {
            if (Services.GetRequiredService<ISettingsService>().Get<bool>("CheckBotScriptsUpdates"))
            {
                Task.Run(async () =>
                {
                    await Task.Delay(2000);
                    await getScripts.GetScriptsAsync(null, default);

                    int missingBefore = getScripts.Missing;
                    int outdatedBefore = getScripts.Outdated;

                    if ((missingBefore > 0 || outdatedBefore > 0)
                        && Services.GetRequiredService<ISettingsService>().Get<bool>("AutoUpdateBotScripts"))
                    {
                        await getScripts.DownloadAllWhereAsync(s => !s.Downloaded || s.Outdated);
                    }
                });
            }

            if (Services.GetRequiredService<ISettingsService>().Get<bool>("CheckAdvanceSkillSetsUpdates"))
        {
            IAdvancedSkillContainer advanceSkillSets = Services.GetRequiredService<IAdvancedSkillContainer>();
            Task.Run(async () =>
            {
                await Task.Delay(2000);
                long remoteSize = await getScripts.CheckAdvanceSkillSetsUpdates();
                if (remoteSize > 0)
                {
                    if (Services.GetRequiredService<ISettingsService>().Get<bool>("AutoUpdateAdvanceSkillSetsUpdates"))
                    {
                        if (await getScripts.UpdateSkillSetsFile())
                        {
                            advanceSkillSets.SyncSkills();
                        }
                    }
                }
            });
        }

        Task.Run(async () =>
        {
            await Task.Delay(3000);
            await getScripts.UpdateQuestDataFile();
        });

        if (Services.GetRequiredService<ISettingsService>().Get<bool>("CheckJunkItemsUpdates"))
        {
            IJunkService junkService = Services.GetRequiredService<IJunkService>();
            Task.Factory.StartNew(async () =>
            {
                long remoteSize = await getScripts.CheckJunkItemsUpdates();
                if (remoteSize > 0)
                {
                    if (Services.GetRequiredService<ISettingsService>().Get<bool>("AutoUpdateJunkItems") || Services.GetRequiredService<IDialogService>().ShowMessageBox("Would you like to update your Junk Items list?", "Junk Items Update", true) == true)
                    {
                        if (await getScripts.UpdateJunkItemsFile())
                        {
                            junkService.Load();
                            if (Services.GetRequiredService<ISettingsService>().Get<bool>("AutoUpdateJunkItems"))
                                Services.GetRequiredService<IDialogService>().ShowMessageBox("Junk Items list has been updated.\r\nYou can disable auto Junk Items updates in Options > Application.", "Junk Items Update");
                            else
                                Services.GetRequiredService<IDialogService>().ShowMessageBox("Junk Items list has been updated.\r\nYou can enable auto Junk Items updates in Options > Application.", "Junk Items Update");
                        }
                        else
                        {
                            Services.GetRequiredService<IDialogService>().ShowMessageBox("Junk Items update error.\r\nYou can disable auto Junk Items updates in Options > Application.", "Junk Items Update");
                        }
                    }
                }
            });
        }
        } // End of !hasEmbedFlag

        Services.GetRequiredService<IPluginManager>().Initialize();

        Services.GetRequiredService<IHotKeyService>().Reload();
    }

    /// <summary>
    /// Configures the services for the application.
    /// </summary>
    private IServiceProvider ConfigureServices()
    {
        IServiceCollection services = new ServiceCollection();

        services.AddSingleton<ISettingsService, SettingsService>();

        services.AddWindowsServices();

        services.AddCommonServices();

        services.AddScriptableObjects();

        services.AddCompiler();

        services.AddSkuaMainAppViewModels();

        ServiceProvider provider = services.BuildServiceProvider();
        Ioc.Default.ConfigureServices(provider);

        return provider;
    }
}