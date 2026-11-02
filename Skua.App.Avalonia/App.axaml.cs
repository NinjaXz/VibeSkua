using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.App.Avalonia.Flash;
using Skua.App.Avalonia.AppStartup;
using Skua.App.Avalonia.Services;
using Skua.Shared.Avalonia.Services;
using Skua.Shared.Avalonia.Controls.Theming;
using Skua.Core.AppStartup;
using Skua.Core.Interfaces;
using Skua.Core.Utils;
using System;
using System.Threading;
using System.Threading.Tasks;

#if IS_WINDOWS
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using WinForms = System.Windows.Forms;
#endif

namespace Skua.App.Avalonia;

public partial class App : Application
{
    private static int _exceptionDialogShown;
    private int _parentHostProcessId;
    private DispatcherTimer? _hostThemeSyncTimer;
    private AvaloniaThemeService? _pendingHostThemeSync;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetParent", SetLastError = true)]
    public static extern IntPtr SetParent_Native(IntPtr hWndChild, IntPtr hWndNewParent);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
    public static extern int GetWindowLong_Native(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    public static extern int SetWindowLong_Native(IntPtr hWnd, int nIndex, int dwNewLong);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetClientRect")]
    public static extern bool GetClientRect_Native(IntPtr hWnd, out RECT lpRect);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "MoveWindow", SetLastError = true)]
    public static extern bool MoveWindow_Native(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowPos", SetLastError = true)]
    public static extern bool SetWindowPos_Native(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "PostMessage", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    public static extern bool PostMessage_Native(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public static System.Threading.Mutex? AppMutex;
    public static string? HostInstanceId { get; private set; }
    public static string HostPipeName { get; private set; } = "VibeSkuaTabHostPipe";
    public static string HostMutexName { get; private set; } = "VibeSkuaTabHostMutex";

    public override void OnFrameworkInitializationCompleted()
    {
        RegisterGlobalExceptionHandlers();

        ServiceCollection services = new();
        services.AddCommonServices();
        services.AddCompiler();
        services.AddScriptableObjects();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IDispatcherService, DispatcherService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IWindowService, WindowService>();
        services.AddSingleton<IFileDialogService, FileDialogService>();
        services.AddSingleton<IThemeService, AvaloniaThemeService>();
        services.AddSingleton(HotKeys.CreateHotKeys);
        services.AddSingleton<IHotKeyService, HotKeyService>();
        services.AddSingleton<ISoundService, SoundService>();
        services.AddSingleton<IFlashUtil, FlashUtil>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<SkuaStartupHandler>();
        Skua.App.Avalonia.AppStartup.Services.AddSkuaMainAppViewModels(services);

        Ioc.Default.ConfigureServices(services.BuildServiceProvider());

        Task.Run(() =>
        {
            try
            {
                Ioc.Default.GetRequiredService<IDiagnosticsService>().Start();
                ISettingsService settings = Ioc.Default.GetRequiredService<ISettingsService>();
                settings.SetApplicationVersion();
                FlashTrustManager.EnsureTrustFile();
            }
            catch { }
        });

        ColorSpectrumFix.Initialize();

        IThemeService themeService = Ioc.Default.GetRequiredService<IThemeService>();
        themeService.ThemeChanged += OnThemeChanged;
        themeService.SchemeChanged += OnSchemeChanged;
        ApplyThemeFromService(themeService);

        string[] args = Environment.GetCommandLineArgs();
        bool hasEmbedFlag = false;
        long embedHwnd = 0;
        int hostPid = 0;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--embed" && i + 1 < args.Length)
            {
                hasEmbedFlag = true;
                if (long.TryParse(args[i + 1], out embedHwnd))
                    Skua.Core.AppStartup.HotKeys.HostHwnd = new IntPtr(embedHwnd);
            }
            if (args[i] == "--host-pid" && i + 1 < args.Length)
            {
                int.TryParse(args[i + 1], out hostPid);
            }
        }

        for (int i = 1; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--host-instance", StringComparison.OrdinalIgnoreCase)) { HostInstanceId = args[i + 1]; break; }
        }
        if (!string.IsNullOrWhiteSpace(HostInstanceId))
        {
            string safeInstanceId = new string(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(HostInstanceId, char.IsLetterOrDigit)));
            if (!string.IsNullOrWhiteSpace(safeInstanceId))
            {
                HostInstanceId = safeInstanceId;
                HostPipeName = $"VibeSkuaTabHostPipe-{safeInstanceId}";
                HostMutexName = $"VibeSkuaTabHostMutex-{safeInstanceId}";
            }
            else HostInstanceId = null;
        }
        _parentHostProcessId = hasEmbedFlag ? hostPid : 0;
        if (_parentHostProcessId > 0)
            Skua.Shared.Avalonia.Controls.Shell.WindowTitleBar.ArtworkPathChanged += OnTitleBarArtworkPathChanged;
        if (_parentHostProcessId > 0 && themeService is AvaloniaThemeService initialTheme)
            QueueHostThemeSync(initialTheme);

        if (!hasEmbedFlag)
        {
            Skua.Core.AppStartup.HotKeys.IsHostProcess = true;
            Task.Run(() =>
            {
                IClientFilesService clientFiles = Ioc.Default.GetRequiredService<IClientFilesService>();
                clientFiles.CreateDirectories();
                clientFiles.CreateFiles();
            });
        }
        else
        {
            try
            {
                var icons = TrayIcon.GetIcons(this);
                if (icons != null)
                {
                    foreach (var ic in icons)
                        ic.IsVisible = false;
                    icons.Clear();
                }
                TrayIcon.SetIcons(this, null);
            }
            catch { }
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            Task.Run(() => Skua.App.Avalonia.Flash.FlashUtil.PreloadSwf());
            
            if (hasEmbedFlag)
            {
                // Child instance
                Views.MainWindow mainChild = new()
                {
                    WindowStartupLocation = global::Avalonia.Controls.WindowStartupLocation.Manual,
                    Title = "Skua-Child",
                    MinWidth = 0,
                    MinHeight = 0,
                    Width = 800,
                    Height = 600,
                    Position = new global::Avalonia.PixelPoint(-32000, -32000),
                    SystemDecorations = global::Avalonia.Controls.SystemDecorations.None,
                    ShowInTaskbar = false
                };
                desktop.MainWindow = mainChild;
                mainChild.HideTitleBar();

                mainChild.Opened += (s, ev) => 
                {
                    IntPtr childHwnd = mainChild.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
                    if (childHwnd != IntPtr.Zero)
                    {
                        int exStyle = GetWindowLong_Native(childHwnd, -20);
                        SetWindowLong_Native(childHwnd, -20, exStyle | 0x00000080); // WS_EX_TOOLWINDOW
                        SetWindowPos_Native(childHwnd, IntPtr.Zero, -32000, -32000, 800, 600, 0x0040 | 0x0010 | 0x0004); // SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_NOZORDER

                        if (embedHwnd != 0)
                        {
                            PostMessage_Native(new IntPtr(embedHwnd), 0x0400 + 440 /* WM_SKUA_CHILD_READY */, childHwnd, new IntPtr(Environment.ProcessId));
                        }
                    }
                };

                if (hostPid != 0)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            var parentProcess = System.Diagnostics.Process.GetProcessById(hostPid);
                            parentProcess.EnableRaisingEvents = true;
                            parentProcess.Exited += (_, _) =>
                            {
                                try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
                            };
                            while (!parentProcess.HasExited)
                            {
                                await Task.Delay(1000);
                            }
                        }
                        catch { }
                        try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
                    });
                }
            }
            else
            {
                string[] rawClientArgs = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Skip(args, 1));
                List<string> filteredArgs = new();
                for (int i = 0; i < rawClientArgs.Length; i++)
                {
                    if (string.Equals(rawClientArgs[i], "--host-instance", StringComparison.OrdinalIgnoreCase)) { i++; continue; }
                    filteredArgs.Add(rawClientArgs[i]);
                }
                string[] clientArgs = filteredArgs.ToArray();
                string payload = System.Text.Json.JsonSerializer.Serialize(clientArgs);
                bool createdNew;
                AppMutex = new System.Threading.Mutex(true, HostMutexName, out createdNew);

                if (!createdNew)
                {
                    bool forwarded = false;
                    for (int attempt = 0; attempt < 5; attempt++)
                    {
                        try
                        {
                            using System.IO.Pipes.NamedPipeClientStream pipeClient = new(".", HostPipeName, System.IO.Pipes.PipeDirection.Out, System.IO.Pipes.PipeOptions.None);
                            pipeClient.Connect(1000);
                            using System.IO.StreamWriter sw = new(pipeClient);
                            sw.WriteLine(payload);
                            sw.Flush();
                            forwarded = true;
                            break;
                        }
                        catch
                        {
                            System.Threading.Thread.Sleep(150);
                        }
                    }

                    if (forwarded)
                    {
                        Environment.Exit(0);
                        return;
                    }

                    // If pipe forwarding failed, the previous host is dead or non-responsive.
                    // Recover and take over as the host window.
                    try
                    {
                        AppMutex?.Dispose();
                        AppMutex = new System.Threading.Mutex(true, HostMutexName);
                    }
                    catch { }
                }

                // Master instance. The tray icon is created only here: an instance that forwards to an
                // existing host exits through Environment.Exit, which never removes it (ghost icon).
                SetupTrayIcon();
                Views.TabbedHostWindow masterWindow = new(clientArgs) { WindowStartupLocation = global::Avalonia.Controls.WindowStartupLocation.CenterScreen };
                desktop.MainWindow = masterWindow;
                if (themeService is AvaloniaThemeService activeTheme)
                    ThemeResourceApplicator.ApplyTitleBarTheme(masterWindow, activeTheme.IsDarkTheme,
                        activeTheme.UseAccentOnMainTitleBar ? activeTheme.PrimaryColor : (Color?)null,
                        activeTheme.UseAccentOnMainTitleBar ? activeTheme.PrimaryForegroundColor : (Color?)null);
                else
                    ThemeResourceApplicator.ApplyTitleBarTheme(masterWindow, themeService.IsDarkTheme);
            }
            desktop.Exit += OnDesktopExit;
        }

        Task.Run(() => TryInitializeStartupServices());
        base.OnFrameworkInitializationCompleted();
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Exception ex = e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception.");
            ShowUnhandledException("AppDomain.CurrentDomain.UnhandledException", ex);
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            System.Diagnostics.Trace.WriteLine($"TaskScheduler.UnobservedTaskException: {e.Exception}");
            e.SetObserved();
        };

    }

    private static void ShowUnhandledException(string source, Exception ex)
    {
        if (Interlocked.Exchange(ref _exceptionDialogShown, 1) == 1)
            return;

        try
        {
            string details = $"Unhandled exception source: {source}\r\n\r\n{ex.GetType().FullName}: {ex.Message}\r\n\r\n{ex.StackTrace}";
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "crash.txt"), details); } catch { }

            #if IS_WINDOWS
            WinForms.MessageBox.Show(
                details,
                "Skua Client Exception",
                WinForms.MessageBoxButtons.OK,
                WinForms.MessageBoxIcon.Error);
            #endif
        }
        catch
        {
            // ignored
        }
        finally
        {
            Interlocked.Exchange(ref _exceptionDialogShown, 0);
        }
    }

    private static async Task TryInitializeStartupServices()
    {
        try
        {
            MainMenu.RegisterManagedWindows(Ioc.Default.GetRequiredService<IWindowService>());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Startup window service init failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            Ioc.Default.GetRequiredService<IPluginManager>().Initialize();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Startup plugin init failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            Ioc.Default.GetRequiredService<IHotKeyService>().Reload();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Startup hotkey init failed: {ex.GetType().Name}: {ex.Message}");
        }

        try
        {
            IGetScriptsService? getScripts = Ioc.Default.GetService<IGetScriptsService>();
            if (getScripts is null)
                return;

            await getScripts.GetScriptsAsync(null, default);

            // Each tab runs as an --embed child process; only the host downloads, so the prompt shows once.
            if (!Skua.Core.AppStartup.HotKeys.IsHostProcess)
                return;

            ISettingsService settings = Ioc.Default.GetRequiredService<ISettingsService>();
            IDialogService dialogService = Ioc.Default.GetRequiredService<IDialogService>();
            if (settings.Get<bool>("CheckBotScriptsUpdates") && (getScripts.Missing > 0 || getScripts.Outdated > 0)
                && (settings.Get<bool>("AutoUpdateBotScripts")
                    || dialogService.ShowMessageBox("Would you like to update your scripts?", "Script Update", true) == true))
            {
                await Skua.Core.Services.ScriptStartupUpdater.DownloadMissingAndOutdatedAsync(getScripts, dialogService);
            }
        }
        catch { }
    }

    private static void OnDesktopExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        DisposeTrayIcon();
        try
        {
            if (Ioc.Default.GetService<IThemeService>() is AvaloniaThemeService ats)
                ats.FlushPendingSave();
        }
        catch { }

        try
        {
            AppMutex?.ReleaseMutex();
            AppMutex?.Dispose();
            AppMutex = null;
        }
        catch { }

        Task.Run(async () =>
        {
            try
            {
                var cleanupTask = Task.Run(async () =>
                {
                    try { await Ioc.Default.GetRequiredService<IDiagnosticsService>().DisposeAsync(); } catch { }
                    try { Ioc.Default.GetRequiredService<ICaptureProxy>().Stop(); } catch { }
                    try
                    {
                        await ((IAsyncDisposable)Ioc.Default.GetRequiredService<IScriptBoost>()).DisposeAsync();
                        await ((IAsyncDisposable)Ioc.Default.GetRequiredService<IScriptBotStats>()).DisposeAsync();
                        await ((IAsyncDisposable)Ioc.Default.GetRequiredService<IScriptDrop>()).DisposeAsync();
                        await Ioc.Default.GetRequiredService<IScriptManager>().StopScript();
                        await ((IScriptInterfaceManager)Ioc.Default.GetRequiredService<IScriptInterface>()).StopTimerAsync();
                    } catch { }
                    try { Ioc.Default.GetRequiredService<IFlashUtil>().Dispose(); } catch { }
                    try
                    {
                        if (Ioc.Default.GetRequiredService<IHotKeyService>() is IDisposable disposableHotKeys)
                            disposableHotKeys.Dispose();
                    } catch { }

                    try
                    {
                        WeakReferenceMessenger.Default.Cleanup();
                        WeakReferenceMessenger.Default.Reset();
                        StrongReferenceMessenger.Default.Reset();
                    } catch { }
                });

                await Task.WhenAny(cleanupTask, Task.Delay(1500));
            }
            catch { }
            finally
            {
                Environment.Exit(0);
            }
        });
    }

    // Full theme changes can include base theme + multiple palette slots, so we always
    // republish the complete resource map here (see ThemeResourceApplicator).
    private void OnThemeChanged(object? theme)
    {
        IThemeService themeService = Ioc.Default.GetRequiredService<IThemeService>();
        ThemeVariant targetVariant = themeService.IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        if (RequestedThemeVariant != targetVariant)
            RequestedThemeVariant = targetVariant;

        if (themeService is AvaloniaThemeService ats)
        {
            // Keep accents on secondary windows; the checkbox only controls the main tab host caption.
            ThemeResourceApplicator.ApplyTitleBarTheme(this, themeService.IsDarkTheme, ats.PrimaryColor, ats.PrimaryForegroundColor);
            ThemeResourceApplicator.ApplyAccentBrushes(
                this,
                ats.PrimaryColor,
                ats.PrimaryForegroundColor,
                ats.SecondaryColor,
                ats.SecondaryForegroundColor,
                isDarkTheme: themeService.IsDarkTheme);
            if (!ats.UseAccentOnMainTitleBar &&
                ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is Views.TabbedHostWindow hostWindow)
            {
                ThemeResourceApplicator.ApplyTitleBarTheme(hostWindow, ats.IsDarkTheme);
            }
            QueueHostThemeSync(ats);
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
            // Scheme edits can change the primary palette without changing the base theme.
            // Keep accents on secondary windows and control only the main host caption with the checkbox.
            ThemeResourceApplicator.ApplyTitleBarTheme(this, isDark, ats.PrimaryColor, ats.PrimaryForegroundColor);
            ThemeResourceApplicator.ApplyAccentBrushes(
                this,
                ats.PrimaryColor,
                ats.PrimaryForegroundColor,
                ats.SecondaryColor,
                ats.SecondaryForegroundColor,
                isDarkTheme: isDark);
            if (!ats.UseAccentOnMainTitleBar &&
                ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                desktop.MainWindow is Views.TabbedHostWindow hostWindow)
            {
                ThemeResourceApplicator.ApplyTitleBarTheme(hostWindow, isDark);
            }
            QueueHostThemeSync(ats);
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
        if (themeService is AvaloniaThemeService ats)
        {
            ThemeResourceApplicator.ApplyTitleBarTheme(this, isDark, ats.PrimaryColor, ats.PrimaryForegroundColor);
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

    /// <summary>
    /// Sends the selected title-bar artwork path to the separate main tab-host process.
    /// The image is updated independently of palette changes so other clients cannot overwrite it
    /// with an older in-memory settings value.
    /// </summary>
    private void OnTitleBarArtworkPathChanged(string? path)
    {
        if (_parentHostProcessId <= 0 || !OperatingSystem.IsWindows())
            return;

        string encodedPath = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path ?? string.Empty));
        _ = Task.Run(() =>
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    using System.IO.Pipes.NamedPipeClientStream pipe = new(".", HostPipeName, System.IO.Pipes.PipeDirection.Out);
                    pipe.Connect(500);
                    using System.IO.StreamWriter writer = new(pipe);
                    writer.WriteLine("ARTWORK|" + encodedPath);
                    writer.Flush();
                    return;
                }
                catch
                {
                    if (attempt < 4)
                        System.Threading.Thread.Sleep(100);
                }
            }
        });
    }

    /// <summary>
    /// Debounces live palette changes and sends the latest display colors to the parent tab host.
    /// Embedded clients run in separate processes, so their theme events cannot reach the host directly.
    /// </summary>
    private void QueueHostThemeSync(AvaloniaThemeService themeService)
    {
        if (_parentHostProcessId <= 0 || !OperatingSystem.IsWindows())
            return;

        _pendingHostThemeSync = themeService;
        if (_hostThemeSyncTimer is null)
        {
            DispatcherTimer syncTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
            syncTimer.Tick += (_, _) =>
            {
                syncTimer.Stop();
                AvaloniaThemeService? latestTheme = _pendingHostThemeSync;
                _pendingHostThemeSync = null;
                if (latestTheme is not null)
                    SendHostThemeSnapshot(latestTheme);
            };
            _hostThemeSyncTimer = syncTimer;
        }

        DispatcherTimer timer = _hostThemeSyncTimer!;
        timer.Stop();
        timer.Start();
    }

    /// <summary>
    /// Sends the live, already-adjusted theme through the existing local tab-host pipe.
    /// The snapshot is display-only and is not persisted by the host process.
    /// </summary>
    private void SendHostThemeSnapshot(AvaloniaThemeService themeService)
    {
        try
        {
            ISettingsService settingsService = Ioc.Default.GetRequiredService<ISettingsService>();
            ThemeItem? savedTheme = ThemeItem.FromString(settingsService.Get<string>("CurrentTheme"));
            ThemeItem displaySnapshot = new()
            {
                Name = savedTheme?.Name ?? "Skua",
                IsDarkTheme = themeService.IsDarkTheme,
                PrimaryColor = themeService.PrimaryColor,
                SecondaryColor = themeService.SecondaryColor,
                PrimaryForegroundColor = themeService.PrimaryForegroundColor,
                SecondaryForegroundColor = themeService.SecondaryForegroundColor,
                UseColorAdjustment = false,
                DesiredContrastRatio = themeService.DesiredContrastRatio,
                ContrastValue = themeService.ContrastValue?.ToString() ?? "Medium",
                ColorSelectionValue = themeService.ColorSelectionValue?.ToString() ?? "All"
            };

            string message = "THEME|" + themeService.UseAccentOnMainTitleBar.ToString() + "|" + displaySnapshot.ConvertToString();
            _ = Task.Run(() =>
            {
                for (int attempt = 0; attempt < 5; attempt++)
                {
                    try
                    {
                        using System.IO.Pipes.NamedPipeClientStream pipe = new(".", HostPipeName, System.IO.Pipes.PipeDirection.Out);
                        pipe.Connect(500);
                        using System.IO.StreamWriter writer = new(pipe);
                        writer.WriteLine(message);
                        writer.Flush();
                        return;
                    }
                    catch
                    {
                        if (attempt < 4)
                            System.Threading.Thread.Sleep(100);
                    }
                }
            });
        }
        catch
        {
            // Theme synchronization is best effort; the embedded client remains usable if the host is closing.
        }
    }

    private void TrayIcon_Clicked(object? sender, EventArgs e)
    {
        ShowClientWindow();
    }

    private void TrayShowClient_Click(object? sender, EventArgs e)
    {
        ShowClientWindow();
    }

    private void TrayHideClient_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is null)
            return;
        // Minimize the owner instead of hiding it directly. The embedded game HWNDs
        // are separate top-level windows owned by the host; Windows hides owned windows
        // with the owner while preserving the Flash renderer for a clean restore.
        desktop.MainWindow.WindowState = WindowState.Minimized;
    }

    private static TrayIcon? _trayIcon;

    public static void DisposeTrayIcon()
    {
        try
        {
            void Cleanup()
            {
                try
                {
                    if (_trayIcon != null)
                    {
                        _trayIcon.IsVisible = false;
                        if (_trayIcon is IDisposable d)
                            d.Dispose();
                        _trayIcon = null;
                    }

                    if (Current != null)
                    {
                        var icons = TrayIcon.GetIcons(Current);
                        if (icons != null)
                        {
                            foreach (var ic in icons)
                            {
                                try
                                {
                                    ic.IsVisible = false;
                                    if (ic is IDisposable d)
                                        d.Dispose();
                                }
                                catch { }
                            }
                            icons.Clear();
                        }
                        TrayIcon.SetIcons(Current, null);
                    }
                }
                catch { }
            }

            if (Dispatcher.UIThread.CheckAccess())
                Cleanup();
            else
                Dispatcher.UIThread.InvokeAsync(Cleanup).Wait(TimeSpan.FromMilliseconds(500));
        }
        catch { }
    }

    private void TrayExit_Click(object? sender, EventArgs e)
    {
        DisposeTrayIcon();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    private void ShowClientWindow()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop || desktop.MainWindow is null)
            return;

        if (!desktop.MainWindow.IsVisible)
            desktop.MainWindow.Show();
        if (desktop.MainWindow.WindowState == WindowState.Minimized)
            desktop.MainWindow.WindowState = WindowState.Normal;
        desktop.MainWindow.Activate();
    }

    private void SetupTrayIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Skua.Shared.Avalonia/Assets/SkuaIcon.ico"));
            var icon = new WindowIcon(stream);

            _trayIcon = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "Skua",
                IsVisible = true
            };
            _trayIcon.Clicked += TrayIcon_Clicked;

            var menu = new NativeMenu();

            var showItem = new NativeMenuItem("Show Client");
            showItem.Click += TrayShowClient_Click;
            menu.Add(showItem);

            var hideItem = new NativeMenuItem("Hide Client");
            hideItem.Click += TrayHideClient_Click;
            menu.Add(hideItem);

            menu.Add(new NativeMenuItemSeparator());

            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += TrayExit_Click;
            menu.Add(exitItem);

            _trayIcon.Menu = menu;

            var trayIcons = new TrayIcons { _trayIcon };
            TrayIcon.SetIcons(this, trayIcons);
        }
        catch { }
    }
}
