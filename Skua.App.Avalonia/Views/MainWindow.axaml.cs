using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Material.Icons;
using Material.Icons.Avalonia;
using Skua.App.Avalonia.Services;
using Skua.App.Avalonia.ViewModels;
using Skua.App.Avalonia.ViewModels.MainMenu;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using Skua.Core.Interfaces.ViewModels;
using System.Threading.Tasks;
using System.Text.Json;
using Skua.Core.AppStartup;

#if IS_WINDOWS
using Avalonia.Input;
using AxShockwaveFlashObjects;
using System.Runtime.Versioning;
#endif

namespace Skua.App.Avalonia.Views;

public partial class MainWindow : Window
{
#if IS_WINDOWS
    public delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData);
    
    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool SetWindowText(IntPtr hWnd, string lpString);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const int WM_NCACTIVATE = 0x0086;
    private const int WM_WINDOWPOSCHANGED = 0x0047;
    private const int WM_DPICHANGED = 0x02E0;
    private const int WM_GETDPISCALEDSIZE = 0x02E4;
    private const int WM_SKUA_CHILD_DPICHANGED = 0x0400 + 441;

    private SubclassProc? _subclassProc;
    private bool _scalingCheckQueued;
#endif
    private const int WM_SKUA_JUMP_MAP = 0x0400 + 449;
    private const int WM_SKUA_SET_OPTION = 0x0400 + 450;
    private const int WM_SKUA_LOAD_SCRIPT = 0x0400 + 453;
    private const int WM_SKUA_ARMY_SCHEDULER = 0x0400 + 454;
    private const int WM_SKUA_ARMY_SCHEDULER_STOP = 0x0400 + 455;
    private const int WM_SKUA_CHECK_LOGIN = 0x0400 + 456;
    private const int WM_SKUA_JUMP_PLAYER = 0x0400 + 457;
    private const int WM_SKUA_JUMP_HOUSE = 0x0400 + 464;
    private const int WM_SKUA_LOGIN = 0x0400 + 447;
    private const int WM_SKUA_GRIDVIEW = 0x0400 + 444;
    private const int WM_SKUA_AUTO_ATTACK = 0x0400 + 459;
    private const int WM_SKUA_OPEN_BANK = 0x0400 + 460;
    private const int WM_SKUA_CHANGE_SERVER = 0x0400 + 465;

    private static readonly TimeSpan MetricRefreshInterval = TimeSpan.FromSeconds(1);

    private readonly IFlashUtil _flash;
    private readonly IScriptOption _options;
    private readonly SkuaStartupHandler _startup;
    private readonly MainMenuViewModel _mainMenuVm;
    private readonly MainViewModel _mainViewModel;
    private MenuItem? _pluginsMenuItem;
    private DispatcherTimer? _tabTitleSyncTimer;
    private bool _isFlashLoaded;
    private bool _isTitleBarPermanentlyHidden = false;
    private int _preThrottleFPS = -1;
    private string? _preThrottleQuality = null;

    public void HideTitleBar()
    {
        _isTitleBarPermanentlyHidden = true;
        var titleBar = this.FindControl<global::Avalonia.Controls.Control>("TitleBar");
        if (titleBar != null)
        {
            titleBar.IsVisible = false;
        }
        
        this.ExtendClientAreaToDecorationsHint = false;
    }

    public MainWindow()
    {
        InitializeComponent();
        _mainViewModel = Ioc.Default.GetRequiredService<MainViewModel>();
        _mainMenuVm = Ioc.Default.GetRequiredService<MainMenuViewModel>();
        _flash = Ioc.Default.GetRequiredService<IFlashUtil>();
        _options = Ioc.Default.GetRequiredService<IScriptOption>();
        _startup = Ioc.Default.GetRequiredService<SkuaStartupHandler>();
        
        DataContext = _mainViewModel;
        Skua.Shared.Avalonia.Controls.Shell.WindowTitleBar.SetArtworkPath(Ioc.Default.GetRequiredService<ISettingsService>().GetShared().TitleBarArtworkPath);
        MainMenuBar.DataContext = _mainMenuVm;
        ConfigureMainMenu();
        RegisterLifecycleHandlers();
        
        #if IS_WINDOWS
        RegisterMessages();
        #endif
    }

    private void ConfigureMainMenu()
    {
        BuildMainMenu();
        _mainMenuVm.MainMenuItems.CollectionChanged += MainMenuItemsChanged;
        if (_mainMenuVm.Plugins is System.Collections.Specialized.INotifyCollectionChanged notifyPlugins)
        {
            notifyPlugins.CollectionChanged += (s, e) => Dispatcher.UIThread.Post(BuildMainMenu);
        }
    }

    private void MainMenuItemsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.UIThread.Post(BuildMainMenu);
    }

    private void BuildMainMenu()
    {
        MainMenuControl.Items.Clear();
        foreach (MainMenuItemViewModel item in _mainMenuVm.MainMenuItems)
            MainMenuControl.Items.Add(CreateMainMenuItem(item, isTopLevel: true));
    }

    private global::Avalonia.Controls.MenuItem CreateMainMenuItem(IMainMenuItemViewModel viewModel, bool isTopLevel = false)
    {
        global::Avalonia.Controls.MenuItem menuItem = new()
        {
            Command = viewModel.Command
        };

        if (isTopLevel)
        {
            menuItem.Classes.Add("top-nav-item");
            MaterialIconKind? iconKind = viewModel.Header switch
            {
                "Scripts" => MaterialIconKind.ScriptTextOutline,
                "Options" => MaterialIconKind.CogOutline,
                "Tools & Helpers" => MaterialIconKind.WrenchOutline,
                "Combat" => MaterialIconKind.SwordCross,
                "Bank" => MaterialIconKind.BankOutline,
                "Diagnostics" => MaterialIconKind.BugOutline,
                "Plugins" => MaterialIconKind.PuzzleOutline,
                _ => null
            };

            if (iconKind.HasValue)
            {
                var icon = new MaterialIcon
                {
                    Kind = iconKind.Value,
                    Width = 14,
                    Height = 14,
                    VerticalAlignment = VerticalAlignment.Center
                };
                icon[!MaterialIcon.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaTextBrush");

                var text = new TextBlock
                {
                    Text = viewModel.Header,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12
                };
                menuItem.Header = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    VerticalAlignment = VerticalAlignment.Center,
                    Children = { icon, text }
                };
            }
            else
            {
                menuItem.Header = viewModel.Header;
            }
        }
        else
        {
            MaterialIconKind? subIconKind = viewModel.Header switch
            {
                "Script Loader" => MaterialIconKind.ScriptOutline,
                "Scheduler" => MaterialIconKind.CalendarClock,
                "Game" => MaterialIconKind.GamepadVariantOutline,
                "Application" => MaterialIconKind.ApplicationCogOutline,
                "CoreBots" => MaterialIconKind.RobotOutline,
                "Application Themes" => MaterialIconKind.PaletteOutline,
                "HotKeys" => MaterialIconKind.KeyboardOutline,
                "Loadouts Manager" => MaterialIconKind.AccountSwitchOutline,
                "Fast Travel" => MaterialIconKind.CompassOutline,
                "Loader" => MaterialIconKind.DownloadOutline,
                "Grabber" => MaterialIconKind.HandBackRightOutline,
                "Current Drops" => MaterialIconKind.GiftOutline,
                "Junk Items" => MaterialIconKind.DeleteSweepOutline,
                "Skills" => MaterialIconKind.LightningBoltOutline,
                "Stats" => MaterialIconKind.ChartLine,
                "Runtime" => MaterialIconKind.PlayNetworkOutline,
                "Logs" => MaterialIconKind.FileDocumentOutline,
                "Console" => MaterialIconKind.Console,
                "Spammer" => MaterialIconKind.SendClockOutline,
                "Logger" => MaterialIconKind.TextSearch,
                "Interceptor" => MaterialIconKind.ShieldBugOutline,
                _ => null
            };

            if (subIconKind.HasValue)
            {
                var subIcon = new MaterialIcon
                {
                    Kind = subIconKind.Value,
                    Width = 14,
                    Height = 14,
                    VerticalAlignment = VerticalAlignment.Center
                };
                subIcon[!MaterialIcon.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaMutedTextBrush");
                menuItem.Icon = subIcon;
            }
            menuItem.Header = viewModel.Header;
        }

        if (viewModel.Header == "Plugins")
        {
            foreach (IMainMenuItemViewModel subItem in _mainMenuVm.Plugins)
                menuItem.Items.Add(CreateMainMenuItem(subItem, isTopLevel: false));
        }
        else if (viewModel.SubItems != null && viewModel.SubItems.Any())
        {
            foreach (IMainMenuItemViewModel subItem in viewModel.SubItems)
                menuItem.Items.Add(CreateMainMenuItem(subItem, isTopLevel: false));
        }

        return menuItem;
    }

    #if IS_WINDOWS
    private void RegisterMessages()
    {
        WeakReferenceMessenger.Default.Register<MainWindow, FlashChangedMessage<AxShockwaveFlash>>(this, static (r, m) => r.OnFlashControlChanged(m.Flash));
    }
    #endif

    private void RegisterLifecycleHandlers()
    {
        Opened += MainWindow_Opened;
        Closing += (s, e) => App.DisposeTrayIcon();
        Closed += MainWindow_Closed;

        if (_options is System.ComponentModel.INotifyPropertyChanged notifyOptions)
        {
            notifyOptions.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(IScriptOption.HeadlessMode))
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        bool isHeadless = _options.HeadlessMode;
                        if (HeadlessOverlay != null) HeadlessOverlay.IsVisible = isHeadless;
                        if (FlashHost != null) FlashHost.IsVisible = !isHeadless && _isFlashLoaded;
                    });
                }
            };
        }
    }

    private void MainWindow_Opened(object? sender, EventArgs e)
    {
        _startup.Execute();
        _flash.FlashCall += OnFlashCall;

        try
        {
            var themeService = Ioc.Default.GetService<IThemeService>();
            bool isDark = themeService?.IsDarkTheme ?? true;
            Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(this, isDark);
        }
        catch { }
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) 
        {
            _flash.InitializeFlash();

            // Start the client-wide timer only after Flash has been initialized.
            // Gameplay statistics then sample manual play without requiring a script.
            ((IScriptInterfaceManager)Ioc.Default.GetRequiredService<IScriptInterface>()).Initialize();

#if IS_WINDOWS
            if (this.TryGetPlatformHandle()?.Handle is IntPtr hwnd && hwnd != IntPtr.Zero)
            {
                SetWindowText(hwnd, _mainViewModel.Title ?? "Skua");
                _subclassProc = WindowSubclassProc;
                SetWindowSubclass(hwnd, _subclassProc, 1, IntPtr.Zero);
            }
#endif
        }

        _tabTitleSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        string lastWrittenUsername = "";
        _tabTitleSyncTimer.Tick += (s, ev) =>
        {
            try
            {
                var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
                if (!bot.Player.LoggedIn || string.IsNullOrWhiteSpace(bot.Player.Username))
                    return;

                string username = bot.Player.Username;
                string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VibeSkuaTabs");
                System.IO.Directory.CreateDirectory(dir);

                if (username != lastWrittenUsername)
                {
                    lastWrittenUsername = username;
                    string usernameFile = System.IO.Path.Combine(dir, $"{Environment.ProcessId}.txt");
                    System.IO.File.WriteAllText(usernameFile, username);
                }

                IScriptMap map = Ioc.Default.GetRequiredService<IScriptMap>();
                IScriptPlayer player = Ioc.Default.GetRequiredService<IScriptPlayer>();
                IScriptOption options = Ioc.Default.GetRequiredService<IScriptOption>();
                if (!map.Loaded || string.IsNullOrWhiteSpace(map.Name) || string.IsNullOrWhiteSpace(player.Cell))
                    return;

                // Report the server-assigned room number rather than the internal area ID or requested private-room number.
                string room = map.RoomNumber > 0 ? map.RoomNumber.ToString() : string.Empty;
                string locationFile = System.IO.Path.Combine(dir, $"{Environment.ProcessId}.location.json");
                string temporaryFile = locationFile + ".tmp";
                string locationJson = JsonSerializer.Serialize(new
                {
                    Map = map.Name,
                    Room = room,
                    Cell = player.Cell,
                    Pad = string.IsNullOrWhiteSpace(player.Pad) ? "Spawn" : player.Pad,
                    Username = username,
                    UpdatedUtc = DateTime.UtcNow
                });
                System.IO.File.WriteAllText(temporaryFile, locationJson);
                System.IO.File.Move(temporaryFile, locationFile, true);
            }
            catch { }
        };
        _tabTitleSyncTimer.Start();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        App.DisposeTrayIcon();
        _tabTitleSyncTimer?.Stop();
        try
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "VibeSkuaTabs");
            string file = System.IO.Path.Combine(dir, $"{Environment.ProcessId}.txt");
            string locationFile = System.IO.Path.Combine(dir, $"{Environment.ProcessId}.location.json");
            string temporaryFile = locationFile + ".tmp";
            if (System.IO.File.Exists(file))
                System.IO.File.Delete(file);
            if (System.IO.File.Exists(locationFile))
                System.IO.File.Delete(locationFile);
            if (System.IO.File.Exists(temporaryFile))
                System.IO.File.Delete(temporaryFile);
        }
        catch { }

        _flash.FlashCall -= OnFlashCall;
        _startup.Dispose();
        StrongReferenceMessenger.Default.UnregisterAll(this);
        WeakReferenceMessenger.Default.UnregisterAll(this);
    }
    
    #if IS_WINDOWS
    private void OnFlashControlChanged(AxShockwaveFlash flash)
    {
        FlashHost.SetChild(flash);
    }
    #endif

    private void OnFlashCall(string function, params object[] args)
    {
        if (function == "loaded" && !_isFlashLoaded)
        {
            _isFlashLoaded = true;
            Dispatcher.UIThread.Post(() =>
            {
                // Hiding alone leaves the indeterminate animation running at the monitor's refresh rate.
                LoadingBar.IsIndeterminate = false;
                LoadingBar.IsVisible = false;
                FlashHost.IsVisible = !_options.HeadlessMode;
                HeadlessOverlay.IsVisible = _options.HeadlessMode;
                try
                {
                    FlashHost?.SetScaleMode(false);
                    _flash.Call("setGameObject", "stage.scaleMode", "showAll");
                }
                catch { }
            });
        }
    }

#if IS_WINDOWS
    private IntPtr WindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_NCACTIVATE)
        {
            if (wParam == IntPtr.Zero)
            {
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(fg, out uint fgPid);
                    if (fgPid == (uint)Environment.ProcessId)
                    {
                        return DefSubclassProc(hWnd, uMsg, (IntPtr)1, lParam);
                    }
                }
            }
        }

        // The tab host sizes this window to its tab area. On a monitor with another scale, keep that size instead of
        // letting Windows scale it, and tell the host so it places the window again.
        if (uMsg == WM_GETDPISCALEDSIZE && HotKeys.HostHwnd != IntPtr.Zero && lParam != IntPtr.Zero && GetWindowRect(hWnd, out RECT current))
        {
            Marshal.WriteInt32(lParam, 0, current.Right - current.Left);
            Marshal.WriteInt32(lParam, 4, current.Bottom - current.Top);
            return (IntPtr)1;
        }
        else if (uMsg == WM_DPICHANGED)
        {
            // Avalonia updates its size in DIPs only on WM_SIZE, which doesn't come when the size in pixels stays
            // the same, so its next layout would resize the window to the old DIP size at the new scale. Give it
            // the new DIP size of the suggested rect first.
            uint newDpi = (uint)(wParam.ToInt64() & 0xFFFF);
            if (newDpi > 0 && lParam != IntPtr.Zero)
            {
                RECT suggested = Marshal.PtrToStructure<RECT>(lParam);
                Width = (suggested.Right - suggested.Left) * 96.0 / newDpi;
                Height = (suggested.Bottom - suggested.Top) * 96.0 / newDpi;
            }

            IntPtr result = DefSubclassProc(hWnd, uMsg, wParam, lParam);
            if (HotKeys.HostHwnd != IntPtr.Zero)
                PostMessage(HotKeys.HostHwnd, WM_SKUA_CHILD_DPICHANGED, hWnd, new IntPtr(Environment.ProcessId));
            return result;
        }
        else if (uMsg == WM_WINDOWPOSCHANGED)
        {
            IntPtr result = DefSubclassProc(hWnd, uMsg, wParam, lParam);
            if (!_scalingCheckQueued)
            {
                _scalingCheckQueued = true;
                Dispatcher.UIThread.Post(() => SyncScalingWithWindowDpi(hWnd), DispatcherPriority.Background);
            }
            return result;
        }

        if (uMsg == WM_SKUA_GRIDVIEW)
        {
            Dispatcher.UIThread.Post(() =>
            {
                bool isGrid = wParam.ToInt32() == 1;
                
                var topNav = this.FindControl<global::Avalonia.Controls.Control>("TopNavBorder");
                if (topNav != null)
                    topNav.IsVisible = !isGrid;
                
                var titleBar = this.FindControl<global::Avalonia.Controls.Control>("TitleBar");
                if (titleBar != null && !_isTitleBarPermanentlyHidden)
                    titleBar.IsVisible = !isGrid;
                
                this.MinWidth = 0;
                this.MinHeight = 0;

                if (FlashHost != null)
                    FlashHost.SetScaleMode(false);

                try
                {
                    _flash.Call("setGameObject", "stage.scaleMode", "showAll");
                }
                catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_LOGIN)
        {
            Task.Run(() => 
            {
                var options = Ioc.Default.GetRequiredService<IScriptOption>();
                var servers = Ioc.Default.GetRequiredService<IScriptServers>();
                string targetServer = string.IsNullOrWhiteSpace(options.ReloginServer) ? "Twilly" : options.ReloginServer;
                servers.Relogin(targetServer);
            });
            return IntPtr.Zero;
        }
        else if (uMsg == 0x0400 + 448) // WM_SKUA_LOGOUT
        {
            Ioc.Default.GetRequiredService<IScriptServers>().Logout();
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_CHANGE_SERVER)
        {
            Task.Run(() =>
            {
                try
                {
                    string tempFile = Path.Combine(Path.GetTempPath(), "skua_global_change_server.txt");
                    if (!System.IO.File.Exists(tempFile))
                        return;

                    string targetServer = System.IO.File.ReadAllText(tempFile).Trim();
                    if (!string.IsNullOrWhiteSpace(targetServer))
                        Ioc.Default.GetRequiredService<IScriptServers>().Relogin(targetServer);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"Army Control server change failed: {ex.GetType().Name}: {ex.Message}");
                }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_JUMP_MAP)
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_jump.txt");
                    if (System.IO.File.Exists(tempFile))
                    {
                        string[] lines = System.IO.File.ReadAllLines(tempFile);
                        string map = lines.Length > 0 ? lines[0].Trim() : "";
                        string cell = lines.Length > 1 && !string.IsNullOrWhiteSpace(lines[1]) ? lines[1].Trim() : "Enter";
                        string pad = lines.Length > 2 && !string.IsNullOrWhiteSpace(lines[2]) ? lines[2].Trim() : "Spawn";
                        
                        if (cell.Contains(','))
                        {
                            var cellParts = cell.Split(',');
                            cell = cellParts[0].Trim();
                            if (cellParts.Length > 1) pad = cellParts[1].Trim();
                        }
                        
                        var mapService = Ioc.Default.GetRequiredService<IScriptMap>();
                        if (string.IsNullOrWhiteSpace(map) || mapService.Name.Equals(map, StringComparison.OrdinalIgnoreCase))
                            mapService.Jump(cell, pad, false);
                        else
                            mapService.Join(map, cell, pad, true, false);
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_JUMP_HOUSE)
        {
            Task.Run(() =>
            {
                try
                {
                    // Same packet CoreBots uses to join the player's own house.
                    var player = Ioc.Default.GetRequiredService<IScriptPlayer>();
                    if (player.LoggedIn && !string.IsNullOrWhiteSpace(player.Username))
                        Ioc.Default.GetRequiredService<IScriptSend>().Packet($"%xt%zm%house%1%{player.Username}%");
                }
                catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_JUMP_PLAYER)
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_jump_player.txt");
                    if (System.IO.File.Exists(tempFile))
                    {
                        string targetPlayer = System.IO.File.ReadAllText(tempFile)?.Trim() ?? "";
                        if (!string.IsNullOrWhiteSpace(targetPlayer))
                            Ioc.Default.GetRequiredService<IScriptPlayer>().Goto(targetPlayer);
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_LOAD_SCRIPT)
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_script.txt");
                    if (System.IO.File.Exists(tempFile))
                    {
                        string targetScript = System.IO.File.ReadAllText(tempFile);
                        if (!string.IsNullOrWhiteSpace(targetScript) && System.IO.File.Exists(targetScript))
                        {
                            var sm = Ioc.Default.GetRequiredService<Skua.App.Avalonia.ViewModels.ScriptLoaderViewModel>();
                            Dispatcher.UIThread.Post(() =>
                            {
                                if (sm.LoadScriptCommand.CanExecute(targetScript))
                                    sm.LoadScriptCommand.Execute(targetScript);
                            });
                        }
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_SET_OPTION)
        {
            int optionId = wParam.ToInt32();
            bool value = lParam.ToInt32() == 1;
            var options = Ioc.Default.GetRequiredService<IScriptOption>();
            switch (optionId)
            {
                case 1: options.LagKiller = value; break;
                case 2: options.HeadlessMode = value; break;
                case 3: options.HidePlayers = value; break;
                case 4: options.DisableFX = value; break;
                case 5: options.InfiniteRange = value; break;
                case 6: options.Magnetise = value; break;
                case 7: options.SkipCutscenes = value; break;
                case 8: options.UseFunctionBasedSkills = value; break;
                case 9: options.StreamerMode = value; break;
                case 10: options.AggroMonsters = value; break;
                case 99:
                    var sm = Ioc.Default.GetRequiredService<Skua.App.Avalonia.ViewModels.ScriptLoaderViewModel>();
                    var scheduler = Ioc.Default.GetRequiredService<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel>();
                    if (value)
                    {
                        if (!sm.ScriptManager.ScriptRunning)
                        {
                            if (sm.ToggleScriptCommand.CanExecute(null))
                                sm.ToggleScriptCommand.Execute(null);
                        }
                    }
                    else
                    {
                        if (scheduler.IsRunningQueue && scheduler.StopQueueCommand.CanExecute(null))
                            scheduler.StopQueueCommand.Execute(null);
                        else if (sm.ScriptManager.ScriptRunning && sm.ToggleScriptCommand.CanExecute(null))
                            sm.ToggleScriptCommand.Execute(null);
                    }
                    break;
            }
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_ARMY_SCHEDULER)
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_playlist.json");
                    if (System.IO.File.Exists(tempFile))
                    {
                        var data = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel.SavedScriptItem>>(System.IO.File.ReadAllText(tempFile));
                        var scheduler = Ioc.Default.GetRequiredService<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel>();
                        Dispatcher.UIThread.Post(() =>
                        {
                            scheduler.ScriptQueue.Clear();
                            if (data != null)
                            {
                                foreach (var item in data)
                                {
                                    if (System.IO.File.Exists(item.Path))
                                    {
                                        var vm = new Skua.Shared.Avalonia.ViewModels.ScriptItemViewModel(item.Path) { Id = item.Id };
                                        if (!string.IsNullOrEmpty(item.Name)) vm.Name = item.Name;
                                        scheduler.ScriptQueue.Add(vm);
                                    }
                                }
                            }
                            if (scheduler.ScriptQueue.Count > 0 && !scheduler.IsRunningQueue)
                            {
                                if (scheduler.StartQueueCommand.CanExecute(null))
                                    scheduler.StartQueueCommand.Execute(null);
                            }
                        });
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_ARMY_SCHEDULER_STOP)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var scheduler = Ioc.Default.GetRequiredService<Skua.Shared.Avalonia.ViewModels.ScriptSchedulerViewModel>();
                if (scheduler.StopQueueCommand.CanExecute(null))
                    scheduler.StopQueueCommand.Execute(null);
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_CHECK_LOGIN)
        {
            var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
            return new IntPtr(bot.Player.LoggedIn ? 1 : 0);
        }
        else if (uMsg == 0x0400 + 458) // WM_SKUA_ACCEPT_QUEST
        {
            Task.Run(() => 
            {
                try 
                {
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_accept_quest.txt");
                    if (System.IO.File.Exists(tempFile))
                    {
                        string[] lines = System.IO.File.ReadAllLines(tempFile);
                        string questIdRaw = lines.Length > 0 ? lines[0].Trim() : "";
                        string rewardIdRaw = lines.Length > 1 ? lines[1].Trim() : "-1";
                        int rewardId = int.TryParse(rewardIdRaw, out int rId) ? rId : -1;
                        if (int.TryParse(questIdRaw, out int questId) && questId > 0)
                        {
                            var quest = Ioc.Default.GetRequiredService<IScriptQuest>();
                            quest.RegisterQuests((questId, rewardId));
                        }
                    }
                } catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_AUTO_ATTACK)
        {
            bool start = wParam.ToInt32() == 1;
            Dispatcher.UIThread.Post(() =>
            {
                var autoVm = Ioc.Default.GetRequiredService<AutoViewModel>();
                if (start)
                    StrongReferenceMessenger.Default.Send<StartAutoAttackMessage>();
                else
                    StrongReferenceMessenger.Default.Send<StopAutoMessage>();
            });
            return IntPtr.Zero;
        }
        else if (uMsg == 0x0400 + 452) // WM_SKUA_THROTTLE
        {
            bool throttle = wParam.ToInt32() == 1;
            var options = Ioc.Default.GetRequiredService<IScriptOption>();
            if (!options.HeadlessMode)
            {
                var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
                try
                {
                    if (throttle)
                    {
                        if (_preThrottleFPS <= 0)
                        {
                            int currentFPS = bot.Flash?.GetGameObject<int>("stage.frameRate") ?? -1;
                            if (currentFPS > 2)
                                _preThrottleFPS = currentFPS;
                            string? currentQuality = bot.Flash?.GetGameObject<string>("stage.quality");
                            if (!string.IsNullOrEmpty(currentQuality))
                                _preThrottleQuality = currentQuality;
                        }

                        bot.Flash?.SetGameObject("stage.frameRate", 2);
                        bot.Flash?.SetGameObject("stage.quality", "LOW");
                        Skua.Core.Utils.MemoryUtils.TrimWorkingSet();
                    }
                    else
                    {
                        int currentFPS = bot.Flash?.GetGameObject<int>("stage.frameRate") ?? -1;
                        int targetFPS = _preThrottleFPS > 2 ? _preThrottleFPS : (currentFPS > 2 ? currentFPS : options.SetFPS);
                        string? targetQuality = _preThrottleQuality;
                        
                        _preThrottleFPS = -1;
                        _preThrottleQuality = null;

                        if (targetFPS > 0)
                            bot.Flash?.SetGameObject("stage.frameRate", targetFPS);
                        if (!string.IsNullOrEmpty(targetQuality))
                            bot.Flash?.SetGameObject("stage.quality", targetQuality);
                    }
                }
                catch { }
            }
            return IntPtr.Zero;
        }
        else if (uMsg == WM_SKUA_OPEN_BANK)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Ioc.Default.GetRequiredService<IScriptBank>().Open();
                }
                catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == HotKeys.WM_SKUA_EXECUTE_HOTKEY)
        {
            int actionId = wParam.ToInt32();
            string? actionName = HotKeys.GetActionNameById(actionId);
            if (!string.IsNullOrEmpty(actionName))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (actionName == "OpenBank")
                            Ioc.Default.GetRequiredService<IScriptBank>().Open();
                        else
                            HotKeys.ExecuteHotkeyActionDirect(actionName);
                    }
                    catch { }
                });
            }
            return IntPtr.Zero;
        }
        else if (uMsg == HotKeys.WM_SKUA_RELOAD_HOTKEYS)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    Ioc.Default.GetRequiredService<IHotKeyService>().Reload();
                }
                catch { }
            });
            return IntPtr.Zero;
        }
        else if (uMsg == 0x004A /* WM_COPYDATA */)
        {
            try
            {
                if (lParam != IntPtr.Zero)
                {
                    var cds = Marshal.PtrToStructure<HotKeys.COPYDATASTRUCT>(lParam);
                    if (cds.dwData == (IntPtr)0x484B && cds.lpData != IntPtr.Zero)
                    {
                        string? actionName = Marshal.PtrToStringUni(cds.lpData);
                        if (!string.IsNullOrEmpty(actionName))
                        {
                            Dispatcher.UIThread.Post(() =>
                            {
                                try
                                {
                                    if (actionName == "OpenBank")
                                        Ioc.Default.GetRequiredService<IScriptBank>().Open();
                                    else
                                        HotKeys.ExecuteHotkeyActionDirect(actionName);
                                }
                                catch { }
                            });
                            return (IntPtr)1;
                        }
                    }
                }
            }
            catch { }
        }
        
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    // Avalonia's Position setter reads the new monitor's DPI into the window without raising ScalingChanged, so
    // the mouse can be mapped at one scale while layout and drawing use another (clicks land beside the buttons).
    // When either scale disagrees with the DPI Windows gives this window, replay WM_DPICHANGED with that DPI and
    // the current rect: Avalonia then updates both, and the window keeps its size.
    private void SyncScalingWithWindowDpi(IntPtr hWnd)
    {
        _scalingCheckQueued = false;
        uint dpi = GetDpiForWindow(hWnd);
        if (dpi == 0 || !GetWindowRect(hWnd, out RECT current))
            return;

        // On Windows, DesktopScaling is the scale Avalonia maps the mouse with; RenderScaling is the one layout uses.
        double windowScaling = dpi / 96.0;
        double inputScaling = DesktopScaling;
        if (Math.Abs(RenderScaling - windowScaling) < 0.001 && Math.Abs(inputScaling - windowScaling) < 0.001)
            return;

        Trace.WriteLine($"VibeSkua scaling resync: layout {RenderScaling}, input {inputScaling}, window DPI {dpi}");
        IntPtr rect = Marshal.AllocHGlobal(Marshal.SizeOf<RECT>());
        try
        {
            Marshal.StructureToPtr(current, rect, false);
            SendMessage(hWnd, WM_DPICHANGED, new IntPtr((int)((dpi << 16) | dpi)), rect);
        }
        finally
        {
            Marshal.FreeHGlobal(rect);
        }
    }
#endif
}
