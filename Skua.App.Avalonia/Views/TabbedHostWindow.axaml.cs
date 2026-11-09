using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Messaging;
using Material.Icons;
using Material.Icons.Avalonia;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;
using Skua.Core.AppStartup;
using Skua.Shared.Avalonia.ViewModels;
using Skua.Shared.Avalonia.ViewModels.ScriptRepo;
using Skua.Shared.Avalonia.ViewModels.Dialogs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Skua.App.Avalonia.Views;

public partial class TabbedHostWindow : Window
{
    #region Win32 API

    public delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, nuint uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    public static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    private SubclassProc? _subclassProc;
    private const int WM_SKUA_CHILD_READY = 0x0400 + 440;
    private const int WM_SKUA_CHILD_DPICHANGED = 0x0400 + 441;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    public static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    public static IntPtr SetWindowLongAny(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8) return SetWindowLongPtr(hWnd, nIndex, dwNewLong);
        return new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr BeginDeferWindowPos(int nNumWindows);

    [DllImport("user32.dll")]
    public static extern IntPtr DeferWindowPos(IntPtr hWinPosInfo, IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool EndDeferWindowPos(IntPtr hWinPosInfo);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    private const int WM_NCACTIVATE = 0x0086;
    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    private const uint WM_CLOSE = 0x0010;
    private const int GWL_HWNDPARENT = -8;
    private const int GWL_EXSTYLE = -20;
    private const uint GW_OWNER = 4;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_SHOW = 5;
    private const int SW_HIDE = 0;
    private const uint SWP_NOMOVE = 0x0001;
    private const uint SWP_NOSIZE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_ASYNCWINDOWPOS = 0x4000;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint WM_ENTERSIZEMOVE = 0x0231;
    private const uint WM_EXITSIZEMOVE = 0x0232;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInformationClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    private static IntPtr _childJobHandle = IntPtr.Zero;

    private static void EnsureChildJobObject()
    {
        if (_childJobHandle != IntPtr.Zero) return;
        try
        {
            _childJobHandle = CreateJobObject(IntPtr.Zero, null);
            if (_childJobHandle != IntPtr.Zero)
            {
                var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
                info.BasicLimitInformation.LimitFlags = 0x2000; // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
                IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
                try
                {
                    Marshal.StructureToPtr(info, extendedInfoPtr, false);
                    SetInformationJobObject(_childJobHandle, 9 /* JobObjectExtendedLimitInformation */, extendedInfoPtr, (uint)length);
                }
                finally
                {
                    Marshal.FreeHGlobal(extendedInfoPtr);
                }
            }
        }
        catch { }
    }

    #endregion Win32 API

    #region Tab State

    private class TabInfo
    {
        public Process Process { get; set; } = null!;
        public IntPtr ChildHwnd { get; set; } = IntPtr.Zero;
        public bool IsThrottled { get; set; } = false;
        public bool IsCustomNamed { get; set; } = false;
        public string? StreamerModeOriginalTitle { get; set; }
    }

    private static readonly object ChildProcessExitLogLock = new();

    private readonly string[] _initialArgs;
    private bool _initialInstanceStarted = false;
    private readonly Dictionary<TabItem, TabInfo> _tabs = new();
    private bool _needsReposition = false;
    private bool _isRepositioning = false;
    private bool _isTogglingGridView = false;
    private DateTime _lastGridViewToggleTime = DateTime.MinValue;
    private readonly TimeSpan _gridViewToggleCooldown = TimeSpan.FromMilliseconds(300);
    private bool _isClosing = false;
    private bool _streamerModeEnabled;
    private readonly CancellationTokenSource _pipeCts = new();
    private bool _isGridViewEnabled = false;
    private GridLayoutMode _gridLayoutMode = GridLayoutMode.Auto;
    private TabItem? _lastSelectedTab;

    // Game size presets inspired by Aeoniian's VibeSkuffle; this implementation is original.
    private static readonly (int Width, int Height)[] GameSizes =
    {
        (640, 367),
        (960, 550),
        (1280, 720),
        (1600, 900),
        (1920, 1080)
    };

    private (int Width, int Height)? _gameSize;

    private static (int Width, int Height)? ParseGameSize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
            return null;

        string[] parts = value.Split('x', StringSplitOptions.TrimEntries);
        if (parts.Length == 2
            && int.TryParse(parts[0], out int width)
            && int.TryParse(parts[1], out int height)
            && width is >= 320 and <= 7680
            && height is >= 180 and <= 4320)
            return (width, height);

        return null;
    }

    private static (int X, int Y, int Width, int Height) FitGameSize(int x, int y, int width, int height, (int Width, int Height)? size)
    {
        if (size is not { } target || width <= 0 || height <= 0)
            return (x, y, width, height);

        double scale = Math.Min(1d, Math.Min((double)width / target.Width, (double)height / target.Height));
        int gameWidth = Math.Max((int)(target.Width * scale), 1);
        int gameHeight = Math.Max((int)(target.Height * scale), 1);
        return (x + (width - gameWidth) / 2, y + (height - gameHeight) / 2, gameWidth, gameHeight);
    }

    private void UpdateGameSizeButton()
    {
        GameSizeText.Text = _gameSize is { } size ? $"Size: {size.Width}x{size.Height}" : "Size: Auto";
        ToolTip.SetTip(GameSizeBorder, _gameSize is { } selected
            ? $"Game size: {selected.Width} x {selected.Height}."
            : "Game size: fills the available client area.");

        GameSizeAutoMenuItem.IsChecked = _gameSize == null;
        GameSize640MenuItem.IsChecked = _gameSize == GameSizes[0];
        GameSize960MenuItem.IsChecked = _gameSize == GameSizes[1];
        GameSize1280MenuItem.IsChecked = _gameSize == GameSizes[2];
        GameSize1600MenuItem.IsChecked = _gameSize == GameSizes[3];
        GameSize1920MenuItem.IsChecked = _gameSize == GameSizes[4];
    }

    private void GameSizeBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        FlyoutBase.ShowAttachedFlyout(GameSizeBorder);
    }

    private void GameSizeMenuItem_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || menuItem.Tag is not string value)
            return;

        _gameSize = ParseGameSize(value);

        Ioc.Default.GetService<ISettingsService>()?.Set("VibeSkuaGameSize", _gameSize is { } selected
            ? $"{selected.Width}x{selected.Height}"
            : "auto");

        UpdateGameSizeButton();
        _lastX = int.MinValue;
        _lastY = int.MinValue;
        _lastW = int.MinValue;
        _lastH = int.MinValue;
        ScheduleReposition();
    }

    private enum GridLayoutMode
    {
        Auto,
        Horizontal,
        Vertical
    }
    private IntPtr _hostHwnd = IntPtr.Zero;
    private int _lastX = -99999, _lastY = -99999, _lastW = -99999, _lastH = -99999;
    private IntPtr _lastActiveHwnd = IntPtr.Zero;
    private bool _lastGridViewState = false;
    private int _childOffsetX = 0;
    private int _childOffsetY = 0;
    private int _childWidth = 0;
    private int _childHeight = 0;
    private int _lastFastX = -99999;
    private int _lastFastY = -99999;
    private bool _isDraggingWindow = false;

    private struct GridTile
    {
        public IntPtr Hwnd;
        public int OffsetX;
        public int OffsetY;
        public int Width;
        public int Height;
    }

    private readonly List<GridTile> _gridTiles = new();

    private Queue<Action> _spawnQueue = new Queue<Action>();
    private bool _isSpawning = false;

    private void EnqueueSpawn(Action spawnAction)
    {
        _spawnQueue.Enqueue(spawnAction);
        ProcessNextSpawn();
    }

    private void ProcessNextSpawn()
    {
        if (_isSpawning || _spawnQueue.Count == 0 || _isClosing) return;

        _isSpawning = true;
        Action next = _spawnQueue.Dequeue();
        try
        {
            next.Invoke();
        }
        catch (Exception ex)
        {
            _isSpawning = false;
            Trace.WriteLine($"VibeSkua child spawn failed: {ex}");
            Dispatcher.UIThread.Post(ProcessNextSpawn, DispatcherPriority.Background);
        }
    }

    private void RecordClientLocation(int pid, ArmyClientLocation location)
    {
        _latestClientLocations[pid] = location;
        string signature = $"{location.Map}|{location.Room}";
        if (_lastRecordedLocationByPid.TryGetValue(pid, out string? previousSignature)
            && previousSignature.Equals(signature, StringComparison.OrdinalIgnoreCase))
            return;

        _lastRecordedLocationByPid[pid] = signature;
        _jumpMapHistory.RemoveAll(item =>
            item.Map.Equals(location.Map, StringComparison.OrdinalIgnoreCase)
            && item.Room.Equals(location.Room, StringComparison.OrdinalIgnoreCase));

        _jumpMapHistory.Insert(0, new ArmyJumpDestination
        {
            Name = location.Username,
            Map = location.Map,
            Room = location.Room,
            Cell = string.IsNullOrWhiteSpace(location.Cell) ? "Enter" : location.Cell,
            Pad = string.IsNullOrWhiteSpace(location.Pad) ? "Spawn" : location.Pad
        });

        if (_jumpMapHistory.Count > 15)
            _jumpMapHistory.RemoveRange(15, _jumpMapHistory.Count - 15);

        SaveArmyJumpSettings();
    }

    #endregion Tab State

    public TabbedHostWindow() : this(Array.Empty<string>())
    {
    }

    public TabbedHostWindow(string rawInitialArgs) : this(ParseRawCommandLine(rawInitialArgs))
    {
    }

    public TabbedHostWindow(string[]? initialArgs)
    {
        _initialArgs = initialArgs ?? Array.Empty<string>();
        InitializeComponent();
        _gameSize = ParseGameSize(Ioc.Default.GetService<ISettingsService>()?.Get("VibeSkuaGameSize", "auto"));
        UpdateGameSizeButton();
        LoadArmyJumpSettings();
        Title = $"VibeSkua V{Skua.Core.Models.ClientFileSources.DisplayVersion}";
        MainTitleBar.TitleText = Title;

        try
        {
            IThemeService? themeService = Ioc.Default.GetService<IThemeService>();
            Skua.Shared.Avalonia.Controls.Shell.WindowTitleBar.SetArtworkPath(
                Ioc.Default.GetRequiredService<ISettingsService>().GetShared().TitleBarArtworkPath);

            if (themeService is Skua.Shared.Avalonia.Services.AvaloniaThemeService avaloniaTheme)
            {
                MainTitleBar.UseAccentBackground = avaloniaTheme.UseAccentOnMainTitleBar;
                Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(this, themeService.IsDarkTheme,
                    avaloniaTheme.UseAccentOnMainTitleBar ? avaloniaTheme.PrimaryColor : (Color?)null,
                    avaloniaTheme.UseAccentOnMainTitleBar ? avaloniaTheme.PrimaryForegroundColor : (Color?)null);
            }
            else
            {
                MainTitleBar.UseAccentBackground = false;
                Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(this, themeService?.IsDarkTheme);
            }
        }
        catch { }

        var options = Ioc.Default.GetRequiredService<IScriptOption>();
        _streamerModeEnabled = options.StreamerMode;
        MiscOptionsMenu.DataContext = options;
        options.PropertyChanged += Options_PropertyChanged;

        Opened += TabbedHostWindow_Opened;
        PositionChanged += (s, e) => OnWindowPositionChanged(e.Point);
        SizeChanged += (s, e) => { ScheduleReposition(); UpdateTabScrollButtons(); };
        ScalingChanged += (s, e) => Dispatcher.UIThread.Post(RepositionWithFreshCache, DispatcherPriority.Loaded);

        PropertyChanged += (s, e) => { if (e.Property == WindowStateProperty || e.Property == IsVisibleProperty) ScheduleReposition(); };
        Activated += (s, e) => { ScheduleReposition(); UpdateTabScrollButtons(); };
        Closing += OnWindowClosing;
        Closed += OnWindowClosed;

        StrongReferenceMessenger.Default.Register<TabbedHostWindow, LoadScriptMessage, int>(this, (int)MessageChannels.ScriptStatus, (r, m) => r.BroadcastScriptAction(m.Path, WM_SKUA_LOAD_SCRIPT));
        StrongReferenceMessenger.Default.Register<TabbedHostWindow, ScriptSchedulerViewModel.ArmySchedulerMessage>(this, (r, m) => r.OnArmySchedulerStart(m));
        StrongReferenceMessenger.Default.Register<TabbedHostWindow, ScriptSchedulerViewModel.ArmySchedulerStopMessage>(this, (r, m) => r.OnArmySchedulerStop(m));

        StartPipeServer();

        TabScrollViewer.PointerWheelChanged += (s, e) =>
        {
            if (e.Delta.Y != 0)
            {
                var newOffset = TabScrollViewer.Offset.X - (e.Delta.Y * 80);
                TabScrollViewer.Offset = new Vector(Math.Max(0, newOffset), 0);
                UpdateTabScrollButtons();
                e.Handled = true;
            }
        };

        TabScrollViewer.PropertyChanged += (s, e) =>
        {
            if (e.Property == ScrollViewer.OffsetProperty ||
                e.Property == ScrollViewer.ExtentProperty ||
                e.Property == ScrollViewer.ViewportProperty)
            {
                UpdateTabScrollButtons();
            }
        };

        var titleSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        titleSyncTimer.Tick += (s, e) => SyncTabTitles();
        titleSyncTimer.Start();

        GridViewBorder.PointerEntered += GridViewBorder_PointerEntered;
        GridViewBorder.PointerExited += GridViewBorder_PointerExited;
        GlobeBorder.PointerEntered += GlobeBorder_PointerEntered;
        GlobeBorder.PointerExited += GlobeBorder_PointerExited;
    }

    private void SyncTabTitles()
    {
        if (_isClosing || _tabs.Count == 0) return;

        var entries = _tabs.Select(kvp => (Tab: kvp.Key, Pid: kvp.Value?.Process?.Id ?? 0)).ToList();

        Task.Run(() =>
        {
            string dir = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
            if (!Directory.Exists(dir)) return;

            var updates = new List<(TabItem Tab, string Username)>();
            var locations = new List<(int Pid, ArmyClientLocation Location)>();

            foreach (var (tab, pid) in entries)
            {
                if (pid == 0) continue;
                try
                {
                    string file = Path.Combine(dir, $"{pid}.txt");
                    if (File.Exists(file))
                    {
                        string username = File.ReadAllText(file).Trim();
                        if (!string.IsNullOrWhiteSpace(username))
                            updates.Add((tab, username));
                    }

                    string locationFile = Path.Combine(dir, $"{pid}.location.json");
                    if (File.Exists(locationFile))
                    {
                        ArmyClientLocation? location = JsonSerializer.Deserialize<ArmyClientLocation>(File.ReadAllText(locationFile));
                        if (location != null
                            && !string.IsNullOrWhiteSpace(location.Map)
                            && location.UpdatedUtc >= DateTime.UtcNow.AddSeconds(-5))
                            locations.Add((pid, location));
                    }
                }
                catch { }
            }

            if ((updates.Count > 0 || locations.Count > 0) && !_isClosing)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_isClosing) return;

                    foreach (var (pid, location) in locations)
                        RecordClientLocation(pid, location);

                    foreach (var (tab, username) in updates)
                    {
                        if (tab.Header is StackPanel headerPanel)
                        {
                            TextBlock? titleBlock = headerPanel.Children.OfType<TextBlock>().FirstOrDefault(tb => tb.Text != "✕");
                            TextBox? editBox = headerPanel.Children.OfType<TextBox>().FirstOrDefault();

                            if (_streamerModeEnabled)
                            {
                                if (titleBlock != null) titleBlock.Text = "VibeSkuaUser";
                                if (editBox != null) editBox.Text = "VibeSkuaUser";
                                continue;
                            }

                            if (_tabs.TryGetValue(tab, out TabInfo? tInfo) && tInfo.IsCustomNamed)
                                continue;

                        if (titleBlock != null && (titleBlock.Text.StartsWith("User ") || titleBlock.Text.StartsWith("Skua ") || titleBlock.Text != username))
                        {
                            titleBlock.Text = username;
                            if (editBox != null) editBox.Text = username;
                        }
                    }
                }, DispatcherPriority.Background);
            }
        });
    }

    #region Lifecycle

    private void TabbedHostWindow_Opened(object? sender, EventArgs e)
    {
        _hostHwnd = this.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (_hostHwnd != IntPtr.Zero)
        {
            HotKeys.HostHwnd = _hostHwnd;
            _subclassProc = HostWindowSubclassProc;
            SetWindowSubclass(_hostHwnd, _subclassProc, 1, IntPtr.Zero);
        }

        HotKeys.RegisterSkuaPid((uint)Environment.ProcessId);
        HotKeys.GetChildHwndForPid = (pid) =>
        {
            var tab = _tabs.Values.FirstOrDefault(t => t.Process != null && (uint)t.Process.Id == pid);
            return tab?.ChildHwnd ?? IntPtr.Zero;
        };

        try
        {
            var themeService = Ioc.Default.GetService<IThemeService>();
            if (themeService != null)
            {
                void RefreshNativeTitleBar()
                {
                    if (themeService is Skua.Shared.Avalonia.Services.AvaloniaThemeService currentTheme)
                    {
                        MainTitleBar.UseAccentBackground = currentTheme.UseAccentOnMainTitleBar;
                        Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(this, themeService.IsDarkTheme,
                            currentTheme.UseAccentOnMainTitleBar ? currentTheme.PrimaryColor : (Color?)null,
                            currentTheme.UseAccentOnMainTitleBar ? currentTheme.PrimaryForegroundColor : (Color?)null);
                    }
                    else
                    {
                        MainTitleBar.UseAccentBackground = false;
                        Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(this, themeService.IsDarkTheme);
                    }
                }

                // Preset changes and single-scheme edits use separate events. Listen to both so
                // changing a custom primary/foreground color cannot leave the native caption stale.
                themeService.ThemeChanged += _ => Dispatcher.UIThread.Post(RefreshNativeTitleBar, DispatcherPriority.Background);
                themeService.SchemeChanged += (_, _) => Dispatcher.UIThread.Post(RefreshNativeTitleBar, DispatcherPriority.Background);
                if (themeService is Skua.Shared.Avalonia.Services.AvaloniaThemeService avaloniaTheme)
                    Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(this, themeService.IsDarkTheme,
                            avaloniaTheme.UseAccentOnMainTitleBar ? avaloniaTheme.PrimaryColor : (Color?)null,
                            avaloniaTheme.UseAccentOnMainTitleBar ? avaloniaTheme.PrimaryForegroundColor : (Color?)null);
                else
                    Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(this, themeService.IsDarkTheme);
            }
        }
        catch { }

        try
        {
            Ioc.Default.GetRequiredService<IHotKeyService>().Reload();
        }
        catch { }

        // Avalonia can raise Opened again after a hidden window is shown. Only launch
        // the startup instance once; subsequent tray restores must reuse existing tabs.
        if (!_initialInstanceStarted)
        {
            _initialInstanceStarted = true;
            AddNewInstance(_initialArgs);
        }
    }

    private IntPtr HostWindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, nuint uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == HotKeys.WM_SKUA_RELOAD_HOTKEYS)
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

        if (uMsg == HotKeys.WM_SKUA_LOAD_SCRIPT_ALL)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var windowService = Ioc.Default.GetRequiredService<IWindowService>();
                    windowService.RegisterManagedWindow("Script Repo", Ioc.Default.GetRequiredService<ScriptRepoViewModel>());
                    windowService.ShowManagedWindow("Script Repo");
                }
                catch { }
            });
            return IntPtr.Zero;
        }

        if (uMsg == 0x001A /* WM_SETTINGCHANGE */ || uMsg == 0x031A /* WM_THEMECHANGED */ || uMsg == 0x0320 /* WM_DWMCOLORIZATIONCOLORCHANGED */)
        {
            IntPtr res = DefSubclassProc(hWnd, uMsg, wParam, lParam);
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    var themeService = Ioc.Default.GetService<IThemeService>();
                    bool isDark = themeService?.IsDarkTheme ?? true;
                    if (themeService is Skua.Shared.Avalonia.Services.AvaloniaThemeService avaloniaTheme)
                        Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(this, isDark,
                            avaloniaTheme.UseAccentOnMainTitleBar ? avaloniaTheme.PrimaryColor : (Color?)null,
                            avaloniaTheme.UseAccentOnMainTitleBar ? avaloniaTheme.PrimaryForegroundColor : (Color?)null);
                    else
                        Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(this, isDark);
                }
                catch { }
            });
            return res;
        }

        if (uMsg == WM_NCACTIVATE)
        {
            if (wParam == IntPtr.Zero)
            {
                IntPtr fg = GetForegroundWindow();
                if (fg != IntPtr.Zero)
                {
                    GetWindowThreadProcessId(fg, out uint fgPid);
                    uint currentPid = (uint)Environment.ProcessId;
                    if (fgPid == currentPid || _tabs.Values.Any(t => t.Process != null && !t.Process.HasExited && (uint)t.Process.Id == fgPid))
                    {
                        return DefSubclassProc(hWnd, uMsg, (IntPtr)1, lParam);
                    }
                }
            }
        }

        if (uMsg == WM_SKUA_CHILD_READY)
        {
            IntPtr childHwnd = wParam;
            int pid = lParam.ToInt32();

            Dispatcher.UIThread.Post(() =>
            {
                if (_isClosing || childHwnd == IntPtr.Zero) return;

                TabInfo? targetInfo = _tabs.Values.FirstOrDefault(t => t.Process != null && t.Process.Id == pid);
                if (targetInfo != null)
                {
                    AttachChildWindow(targetInfo, childHwnd);
                }
            });
            return IntPtr.Zero;
        }

        // A child moved onto a monitor with another scale may have resized itself. Its new size is invisible to the
        // position cache, so place it again.
        if (uMsg == WM_SKUA_CHILD_DPICHANGED)
        {
            Dispatcher.UIThread.Post(RepositionWithFreshCache, DispatcherPriority.Loaded);
            return IntPtr.Zero;
        }

        if (uMsg == WM_ENTERSIZEMOVE)
        {
            IntPtr res = DefSubclassProc(hWnd, uMsg, wParam, lParam);
            _isDraggingWindow = false;
            _lastX = -99999;
            _lastY = -99999;
            DoReposition();
            _isDraggingWindow = true;
            return res;
        }

        if (uMsg == WM_EXITSIZEMOVE || uMsg == 0x0215 /* WM_CAPTURECHANGED */)
        {
            IntPtr res = DefSubclassProc(hWnd, uMsg, wParam, lParam);
            _isDraggingWindow = false;
            _lastX = -99999;
            _lastY = -99999;
            DoReposition();
            return res;
        }

        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private void AttachChildWindow(TabInfo info, IntPtr childHwnd)
    {
        if (info.ChildHwnd != IntPtr.Zero) return;

        info.ChildHwnd = childHwnd;
        if (info.Process != null)
            HotKeys.RegisterSkuaPid((uint)info.Process.Id);
        if (HotKeys.ActiveChildHwnd == IntPtr.Zero)
            HotKeys.ActiveChildHwnd = childHwnd;

        int exStyle = GetWindowLong(childHwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_TOOLWINDOW;
        SetWindowLong(childHwnd, GWL_EXSTYLE, exStyle);
        SetWindowLongAny(childHwnd, GWL_HWNDPARENT, _hostHwnd);

        if (_isGridViewEnabled)
        {
            PostMessage(childHwnd, WM_SKUA_GRIDVIEW, new IntPtr(1), IntPtr.Zero);
        }

        DoReposition();

        _isSpawning = false;
        ProcessNextSpawn();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _isClosing = true;
        App.DisposeTrayIcon();
        try { _pipeCts.Cancel(); } catch { }

        var tabsToClose = _tabs.Values.ToList();
        _tabs.Clear();

        foreach (var info in tabsToClose)
        {
            try
            {
                if (info.ChildHwnd != IntPtr.Zero)
                    ShowWindow(info.ChildHwnd, SW_HIDE);
                if (!info.Process.HasExited)
                {
                    info.Process.Kill();
                    info.Process.WaitForExit(500);
                }
                try { File.Delete(Path.Combine(Path.GetTempPath(), "VibeSkuaTabs", $"{info.Process.Id}.txt")); } catch { }
                try { File.Delete(Path.Combine(Path.GetTempPath(), "VibeSkuaTabs", $"{info.Process.Id}.location.json")); } catch { }
                _latestClientLocations.Remove(info.Process.Id);
                _lastRecordedLocationByPid.Remove(info.Process.Id);
            }
            catch { }
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        App.DisposeTrayIcon();
        try
        {
            App.AppMutex?.ReleaseMutex();
            App.AppMutex?.Dispose();
            App.AppMutex = null;
        }
        catch { }

        Task.Run(async () =>
        {
            await Task.Delay(300);
            Environment.Exit(0);
        });
    }

    #endregion Lifecycle

    #region Pipe Server

    private void StartPipeServer()
    {
        Task.Run(async () =>
        {
            while (!_isClosing && !_pipeCts.Token.IsCancellationRequested)
            {
                NamedPipeServerStream? pipeServer = null;
                try
                {
                    pipeServer = new NamedPipeServerStream(
                        App.HostPipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    using var reg = _pipeCts.Token.Register(() =>
                    {
                        try { pipeServer?.Dispose(); } catch { }
                    });

                    await pipeServer.WaitForConnectionAsync(_pipeCts.Token);

                    using (pipeServer)
                    using (StreamReader sr = new(pipeServer))
                    {
                        string? line = await sr.ReadLineAsync();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            if (line.StartsWith("ARTWORK|", StringComparison.Ordinal))
                            {
                                string encodedPath = line.Substring("ARTWORK|".Length);
                                Dispatcher.UIThread.Post(() =>
                                {
                                    try
                                    {
                                        string artworkPath = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encodedPath));
                                        Skua.Shared.Avalonia.Controls.Shell.WindowTitleBar.SetArtworkPath(
                                            string.IsNullOrWhiteSpace(artworkPath) ? null : artworkPath);
                                    }
                                    catch (Exception ex)
                                    {
                                        Trace.WriteLine($"VibeSkua artwork sync failed: {ex.GetType().Name}: {ex.Message}");
                                    }
                                });
                            }
                            else if (line.StartsWith("THEME|", StringComparison.Ordinal))
                            {
                                string themePayload = line.Substring("THEME|".Length);
                                bool useAccentOnMainTitleBar = false;
                                string themeCsv = themePayload;
                                int separatorIndex = themePayload.IndexOf('|');
                                if (separatorIndex >= 0 &&
                                    bool.TryParse(themePayload[..separatorIndex], out bool parsedAccentSetting))
                                {
                                    useAccentOnMainTitleBar = parsedAccentSetting;
                                    themeCsv = themePayload[(separatorIndex + 1)..];
                                }

                                Dispatcher.UIThread.Post(() =>
                                {
                                    try
                                    {
                                        Skua.Shared.Avalonia.Services.ThemeItem? themeItem =
                                            Skua.Shared.Avalonia.Services.ThemeItem.FromString(themeCsv);
                                        if (themeItem is not null &&
                                            Ioc.Default.GetService<IThemeService>() is Skua.Shared.Avalonia.Services.AvaloniaThemeService themeService)
                                        {
                                            themeService.ApplyExternalThemeSnapshot(themeItem, useAccentOnMainTitleBar);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Trace.WriteLine($"VibeSkua theme sync failed: {ex.GetType().Name}: {ex.Message}");
                                    }
                                });
                            }
                            else
                            {
                                string[]? parsedArgs = null;
                                try
                                {
                                    if (line.TrimStart().StartsWith("["))
                                    {
                                        parsedArgs = System.Text.Json.JsonSerializer.Deserialize<string[]>(line);
                                    }
                                }
                                catch { }

                                parsedArgs ??= ParseRawCommandLine(line);
                                Dispatcher.UIThread.Post(() => AddNewInstance(parsedArgs));
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    try { pipeServer?.Dispose(); } catch { }
                    break;
                }
                catch
                {
                    try { pipeServer?.Dispose(); } catch { }
                    if (_isClosing || _pipeCts.Token.IsCancellationRequested) break;
                    await Task.Delay(100);
                }
            }
        });
    }

    private static string[] ParseRawCommandLine(string? cmdLine)
    {
        if (string.IsNullOrWhiteSpace(cmdLine))
            return Array.Empty<string>();

        List<string> args = new();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < cmdLine.Length; i++)
        {
            char c = cmdLine[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
            args.Add(current.ToString());

        return args.ToArray();
    }

    #endregion Pipe Server

    #region Tab Switching & Positioning

    private const int WM_SKUA_GRIDVIEW = 0x0400 + 444;
    private const int WM_SKUA_START_SCRIPT = 0x0400 + 445;
    private const int WM_SKUA_STOP_SCRIPT = 0x0400 + 446;
    private const int WM_SKUA_LOGIN = 0x0400 + 447;
    private const int WM_SKUA_LOGOUT = 0x0400 + 448;
    private const int WM_SKUA_JUMP_MAP = 0x0400 + 449;
    private const int WM_SKUA_SET_OPTION = 0x0400 + 450;
    private const int WM_SKUA_THROTTLE = 0x0400 + 452;
    private const int WM_SKUA_LOAD_SCRIPT = 0x0400 + 453;
    private const int WM_SKUA_ARMY_SCHEDULER = 0x0400 + 454;
    private const int WM_SKUA_ARMY_SCHEDULER_STOP = 0x0400 + 455;
    private const int WM_SKUA_CHECK_LOGIN = 0x0400 + 456;
    private const int WM_SKUA_JUMP_PLAYER = 0x0400 + 457;
    private const int WM_SKUA_AUTO_ATTACK = 0x0400 + 459;

    private void OnArmySchedulerStart(ScriptSchedulerViewModel.ArmySchedulerMessage m)
    {
        m.Handled = true;
        try
        {
            var dialogService = Ioc.Default.GetRequiredService<IDialogService>();

            int notLoggedInCount = 0;
            foreach (var info in _tabs.Values)
            {
                if (info.ChildHwnd != IntPtr.Zero)
                {
                    IntPtr res = SendMessage(info.ChildHwnd, (uint)WM_SKUA_CHECK_LOGIN, IntPtr.Zero, IntPtr.Zero);
                    if (res.ToInt32() == 0)
                        notLoggedInCount++;
                }
            }

            if (notLoggedInCount > 0)
            {
                dialogService.ShowMessageBox($"Cannot start Army Scheduler! {notLoggedInCount} account(s) are not logged in. Please log them in first.", "Army Scheduler Error");
                return;
            }

            string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_playlist.json");
            var data = m.Queue.Select(x => new ScriptSchedulerViewModel.SavedScriptItem { Path = x.Path, Id = x.Id, Name = x.Name }).ToList();
            System.IO.File.WriteAllText(tempFile, System.Text.Json.JsonSerializer.Serialize(data));

            foreach (var info in _tabs.Values)
                if (info.ChildHwnd != IntPtr.Zero)
                    PostMessage(info.ChildHwnd, WM_SKUA_ARMY_SCHEDULER, IntPtr.Zero, IntPtr.Zero);

            dialogService.ShowMessageBox("Army Scheduler payload has been broadcasted. All active accounts will now seamlessly rebuild their queues and begin executing the playlist.", "Army Scheduler Started");
        }
        catch { }
    }

    private void OnArmySchedulerStop(ScriptSchedulerViewModel.ArmySchedulerStopMessage m)
    {
        m.Handled = true;
        try
        {
            foreach (var info in _tabs.Values)
                if (info.ChildHwnd != IntPtr.Zero)
                    PostMessage(info.ChildHwnd, WM_SKUA_ARMY_SCHEDULER_STOP, IntPtr.Zero, IntPtr.Zero);

            var dialogService = Ioc.Default.GetRequiredService<IDialogService>();
            dialogService.ShowMessageBox("Stop signal has been successfully broadcasted. All accounts are now halting their schedulers.", "Army Scheduler Stopped");
        }
        catch { }
    }

    private void MenuItem_ArmyScheduler_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var windowService = Ioc.Default.GetRequiredService<IWindowService>();
            windowService.RegisterManagedWindow("Scheduler", Ioc.Default.GetRequiredService<ScriptSchedulerViewModel>());
            windowService.RegisterManagedWindow("Script Repo", Ioc.Default.GetRequiredService<ScriptRepoViewModel>());
            windowService.ShowManagedWindow("Scheduler");
        }
        catch (Exception ex)
        {
            Ioc.Default.GetRequiredService<IDialogService>().ShowMessageBox(ex.ToString(), "Error Opening Scheduler");
        }
    }

    private bool _ignoreSelectionChange = false;

    public void EnterGridView()
    {
        if (_tabs.Count <= 1 || _isTogglingGridView) return;

        _isTogglingGridView = true;
        try
        {
            _lastGridViewToggleTime = DateTime.UtcNow;
            _isGridViewEnabled = true;
            UpdateGridViewBorderColor();

            foreach (var info in _tabs.Values)
            {
                if (info.ChildHwnd != IntPtr.Zero)
                {
                    PostMessage(info.ChildHwnd, WM_SKUA_GRIDVIEW, new IntPtr(1), IntPtr.Zero);
                }
            }

            if (InstancesTabControl.SelectedItem is TabItem currentTab)
                _lastSelectedTab = currentTab;
            _lastSelectedTab ??= InstancesTabControl.Items.OfType<TabItem>().FirstOrDefault();

            _ignoreSelectionChange = true;
            InstancesTabControl.SelectedItem = null;
            _ignoreSelectionChange = false;

            DoReposition();
        }
        finally
        {
            _isTogglingGridView = false;
        }
    }

    public void ExitGridView()
    {
        if (_isTogglingGridView) return;

        _isTogglingGridView = true;
        try
        {
            _lastGridViewToggleTime = DateTime.UtcNow;
            _isGridViewEnabled = false;
            UpdateGridViewBorderColor();

            foreach (var info in _tabs.Values)
            {
                if (info.ChildHwnd != IntPtr.Zero)
                {
                    PostMessage(info.ChildHwnd, WM_SKUA_GRIDVIEW, IntPtr.Zero, IntPtr.Zero);
                }
            }

            _ignoreSelectionChange = true;
            if (_lastSelectedTab != null && InstancesTabControl.Items.Contains(_lastSelectedTab))
            {
                InstancesTabControl.SelectedItem = _lastSelectedTab;
            }
            else
            {
                var firstTab = InstancesTabControl.Items.OfType<TabItem>().FirstOrDefault();
                if (firstTab != null)
                    InstancesTabControl.SelectedItem = firstTab;
            }
            _ignoreSelectionChange = false;

            DoReposition();
        }
        finally
        {
            _isTogglingGridView = false;
        }
    }

    private void GridViewBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;

        if (DateTime.UtcNow - _lastGridViewToggleTime < _gridViewToggleCooldown)
            return;

        if (_isGridViewEnabled)
        {
            ExitGridView();
        }
        else
        {
            if (_tabs.Count <= 1)
            {
                // If only 1 account, Grid View has nothing to tile; guarantee normal tab view
                ExitGridView();
                return;
            }
            EnterGridView();
        }
    }

    // Grid Layout toolbar is temporarily hidden; restore its handler with the XAML control when re-enabled.
    private void MenuItem_GridLayoutAuto_Click(object? sender, RoutedEventArgs e) => SetGridLayoutMode(GridLayoutMode.Auto);

    private void MenuItem_GridLayoutHorizontal_Click(object? sender, RoutedEventArgs e) => SetGridLayoutMode(GridLayoutMode.Horizontal);

    private void MenuItem_GridLayoutVertical_Click(object? sender, RoutedEventArgs e) => SetGridLayoutMode(GridLayoutMode.Vertical);

    private void SetGridLayoutMode(GridLayoutMode mode)
    {
        _gridLayoutMode = mode;
        if (_isGridViewEnabled)
            ScheduleReposition();
    }

    private void GridViewBorder_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (!_isGridViewEnabled)
            GridViewBorder[!Border.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaNavHoverBrush");
    }

    private void GridViewBorder_PointerExited(object? sender, PointerEventArgs e)
    {
        if (!_isGridViewEnabled)
            GridViewBorder[!Border.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaSurfaceAltBrush");
    }

    private void AddTabButton_Click(object? sender, RoutedEventArgs e)
    {
        AddNewInstance();
    }

    private void ScrollTabsLeftButton_Click(object? sender, RoutedEventArgs e)
    {
        if (TabScrollViewer == null) return;
        var newOffset = Math.Max(0, TabScrollViewer.Offset.X - 160);
        TabScrollViewer.Offset = new Vector(newOffset, 0);
        UpdateTabScrollButtons();
    }

    private void ScrollTabsRightButton_Click(object? sender, RoutedEventArgs e)
    {
        if (TabScrollViewer == null) return;
        double maxScroll = Math.Max(0, TabScrollViewer.Extent.Width - TabScrollViewer.Viewport.Width);
        var newOffset = Math.Min(maxScroll, TabScrollViewer.Offset.X + 160);
        TabScrollViewer.Offset = new Vector(newOffset, 0);
        UpdateTabScrollButtons();
    }

    private void UpdateTabScrollButtons()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (TabScrollViewer == null || ScrollTabsLeftButton == null || ScrollTabsRightButton == null)
                return;

            double maxScroll = Math.Max(0, TabScrollViewer.Extent.Width - TabScrollViewer.Viewport.Width);
            if (maxScroll > 2)
            {
                ScrollTabsLeftButton.IsVisible = TabScrollViewer.Offset.X > 1;
                ScrollTabsRightButton.IsVisible = TabScrollViewer.Offset.X < maxScroll - 1;
            }
            else
            {
                ScrollTabsLeftButton.IsVisible = false;
                ScrollTabsRightButton.IsVisible = false;
            }
        }, DispatcherPriority.Normal);
    }

    private void ScrollTabIntoView(TabItem? tab)
    {
        if (tab == null || TabScrollViewer == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var tabBounds = tab.Bounds;
                var currentOffset = TabScrollViewer.Offset.X;
                var viewportWidth = TabScrollViewer.Viewport.Width;

                if (viewportWidth <= 0 || tabBounds.Width <= 0) return;

                if (tabBounds.Left < currentOffset)
                {
                    TabScrollViewer.Offset = new Vector(Math.Max(0, tabBounds.Left), 0);
                }
                else if (tabBounds.Right > currentOffset + viewportWidth)
                {
                    var target = tabBounds.Right - viewportWidth;
                    TabScrollViewer.Offset = new Vector(Math.Max(0, target), 0);
                }
            }
            catch { }
        }, DispatcherPriority.Loaded);
    }

    private void InstancesTabControl_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source != InstancesTabControl || _ignoreSelectionChange) return;

        if (_isGridViewEnabled && InstancesTabControl.SelectedItem != null)
        {
            // ExitGridView restores _lastSelectedTab, so point it at the tab that was just clicked.
            if (InstancesTabControl.SelectedItem is TabItem clickedTab)
                _lastSelectedTab = clickedTab;
            ExitGridView();
            return;
        }

        DoReposition();
        UpdateTabScrollButtons();
        if (InstancesTabControl.SelectedItem is TabItem selTab)
        {
            ScrollTabIntoView(selTab);
        }
    }

    private void GlobeBorder_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
        global::Avalonia.Controls.Primitives.FlyoutBase.ShowAttachedFlyout(GlobeBorder);
    }

    private void GlobeBorder_PointerEntered(object? sender, PointerEventArgs e)
    {
        GlobeBorder[!Border.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaNavHoverBrush");
    }

    private void GlobeBorder_PointerExited(object? sender, PointerEventArgs e)
    {
        GlobeBorder[!Border.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaSurfaceAltBrush");
    }

    private void ApplyStreamerModeToTabHeaders(bool enabled)
    {
        foreach (KeyValuePair<TabItem, TabInfo> entry in _tabs)
        {
            if (entry.Key.Header is not StackPanel headerPanel)
                continue;

            TextBlock? titleBlock = headerPanel.Children.OfType<TextBlock>().FirstOrDefault(block => block.Text != "✕");
            TextBox? editBox = headerPanel.Children.OfType<TextBox>().FirstOrDefault();

            if (enabled)
            {
                if (entry.Value.StreamerModeOriginalTitle == null)
                    entry.Value.StreamerModeOriginalTitle = titleBlock?.Text ?? string.Empty;

                if (titleBlock != null) titleBlock.Text = "VibeSkuaUser";
                if (editBox != null) editBox.Text = "VibeSkuaUser";
                continue;
            }

            if (entry.Value.StreamerModeOriginalTitle is string originalTitle)
            {
                if (entry.Value.IsCustomNamed && titleBlock != null)
                    titleBlock.Text = originalTitle;

                if (editBox != null) editBox.Text = titleBlock?.Text ?? originalTitle;
                entry.Value.StreamerModeOriginalTitle = null;
            }
        }
    }

    private void Options_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var options = sender as IScriptOption;
        if (options == null) return;

        if (e.PropertyName == "StreamerMode")
        {
            _streamerModeEnabled = options.StreamerMode;
            ApplyStreamerModeToTabHeaders(_streamerModeEnabled);
        }

        int optionId = -1;
        bool val = false;

        switch (e.PropertyName)
        {
            case "LagKiller": optionId = 1; val = options.LagKiller; break;
            case "HeadlessMode": optionId = 2; val = options.HeadlessMode; break;
            case "HidePlayers": optionId = 3; val = options.HidePlayers; break;
            case "DisableFX": optionId = 4; val = options.DisableFX; break;
            case "InfiniteRange": optionId = 5; val = options.InfiniteRange; break;
            case "StreamerMode": optionId = 9; val = options.StreamerMode; break;
            case "AggroMonsters": optionId = 10; val = options.AggroMonsters; break;
        }

        if (optionId != -1)
        {
            foreach (var info in _tabs.Values)
                if (info.ChildHwnd != IntPtr.Zero)
                    PostMessage(info.ChildHwnd, WM_SKUA_SET_OPTION, new IntPtr(optionId), new IntPtr(val ? 1 : 0));
        }
    }

    private async void MenuItem_StartAllScripts_Click(object? sender, RoutedEventArgs e)
    {
        if (MenuStartScripts != null) MenuStartScripts.IsEnabled = false;
        if (MenuStopScripts != null) MenuStopScripts.IsEnabled = false;

        foreach (var info in _tabs.Values)
            if (info.ChildHwnd != IntPtr.Zero)
                PostMessage(info.ChildHwnd, WM_SKUA_SET_OPTION, new IntPtr(99), new IntPtr(1));

        await Task.Delay(2000);

        if (MenuStartScripts != null) MenuStartScripts.IsEnabled = true;
        if (MenuStopScripts != null) MenuStopScripts.IsEnabled = true;
    }

    private async void MenuItem_StopAllScripts_Click(object? sender, RoutedEventArgs e)
    {
        if (MenuStartScripts != null) MenuStartScripts.IsEnabled = false;
        if (MenuStopScripts != null) MenuStopScripts.IsEnabled = false;

        foreach (var info in _tabs.Values)
            if (info.ChildHwnd != IntPtr.Zero)
                PostMessage(info.ChildHwnd, WM_SKUA_SET_OPTION, new IntPtr(99), new IntPtr(0));

        await Task.Delay(4000);

        if (MenuStartScripts != null) MenuStartScripts.IsEnabled = true;
        if (MenuStopScripts != null) MenuStopScripts.IsEnabled = true;
    }

    private async void MenuItem_StartAutoAttackAll_Click(object? sender, RoutedEventArgs e)
    {
        if (MenuStartAutoAttack != null) MenuStartAutoAttack.IsEnabled = false;
        if (MenuStopAutoAttack != null) MenuStopAutoAttack.IsEnabled = false;

        foreach (var info in _tabs.Values)
            if (info.ChildHwnd != IntPtr.Zero)
                PostMessage(info.ChildHwnd, WM_SKUA_AUTO_ATTACK, new IntPtr(1), IntPtr.Zero);

        await Task.Delay(2000);

        if (MenuStartAutoAttack != null) MenuStartAutoAttack.IsEnabled = true;
        if (MenuStopAutoAttack != null) MenuStopAutoAttack.IsEnabled = true;
    }

    private async void MenuItem_StopAutoAttackAll_Click(object? sender, RoutedEventArgs e)
    {
        if (MenuStartAutoAttack != null) MenuStartAutoAttack.IsEnabled = false;
        if (MenuStopAutoAttack != null) MenuStopAutoAttack.IsEnabled = false;

        foreach (var info in _tabs.Values)
            if (info.ChildHwnd != IntPtr.Zero)
                PostMessage(info.ChildHwnd, WM_SKUA_AUTO_ATTACK, new IntPtr(0), IntPtr.Zero);

        await Task.Delay(2000);

        if (MenuStartAutoAttack != null) MenuStartAutoAttack.IsEnabled = true;
        if (MenuStopAutoAttack != null) MenuStopAutoAttack.IsEnabled = true;
    }

    private void BroadcastScriptAction(string scriptPath, int msg)
    {
        try
        {
            string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_script.txt");
            System.IO.File.WriteAllText(tempFile, scriptPath);

            foreach (var info in _tabs.Values)
            {
                if (info.ChildHwnd != IntPtr.Zero)
                {
                    PostMessage(info.ChildHwnd, (uint)msg, IntPtr.Zero, IntPtr.Zero);
                }
            }
        }
        catch { }
    }

    private void MenuItem_LoadScriptAll_Click(object? sender, RoutedEventArgs e)
    {
        var windowService = Ioc.Default.GetRequiredService<IWindowService>();
        windowService.RegisterManagedWindow("Script Repo", Ioc.Default.GetRequiredService<ScriptRepoViewModel>());
        windowService.ShowManagedWindow("Script Repo");
    }

    private async void MenuItem_LoginAll_Click(object? sender, RoutedEventArgs e)
    {
        var menuItem = sender as MenuItem;
        string originalHeader = menuItem?.Header?.ToString() ?? "Login All Clients";
        if (menuItem != null) menuItem.IsEnabled = false;

        int count = 1;
        int total = _tabs.Values.Count(i => i.ChildHwnd != IntPtr.Zero);

        foreach (var info in _tabs.Values)
        {
            if (info.ChildHwnd != IntPtr.Zero)
            {
                if (menuItem != null) menuItem.Header = $"Logging in... ({count}/{total})";
                PostMessage(info.ChildHwnd, WM_SKUA_LOGIN, IntPtr.Zero, IntPtr.Zero);
                await Task.Delay(2000);
                count++;
            }
        }

        if (menuItem != null)
        {
            menuItem.Header = originalHeader;
            menuItem.IsEnabled = true;
        }
    }

    private void MenuItem_LogoutAll_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var info in _tabs.Values)
            if (info.ChildHwnd != IntPtr.Zero)
                PostMessage(info.ChildHwnd, WM_SKUA_LOGOUT, IntPtr.Zero, IntPtr.Zero);
    }

    private string _lastJumpMap = "";
    private string _lastJumpCell = "";
    private string _lastJumpPlayer = "";
    private static string ArmyJumpSettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Skua", "army-control-jump.json");

    private sealed class ArmyJumpSettings
    {
        public string Map { get; set; } = "";
        public string Room { get; set; } = "";
        public string Cell { get; set; } = "Enter";
        public string Player { get; set; } = "";
        public List<ArmyJumpDestination> History { get; set; } = new();
        public List<ArmyJumpDestination> Presets { get; set; } = new();
    }

    private sealed class ArmyJumpDestination
    {
        public string Name { get; set; } = "";
        public string Map { get; set; } = "";
        public string Room { get; set; } = "";
        public string Cell { get; set; } = "Enter";
        public string Pad { get; set; } = "Spawn";

        public string DisplayName => $"{Map}{(string.IsNullOrWhiteSpace(Room) ? "" : $"-{Room}")} - {Cell}, {Pad}";
    }

    private sealed class ArmyClientLocation
    {
        public ArmyClientLocation() { }

        public string Map { get; set; } = "";
        public string Room { get; set; } = "";
        public string Cell { get; set; } = "Enter";
        public string Pad { get; set; } = "Spawn";
        public string Username { get; set; } = "";
        public DateTime UpdatedUtc { get; set; }
    }

    private readonly Dictionary<int, ArmyClientLocation> _latestClientLocations = new();
    private readonly Dictionary<int, string> _lastRecordedLocationByPid = new();

    private List<ArmyJumpDestination> _jumpMapHistory = new();
    private List<ArmyJumpDestination> _jumpMapPresets = new();
    private string _lastJumpRoom = "";

    private void LoadArmyJumpSettings()
    {
        try
        {
            string path = ArmyJumpSettingsPath;
            if (!File.Exists(path)) return;
            ArmyJumpSettings? settings = JsonSerializer.Deserialize<ArmyJumpSettings>(File.ReadAllText(path));
            if (settings == null) return;
            _lastJumpMap = settings.Map ?? "";
            _lastJumpCell = settings.Cell ?? "Enter";
            _lastJumpRoom = settings.Room ?? "";
            _lastJumpPlayer = settings.Player ?? "";
            _jumpMapHistory = settings.History ?? new List<ArmyJumpDestination>();
            _jumpMapPresets = settings.Presets ?? new List<ArmyJumpDestination>();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to load Army Control jump settings: {ex.Message}");
        }
    }

    private void SaveArmyJumpSettings()
    {
        try
        {
            string path = ArmyJumpSettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new ArmyJumpSettings
            {
                Map = _lastJumpMap,
                Cell = _lastJumpCell,
                Room = _lastJumpRoom,
                Player = _lastJumpPlayer,
                History = _jumpMapHistory,
                Presets = _jumpMapPresets
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to save Army Control jump settings: {ex.Message}");
        }
    }

    private async void MenuItem_MapTravel_Click(object? sender, RoutedEventArgs e)
    {
        Window dialog = new Window
        {
            Title = "Map Travel",
            Width = 640,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = this.Background
        };

        StackPanel panel = new StackPanel { Spacing = 14, Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = "Choose a recent destination or a saved preset to jump all clients.",
            TextWrapping = TextWrapping.Wrap
        });

        TextBlock historyLabel = new TextBlock { Text = "Recent destinations" };
        ComboBox historyPicker = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 360
        };
        StackPanel historyButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        Button jumpHistoryButton = new Button { Content = "Select / Jump", MinWidth = 110 };
        Button clearHistoryButton = new Button { Content = "Clear History", MinWidth = 100 };

        void RefreshHistory()
        {
            List<ArmyJumpDestination> history = _jumpMapHistory.ToList();
            historyPicker.ItemsSource = history.Select(item => item.DisplayName).ToList();
            historyPicker.SelectedIndex = history.Count > 0 ? 0 : -1;
            jumpHistoryButton.IsEnabled = history.Count > 0;
            clearHistoryButton.IsEnabled = history.Count > 0;
        }

        jumpHistoryButton.Click += (_, _) =>
        {
            List<ArmyJumpDestination> history = _jumpMapHistory.ToList();
            int index = historyPicker.SelectedIndex;
            if (index >= 0 && index < history.Count)
                JumpArmyToDestination(history[index]);
        };
        clearHistoryButton.Click += (_, _) =>
        {
            _jumpMapHistory.Clear();
            SaveArmyJumpSettings();
            RefreshHistory();
        };
        historyButtons.Children.Add(jumpHistoryButton);
        historyButtons.Children.Add(clearHistoryButton);
        panel.Children.Add(historyLabel);
        panel.Children.Add(historyPicker);
        panel.Children.Add(historyButtons);

        panel.Children.Add(new Separator());

        TextBlock presetLabel = new TextBlock { Text = "Saved presets" };
        ComboBox presetPicker = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 360
        };
        StackPanel presetButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        Button jumpPresetButton = new Button { Content = "Select / Jump", MinWidth = 110 };
        Button createPresetButton = new Button { Content = "Create", MinWidth = 80 };
        Button editPresetButton = new Button { Content = "Edit", MinWidth = 70 };
        Button deletePresetButton = new Button { Content = "Delete", MinWidth = 80 };

        void RefreshPresets(int selectedIndex = 0)
        {
            presetPicker.ItemsSource = _jumpMapPresets.Select(item => $"{item.Name} — {item.DisplayName}").ToList();
            presetPicker.SelectedIndex = _jumpMapPresets.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, _jumpMapPresets.Count - 1);
            bool hasPresets = _jumpMapPresets.Count > 0;
            jumpPresetButton.IsEnabled = hasPresets;
            editPresetButton.IsEnabled = hasPresets;
            deletePresetButton.IsEnabled = hasPresets;
        }

        jumpPresetButton.Click += (_, _) =>
        {
            int index = presetPicker.SelectedIndex;
            if (index >= 0 && index < _jumpMapPresets.Count)
                JumpArmyToDestination(_jumpMapPresets[index]);
        };
        createPresetButton.Click += async (_, _) =>
        {
            dialog.Hide();
            await OpenJumpMapPresetEditor(null);
            RefreshPresets(_jumpMapPresets.Count - 1);
            dialog.Show();
        };
        editPresetButton.Click += async (_, _) =>
        {
            int index = presetPicker.SelectedIndex;
            if (index < 0 || index >= _jumpMapPresets.Count)
                return;

            ArmyJumpDestination selectedPreset = _jumpMapPresets[index];
            dialog.Hide();
            await OpenJumpMapPresetEditor(selectedPreset);
            RefreshPresets(index);
            dialog.Show();
        };
        deletePresetButton.Click += (_, _) =>
        {
            int index = presetPicker.SelectedIndex;
            if (index < 0 || index >= _jumpMapPresets.Count)
                return;

            _jumpMapPresets.RemoveAt(index);
            SaveArmyJumpSettings();
            RefreshPresets(index);
        };

        presetButtons.Children.Add(jumpPresetButton);
        presetButtons.Children.Add(createPresetButton);
        presetButtons.Children.Add(editPresetButton);
        presetButtons.Children.Add(deletePresetButton);
        panel.Children.Add(presetLabel);
        panel.Children.Add(presetPicker);
        panel.Children.Add(presetButtons);

        StackPanel bottomButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        Button manualJumpButton = new Button { Content = "Jump to Map / Cell...", MinWidth = 140 };
        Button closeButton = new Button { Content = "Close", MinWidth = 84, IsCancel = true };
        manualJumpButton.Click += (_, _) =>
        {
            dialog.Hide();
            MenuItem_JumpMap_Click(null, new RoutedEventArgs());
            dialog.Show();
        };
        closeButton.Click += (_, _) => dialog.Close();
        bottomButtons.Children.Add(manualJumpButton);
        bottomButtons.Children.Add(closeButton);
        panel.Children.Add(bottomButtons);

        RefreshHistory();
        RefreshPresets();
        dialog.Content = panel;
        await dialog.ShowDialog(this);
    }

    private void MenuItem_JumpMap_Click(object? sender, RoutedEventArgs e)
    {
        InputDialogViewModel vm = new("Jump Army to Map / Cell", "Enter destination as Map,Room,Cell (room is optional):", "e.g., yulgar,12345,Enter", false);
        vm.DialogTextInput = $"{_lastJumpMap},{_lastJumpRoom},{_lastJumpCell}";

        if (Ioc.Default.GetRequiredService<IDialogService>().ShowDialog(vm) != true)
            return;

        string[] parts = (vm.DialogTextInput ?? "").Split(',');
        string targetMap = parts.Length > 0 ? parts[0].Trim() : "";
        string targetRoom = parts.Length > 2 ? parts[1].Trim() : "";
        string targetCell = parts.Length > 2 ? parts[2].Trim() : (parts.Length > 1 ? parts[1].Trim() : "Enter");

        if (!string.IsNullOrWhiteSpace(targetMap))
            JumpArmyToDestination(new ArmyJumpDestination { Map = targetMap, Room = targetRoom, Cell = string.IsNullOrWhiteSpace(targetCell) ? "Enter" : targetCell });
    }

    private void JumpArmyToDestination(ArmyJumpDestination destination)
    {
        _lastJumpMap = destination.Map.Trim();
        _lastJumpRoom = destination.Room.Trim();
        _lastJumpCell = string.IsNullOrWhiteSpace(destination.Cell) ? "Enter" : destination.Cell.Trim();

        SaveArmyJumpSettings();
        string mapToJoin = string.IsNullOrWhiteSpace(_lastJumpRoom) ? _lastJumpMap : $"{_lastJumpMap}-{_lastJumpRoom}";
        string tempFile = Path.Combine(Path.GetTempPath(), "skua_global_jump.txt");
        File.WriteAllLines(tempFile, new[] { mapToJoin, _lastJumpCell, string.IsNullOrWhiteSpace(destination.Pad) ? "Spawn" : destination.Pad.Trim() });

        foreach (var info in _tabs.Values)
            if (info.ChildHwnd != IntPtr.Zero)
                PostMessage(info.ChildHwnd, WM_SKUA_JUMP_MAP, IntPtr.Zero, IntPtr.Zero);
    }

    private async void MenuItem_JumpMapHistory_Click(object? sender, RoutedEventArgs e)
    {
        if (_jumpMapHistory.Count == 0)
        {
            Ioc.Default.GetRequiredService<IDialogService>().ShowMessageBox("No recently visited maps yet. Maps will appear here as your open clients travel.", "Jump Map History");
            return;
        }

        Window dialog = new Window
        {
            Title = "Jump to Recent Destination",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = this.Background
        };

        StackPanel panel = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Select a recent map, room and cell:", TextWrapping = TextWrapping.Wrap });

        List<ArmyJumpDestination> history = _jumpMapHistory.ToList();
        ComboBox historyPicker = new ComboBox
        {
            ItemsSource = history.Select(item => item.DisplayName).ToList(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Children.Add(historyPicker);

        StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        Button clearButton = new Button { Content = "Clear History", MinWidth = 100 };
        Button cancelButton = new Button { Content = "Cancel", MinWidth = 84 };
        Button jumpButton = new Button { Content = "Jump", MinWidth = 84, IsDefault = true };
        clearButton.Click += (_, _) =>
        {
            _jumpMapHistory.Clear();
            SaveArmyJumpSettings();
            dialog.Close(false);
        };
        cancelButton.Click += (_, _) => dialog.Close(false);
        jumpButton.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(clearButton);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(jumpButton);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        bool? confirmed = await dialog.ShowDialog<bool?>(this);
        if (confirmed != true || historyPicker.SelectedIndex < 0 || historyPicker.SelectedIndex >= history.Count)
            return;

        JumpArmyToDestination(history[historyPicker.SelectedIndex]);
    }

    private async void MenuItem_SaveJumpMapPreset_Click(object? sender, RoutedEventArgs e)
    {
        await OpenJumpMapPresetEditor(null);
    }

    private async Task OpenJumpMapPresetEditor(ArmyJumpDestination? existingPreset)
    {
        Window dialog = new Window
        {
            Title = existingPreset == null ? "Create Map Preset" : "Edit Map Preset",
            Width = 460,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = this.Background
        };

        StackPanel panel = new StackPanel { Spacing = 10, Margin = new Thickness(18) };
        TextBox nameInput = new TextBox { Watermark = "Preset name", Text = existingPreset?.Name ?? "" };
        TextBox mapInput = new TextBox { Watermark = "Map name", Text = existingPreset?.Map ?? "" };
        TextBox roomInput = new TextBox { Watermark = "Room number (optional)", Text = existingPreset?.Room ?? "" };
        TextBox cellInput = new TextBox { Watermark = "Cell", Text = existingPreset?.Cell ?? "Enter" };
        TextBox padInput = new TextBox { Watermark = "Pad", Text = existingPreset?.Pad ?? "Spawn" };

        // New presets start with the selected client's current location when available.
        if (existingPreset == null
            && InstancesTabControl.SelectedItem is TabItem currentTab
            && _tabs.TryGetValue(currentTab, out TabInfo? currentInfo)
            && currentInfo.Process != null
            && _latestClientLocations.TryGetValue(currentInfo.Process.Id, out ArmyClientLocation? currentLocation)
            && currentLocation != null
            && currentLocation.UpdatedUtc >= DateTime.UtcNow.AddSeconds(-5)
            && !string.IsNullOrWhiteSpace(currentLocation.Map))
        {
            mapInput.Text = currentLocation.Map;
            roomInput.Text = currentLocation.Room;
            cellInput.Text = currentLocation.Cell;
            padInput.Text = currentLocation.Pad;
            nameInput.Text = currentLocation.Map + (string.IsNullOrWhiteSpace(currentLocation.Room) ? "" : "-" + currentLocation.Room);
        }

        panel.Children.Add(new TextBlock { Text = "Preset name" });
        panel.Children.Add(nameInput);
        panel.Children.Add(new TextBlock { Text = "Map" });
        panel.Children.Add(mapInput);
        panel.Children.Add(new TextBlock { Text = "Room number (optional)" });
        panel.Children.Add(roomInput);
        panel.Children.Add(new TextBlock { Text = "Cell" });
        panel.Children.Add(cellInput);
        panel.Children.Add(new TextBlock { Text = "Pad" });
        panel.Children.Add(padInput);

        Button saveCurrentButton = new Button { Content = "Use Current Client Location", HorizontalAlignment = HorizontalAlignment.Left };
        saveCurrentButton.Click += (_, _) =>
        {
            if (InstancesTabControl.SelectedItem is not TabItem selectedTab
                || !_tabs.TryGetValue(selectedTab, out TabInfo? selectedInfo)
                || selectedInfo.Process == null
                || !_latestClientLocations.TryGetValue(selectedInfo.Process.Id, out ArmyClientLocation? location)
                || location == null
                || location.UpdatedUtc < DateTime.UtcNow.AddSeconds(-5)
                || string.IsNullOrWhiteSpace(location.Map))
            {
                Ioc.Default.GetRequiredService<IDialogService>().ShowMessageBox("The selected client has not reported a current location yet. Log in and wait a moment, then try again.", "Current Location");
                return;
            }

            mapInput.Text = location.Map;
            roomInput.Text = location.Room;
            cellInput.Text = location.Cell;
            padInput.Text = location.Pad;
            if (string.IsNullOrWhiteSpace(nameInput.Text))
                nameInput.Text = location.Map + (string.IsNullOrWhiteSpace(location.Room) ? "" : "-" + location.Room);
        };
        panel.Children.Add(saveCurrentButton);

        StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        Button cancelButton = new Button { Content = "Cancel", MinWidth = 84 };
        Button saveButton = new Button { Content = "Save Preset", MinWidth = 100, IsDefault = true };
        cancelButton.Click += (_, _) => dialog.Close(false);
        saveButton.Click += (_, _) =>
        {
            string name = nameInput.Text?.Trim() ?? "";
            string map = mapInput.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(map))
            {
                Ioc.Default.GetRequiredService<IDialogService>().ShowMessageBox("Enter both a preset name and map name.", "Create Map Preset");
                return;
            }

            ArmyJumpDestination preset = existingPreset ?? new ArmyJumpDestination();
            preset.Name = name;
            preset.Map = map;
            preset.Room = roomInput.Text?.Trim() ?? "";
            preset.Cell = string.IsNullOrWhiteSpace(cellInput.Text) ? "Enter" : cellInput.Text.Trim();
            preset.Pad = string.IsNullOrWhiteSpace(padInput.Text) ? "Spawn" : padInput.Text.Trim();

            _jumpMapPresets.RemoveAll(item => !ReferenceEquals(item, preset) && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existingPreset == null)
                _jumpMapPresets.Add(preset);
            SaveArmyJumpSettings();
            dialog.Close(true);
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        await dialog.ShowDialog<bool?>(this);
    }

    private async void MenuItem_JumpMapPreset_Click(object? sender, RoutedEventArgs e)
    {
        if (_jumpMapPresets.Count == 0)
        {
            Ioc.Default.GetRequiredService<IDialogService>().ShowMessageBox("No map presets saved yet. Use Save Current Map as Preset to add one.", "Map Presets");
            return;
        }

        Window dialog = new Window
        {
            Title = "Jump to Map Preset",
            Width = 440,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = this.Background
        };

        StackPanel panel = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Select a saved map, room and cell preset:", TextWrapping = TextWrapping.Wrap });

        List<ArmyJumpDestination> presets = _jumpMapPresets.ToList();
        ComboBox presetPicker = new ComboBox
        {
            ItemsSource = presets.Select(item => $"{item.Name} — {item.DisplayName}").ToList(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Children.Add(presetPicker);

        StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        Button cancelButton = new Button { Content = "Cancel", MinWidth = 84 };
        Button jumpButton = new Button { Content = "Jump", MinWidth = 84, IsDefault = true };
        cancelButton.Click += (_, _) => dialog.Close(false);
        jumpButton.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(jumpButton);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        bool? confirmed = await dialog.ShowDialog<bool?>(this);
        if (confirmed != true || presetPicker.SelectedIndex < 0 || presetPicker.SelectedIndex >= presets.Count)
            return;

        JumpArmyToDestination(presets[presetPicker.SelectedIndex]);
    }

    private async void MenuItem_ManageJumpMapPresets_Click(object? sender, RoutedEventArgs e)
    {
        Window dialog = new Window
        {
            Title = "Manage Map Presets",
            Width = 600,
            MinHeight = 280,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = this.Background
        };

        StackPanel panel = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Create, inspect, edit, or delete saved map presets.", TextWrapping = TextWrapping.Wrap });

        ListBox presetList = new ListBox
        {
            ItemsSource = _jumpMapPresets.Select(item => $"{item.Name} — {item.DisplayName}").ToList(),
            SelectedIndex = _jumpMapPresets.Count > 0 ? 0 : -1,
            MinHeight = 100,
            MaxHeight = 320
        };
        panel.Children.Add(presetList);

        TextBlock destinationText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        void UpdateDestination()
        {
            int index = presetList.SelectedIndex;
            destinationText.Text = index >= 0 && index < _jumpMapPresets.Count
                ? $"Destination: {_jumpMapPresets[index].DisplayName}"
                : "No preset selected.";
        }

        presetList.SelectionChanged += (_, _) => UpdateDestination();
        panel.Children.Add(destinationText);
        UpdateDestination();

        StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        Button createButton = new Button { Content = "Create Preset...", MinWidth = 110 };
        Button editButton = new Button { Content = "Edit Selected...", MinWidth = 110, IsEnabled = _jumpMapPresets.Count > 0 };
        Button deleteButton = new Button { Content = "Delete Selected", MinWidth = 120, IsEnabled = _jumpMapPresets.Count > 0 };
        Button closeButton = new Button { Content = "Close", MinWidth = 84, IsCancel = true };

        void RefreshPresets(int selectedIndex)
        {
            presetList.ItemsSource = _jumpMapPresets.Select(item => $"{item.Name} — {item.DisplayName}").ToList();
            presetList.SelectedIndex = _jumpMapPresets.Count == 0 ? -1 : Math.Clamp(selectedIndex, 0, _jumpMapPresets.Count - 1);
            editButton.IsEnabled = _jumpMapPresets.Count > 0;
            deleteButton.IsEnabled = _jumpMapPresets.Count > 0;
            UpdateDestination();
        }

        createButton.Click += async (_, _) =>
        {
            dialog.Hide();
            await OpenJumpMapPresetEditor(null);
            dialog.Show();
            RefreshPresets(_jumpMapPresets.Count - 1);
        };
        editButton.Click += async (_, _) =>
        {
            int index = presetList.SelectedIndex;
            if (index < 0 || index >= _jumpMapPresets.Count)
                return;

            ArmyJumpDestination selectedPreset = _jumpMapPresets[index];
            dialog.Hide();
            await OpenJumpMapPresetEditor(selectedPreset);
            dialog.Show();
            RefreshPresets(index);
        };
        deleteButton.Click += (_, _) =>
        {
            int index = presetList.SelectedIndex;
            if (index < 0 || index >= _jumpMapPresets.Count)
                return;

            _jumpMapPresets.RemoveAt(index);
            SaveArmyJumpSettings();
            RefreshPresets(index);
        };
        closeButton.Click += (_, _) => dialog.Close();
        buttons.Children.Add(createButton);
        buttons.Children.Add(editButton);
        buttons.Children.Add(deleteButton);
        buttons.Children.Add(closeButton);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        await dialog.ShowDialog(this);
    }

    private async void MenuItem_JumpPlayer_Click(object? sender, RoutedEventArgs e)
    {
        SyncTabTitles();
        List<string> players = _tabs
            .Select(entry => GetTabLoggedInUsername(entry.Key, entry.Value, GetTabTitle(entry.Key)))
            .Where(username => !string.IsNullOrWhiteSpace(username))
            .Select(username => username!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(username => username, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (players.Count == 0)
        {
            Ioc.Default.GetService<IDialogService>()?.ShowMessageBox("No logged-in player names were found in the open client tabs. Log in to at least one tab and try again.", "Jump All to Player");
            return;
        }

        Window dialog = new Window
        {
            Title = "Jump Army to Player",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = this.Background
        };
        StackPanel panel = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Select a player from the open client tabs:", TextWrapping = TextWrapping.Wrap });
        string initialPlayer = players.FirstOrDefault(player => player.Equals(_lastJumpPlayer, StringComparison.OrdinalIgnoreCase)) ?? players[0];
        ComboBox playerPicker = new ComboBox
        {
            ItemsSource = players,
            SelectedItem = initialPlayer,
            Text = initialPlayer,
            IsEditable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Children.Add(playerPicker);
        StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        Button cancelButton = new Button { Content = "Cancel", MinWidth = 84 };
        Button jumpButton = new Button { Content = "Jump", MinWidth = 84, IsDefault = true };
        cancelButton.Click += (_, _) => dialog.Close(false);
        jumpButton.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(jumpButton);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        bool? confirmed = await dialog.ShowDialog<bool?>(this);
        string targetPlayer = playerPicker.Text?.Trim() ?? "";
        if (confirmed != true || string.IsNullOrWhiteSpace(targetPlayer))
            return;

        _lastJumpPlayer = targetPlayer;
        SaveArmyJumpSettings();
        string tempFile = Path.Combine(Path.GetTempPath(), "skua_global_jump_player.txt");
        File.WriteAllText(tempFile, targetPlayer);

        foreach (var info in _tabs.Values)
            if (info.ChildHwnd != IntPtr.Zero)
                PostMessage(info.ChildHwnd, WM_SKUA_JUMP_PLAYER, IntPtr.Zero, IntPtr.Zero);
    }

    private string GetTabTitle(TabItem tab)
    {
        if (tab.Header is StackPanel headerPanel)
            return headerPanel.Children.OfType<TextBlock>().FirstOrDefault(block => block.Text != "✕")?.Text ?? "";
        return tab.Header?.ToString() ?? "";
    }

    // Each client joins its own house (no input needed).
    private void MenuItem_JumpHouse_Click(object? sender, RoutedEventArgs e)
    {
        foreach (var info in _tabs.Values)
            if (info.ChildHwnd != IntPtr.Zero)
                PostMessage(info.ChildHwnd, WM_SKUA_JUMP_HOUSE, IntPtr.Zero, IntPtr.Zero);
    }

    private const int WM_SKUA_JUMP_HOUSE = 0x0400 + 464;
    private const int WM_SKUA_CHANGE_SERVER = 0x0400 + 465;
    private const int WM_SKUA_ACCEPT_QUEST = 0x0400 + 458;
    private string _lastAcceptQuestId = "";

    private async void MenuItem_ChangeServerAll_Click(object? sender, RoutedEventArgs e)
    {
        List<string> serverNames;
        try
        {
            IScriptServers? scriptServers = Ioc.Default.GetService<IScriptServers>();
            if (scriptServers is null)
            {
                Ioc.Default.GetService<IDialogService>()?.ShowMessageBox("The server service is unavailable. Please restart VibeSkua and try again.", "Change Server for All");
                return;
            }

            List<Skua.Core.Models.Servers.Server> servers = await scriptServers.GetServers(true);
            serverNames = servers
                .Where(server => server.Online && !string.IsNullOrWhiteSpace(server.Name) && !string.IsNullOrWhiteSpace(server.IP))
                .Select(server => server.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to load Army Control servers: {ex.GetType().Name}: {ex.Message}");
            Ioc.Default.GetService<IDialogService>()?.ShowMessageBox("Could not load the current server list. Check your connection and try again.", "Change Server for All");
            return;
        }

        if (serverNames.Count == 0)
        {
            Ioc.Default.GetService<IDialogService>()?.ShowMessageBox("The server API did not return any online servers.", "Change Server for All");
            return;
        }

        Window dialog = new Window
        {
            Title = "Change Server for All",
            Width = 360,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = this.Background
        };
        StackPanel panel = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = "Select the server for all open clients:", TextWrapping = TextWrapping.Wrap });

        string initialServer = serverNames.FirstOrDefault(name => name.Equals("Artix", StringComparison.OrdinalIgnoreCase)) ?? serverNames[0];
        ComboBox serverPicker = new ComboBox
        {
            ItemsSource = serverNames,
            SelectedItem = initialServer,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Children.Add(serverPicker);

        StackPanel buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        Button cancelButton = new Button { Content = "Cancel", MinWidth = 84 };
        Button changeButton = new Button { Content = "Change Server", MinWidth = 110, IsDefault = true };
        cancelButton.Click += (_, _) => dialog.Close(false);
        changeButton.Click += (_, _) => dialog.Close(true);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(changeButton);
        panel.Children.Add(buttons);
        dialog.Content = panel;

        bool? confirmed = await dialog.ShowDialog<bool?>(this);
        string targetServer = serverPicker.SelectedItem?.ToString()?.Trim() ?? string.Empty;
        if (confirmed != true || string.IsNullOrWhiteSpace(targetServer))
            return;

        try
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "skua_global_change_server.txt");
            File.WriteAllText(tempFile, targetServer);
            int sentCount = 0;
            foreach (var info in _tabs.Values)
            {
                if (info.ChildHwnd != IntPtr.Zero && PostMessage(info.ChildHwnd, WM_SKUA_CHANGE_SERVER, IntPtr.Zero, IntPtr.Zero))
                    sentCount++;
            }

            if (sentCount == 0)
                Ioc.Default.GetService<IDialogService>()?.ShowMessageBox("No active client windows were available to receive the server change.", "Change Server for All");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to broadcast Army Control server change: {ex.GetType().Name}: {ex.Message}");
            Ioc.Default.GetService<IDialogService>()?.ShowMessageBox("Could not send the server change to the clients.", "Change Server for All");
        }
    }

    private void MenuItem_AcceptQuest_Click(object? sender, RoutedEventArgs e)
    {
        var vm = new InputDialogViewModel("Accept Quest (Army)", "Enter quest ID to accept/register on all accounts:", "e.g., 1907", false);
        vm.DialogTextInput = _lastAcceptQuestId;

        if (Ioc.Default.GetRequiredService<IDialogService>().ShowDialog(vm) == true)
        {
            string questId = vm.DialogTextInput?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(questId))
            {
                _lastAcceptQuestId = questId;
                string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_accept_quest.txt");
                System.IO.File.WriteAllLines(tempFile, new[] { questId, "-1" });

                foreach (var info in _tabs.Values)
                    if (info.ChildHwnd != IntPtr.Zero)
                        PostMessage(info.ChildHwnd, WM_SKUA_ACCEPT_QUEST, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }

    private void UpdateGridViewBorderColor()
    {
        if (GameSizeBorder != null)
            GameSizeBorder.IsVisible = !_isGridViewEnabled;

        if (_isGridViewEnabled)
        {
            GridViewBorder[!Border.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaSelectionBrush");
            GridViewBorder[!Border.BorderBrushProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaAccentBrush");
            if (GridViewText != null) GridViewText[!TextBlock.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaAccentBrush");
            if (GridViewIcon != null) GridViewIcon[!MaterialIcon.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaAccentBrush");
        }
        else
        {
            GridViewBorder[!Border.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaSurfaceAltBrush");
            GridViewBorder[!Border.BorderBrushProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaBorderBrush");
            if (GridViewText != null) GridViewText[!TextBlock.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaTextBrush");
            if (GridViewIcon != null) GridViewIcon[!MaterialIcon.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaTextBrush");
        }
    }

    /// <summary>
    /// Hides all embedded game windows to prevent their pixels remaining visible when the client is hidden.
    /// </summary>
    public void HideEmbeddedClients()
    {
        foreach (TabInfo info in _tabs.Values)
        {
            if (info.ChildHwnd != IntPtr.Zero)
                ShowWindow(info.ChildHwnd, SW_HIDE);
        }

        HotKeys.ActiveChildHwnd = IntPtr.Zero;
    }

    /// <summary>
    /// Restores embedded game windows after the client is shown again.
    /// </summary>
    public void ShowEmbeddedClients()
    {
        _lastX = int.MinValue;
        _lastY = int.MinValue;
        _lastW = int.MinValue;
        _lastH = int.MinValue;
        _lastActiveHwnd = IntPtr.Zero;
        ScheduleReposition();
    }

    // Places the tabs now, even mid-drag. A drag only moves them (FastReposition) at the size and offset cached when
    // it started, which are wrong once the host or a child has crossed onto a monitor with another scale.
    private void RepositionWithFreshCache()
    {
        if (_isClosing) return;

        bool dragging = _isDraggingWindow;
        _isDraggingWindow = false;
        _lastX = int.MinValue;
        _lastY = int.MinValue;
        _lastW = int.MinValue;
        _lastH = int.MinValue;
        _lastActiveHwnd = IntPtr.Zero;
        DoReposition();
        _isDraggingWindow = dragging;
    }

    private void OnWindowPositionChanged(PixelPoint newPos)
    {
        if (!IsLoaded || _isClosing || WindowState == WindowState.Minimized || !IsVisible)
            return;

        if (_isRepositioning)
            return;

        if (_childWidth <= 1 || _childHeight <= 1)
        {
            DoReposition();
            return;
        }

        if (newPos.X == _lastFastX && newPos.Y == _lastFastY)
            return;

        _lastFastX = newPos.X;
        _lastFastY = newPos.Y;

        FastReposition(newPos);
    }

    private void FastReposition(PixelPoint newPos)
    {
        if (_childWidth <= 1 || _childHeight <= 1)
            return;

        if (_isGridViewEnabled)
        {
            for (int i = 0; i < _gridTiles.Count; i++)
            {
                var tile = _gridTiles[i];
                if (tile.Hwnd != IntPtr.Zero)
                {
                    SetWindowPos(tile.Hwnd, IntPtr.Zero,
                        newPos.X + tile.OffsetX,
                        newPos.Y + tile.OffsetY,
                        tile.Width,
                        tile.Height,
                        SWP_NOACTIVATE | SWP_NOZORDER);
                    ShowWindow(tile.Hwnd, SW_SHOW);
                }
            }
            return;
        }

        IntPtr activeChildHwnd = _lastActiveHwnd;
        if (activeChildHwnd == IntPtr.Zero)
        {
            if (InstancesTabControl.SelectedItem is TabItem tab && _tabs.TryGetValue(tab, out var info))
                activeChildHwnd = info.ChildHwnd;
            if (activeChildHwnd == IntPtr.Zero && _tabs.Count > 0)
                activeChildHwnd = _tabs.Values.FirstOrDefault(t => t.ChildHwnd != IntPtr.Zero)?.ChildHwnd ?? IntPtr.Zero;
            _lastActiveHwnd = activeChildHwnd;
        }

        if (activeChildHwnd != IntPtr.Zero)
        {
            int targetX = newPos.X + _childOffsetX;
            int targetY = newPos.Y + _childOffsetY;

            SetWindowPos(activeChildHwnd, IntPtr.Zero,
                targetX,
                targetY,
                _childWidth,
                _childHeight,
                SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_NOZORDER);
        }
    }

    private bool _repositionScheduled = false;

    private void ScheduleReposition()
    {
        _needsReposition = true;
        if (_repositionScheduled || _isClosing) return;

        _repositionScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _repositionScheduled = false;
            if (_isClosing || !IsLoaded) return;

            _needsReposition = false;
            DoReposition();
        }, DispatcherPriority.Render);
    }

    private void DoReposition()
    {
        if (!IsLoaded || _isClosing || _isDraggingWindow) return;

        if (_isRepositioning)
        {
            _needsReposition = true;
            return;
        }

        _isRepositioning = true;
        try
        {
            TabItem? selectedTab = InstancesTabControl.SelectedItem as TabItem;
            if (!_isGridViewEnabled && selectedTab == null)
            {
                selectedTab = (_lastSelectedTab != null && InstancesTabControl.Items.Contains(_lastSelectedTab))
                    ? _lastSelectedTab
                    : InstancesTabControl.Items.OfType<TabItem>().FirstOrDefault();

                if (selectedTab != null)
                {
                    _ignoreSelectionChange = true;
                    InstancesTabControl.SelectedItem = selectedTab;
                    _ignoreSelectionChange = false;
                }
            }

            if (!_isGridViewEnabled && selectedTab == null) return;

            if (WindowState == WindowState.Minimized || !IsVisible)
            {
                // Let Windows handle visibility for owned windows when the host is hidden.
                // Explicitly hiding the embedded Flash HWND can leave the child renderer black
                // after restoration, so do not send SW_HIDE to child windows here.
                return;
            }

            PixelPoint screenTL;
            PixelPoint screenBR;
            try
            {
                screenTL = HostContainer.PointToScreen(new Point(0, 0));
                screenBR = HostContainer.PointToScreen(new Point(HostContainer.Bounds.Width, HostContainer.Bounds.Height));
            }
            catch
            {
                return;
            }

            int x = screenTL.X;
            int y = screenTL.Y;
            int w = Math.Max(screenBR.X - screenTL.X, 1);
            int h = Math.Max(screenBR.Y - screenTL.Y, 1);

            if (w <= 1 || h <= 1) return;

            (int gameX, int gameY, int gameWidth, int gameHeight) = FitGameSize(x, y, w, h, _gameSize);
            _childOffsetX = (_isGridViewEnabled ? x : gameX) - Position.X;
            _childOffsetY = (_isGridViewEnabled ? y : gameY) - Position.Y;
            _childWidth = _isGridViewEnabled ? w : gameWidth;
            _childHeight = _isGridViewEnabled ? h : gameHeight;
            _lastFastX = Position.X;
            _lastFastY = Position.Y;

            // Do not include SWP_SHOWWINDOW in queued position requests. A stale async request
            // can otherwise re-show a child after the host has been hidden from the tray.
            uint baseFlags = SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_NOZORDER;

            var activeWindows = InstancesTabControl.Items.OfType<TabItem>()
                .Where(t => _tabs.ContainsKey(t))
                .Select(t => _tabs[t])
                .Where(v => v.ChildHwnd != IntPtr.Zero)
                .ToList();

            bool isLoading = false;
            if (_isGridViewEnabled)
            {
                isLoading = _tabs.Values.Any(v => v.ChildHwnd == IntPtr.Zero);
            }
            else
            {
                if (selectedTab != null && _tabs.TryGetValue(selectedTab, out TabInfo? activeInfo) && activeInfo.ChildHwnd == IntPtr.Zero)
                    isLoading = true;
            }
            if (LoadingIndicator != null) LoadingIndicator.IsVisible = isLoading;

            if (activeWindows.Count == 0) return;

            if (_isGridViewEnabled)
            {
                int n = activeWindows.Count;
                int cols = _gridLayoutMode switch
                {
                    GridLayoutMode.Horizontal => n,
                    GridLayoutMode.Vertical => 1,
                    _ => (int)Math.Ceiling(Math.Sqrt(n))
                };
                int rows = (int)Math.Ceiling((double)n / cols);
                int cellW = w / cols;
                int cellH = h / rows;

                _gridTiles.Clear();
                for (int i = 0; i < n; i++)
                {
                    int r = i / cols;
                    int c = i % cols;
                    _gridTiles.Add(new GridTile
                    {
                        Hwnd = activeWindows[i].ChildHwnd,
                        OffsetX = _childOffsetX + (c * cellW),
                        OffsetY = _childOffsetY + (r * cellH),
                        Width = cellW,
                        Height = cellH
                    });
                    SetWindowPos(activeWindows[i].ChildHwnd, IntPtr.Zero, x + c * cellW, y + r * cellH, cellW, cellH, baseFlags);
                    ShowWindow(activeWindows[i].ChildHwnd, SW_SHOW);
                    if (activeWindows[i].IsThrottled)
                    {
                        activeWindows[i].IsThrottled = false;
                        PostMessage(activeWindows[i].ChildHwnd, WM_SKUA_THROTTLE, new IntPtr(0), IntPtr.Zero);
                    }
                }
                _lastGridViewState = true;
                _lastActiveHwnd = IntPtr.Zero;
                if (selectedTab != null && _tabs.TryGetValue(selectedTab, out TabInfo? selInfo) && selInfo.ChildHwnd != IntPtr.Zero)
                    HotKeys.ActiveChildHwnd = selInfo.ChildHwnd;
                else if (activeWindows.Count > 0)
                    HotKeys.ActiveChildHwnd = activeWindows[0].ChildHwnd;
            }
            else
            {
                TabInfo? activeInfo = null;
                IntPtr activeChildHwnd = IntPtr.Zero;
                if (selectedTab != null && _tabs.TryGetValue(selectedTab, out activeInfo) && activeInfo.ChildHwnd != IntPtr.Zero)
                {
                    activeChildHwnd = activeInfo.ChildHwnd;
                }
                if (activeChildHwnd == IntPtr.Zero && _tabs.Count > 0)
                {
                    activeChildHwnd = _tabs.Values.FirstOrDefault(t => t.ChildHwnd != IntPtr.Zero)?.ChildHwnd ?? IntPtr.Zero;
                }
                HotKeys.ActiveChildHwnd = activeChildHwnd;

                if (gameX == _lastX && gameY == _lastY && gameWidth == _lastW && gameHeight == _lastH &&
                    activeChildHwnd == _lastActiveHwnd && _lastGridViewState == _isGridViewEnabled)
                {
                    return;
                }

                _lastX = gameX;
                _lastY = gameY;
                _lastW = gameWidth;
                _lastH = gameHeight;
                _lastActiveHwnd = activeChildHwnd;
                _lastGridViewState = false;

                if (activeChildHwnd != IntPtr.Zero)
                {
                    SetWindowPos(activeChildHwnd, IntPtr.Zero, gameX, gameY, gameWidth, gameHeight, baseFlags);
                    ShowWindow(activeChildHwnd, SW_SHOW);
                    if (activeInfo != null && activeInfo.IsThrottled)
                    {
                        activeInfo.IsThrottled = false;
                        PostMessage(activeChildHwnd, WM_SKUA_THROTTLE, new IntPtr(0), IntPtr.Zero);
                    }
                }

                foreach (var kvp in _tabs)
                {
                    if (kvp.Key == selectedTab) continue;

                    TabInfo info = kvp.Value;
                    if (info.ChildHwnd == IntPtr.Zero) continue;

                    if (!info.IsThrottled)
                    {
                        info.IsThrottled = true;
                        PostMessage(info.ChildHwnd, WM_SKUA_THROTTLE, new IntPtr(1), IntPtr.Zero);
                    }

                    // Keep inactive tabs hidden instead of moving them off-screen with an async
                    // SWP_SHOWWINDOW request that could race with tray hide/show.
                    ShowWindow(info.ChildHwnd, SW_HIDE);
                }
            }
        }
        finally
        {
            _isRepositioning = false;
            if (_needsReposition)
            {
                _needsReposition = false;
                Dispatcher.UIThread.Post(DoReposition, DispatcherPriority.Render);
            }
        }
    }

    #endregion Tab Switching & Positioning

    private static IntPtr FindWindowByProcessId(int processId)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if ((int)pid == processId)
            {
                System.Text.StringBuilder sb = new(256);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString();

                // Match WPF exactly: just check if it's visible and belongs to the process.
                if (IsWindowVisible(hWnd))
                {
                    result = hWnd;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    #region Tab Management

    private void CloseTab(TabItem tab)
    {
        if (!_tabs.ContainsKey(tab) && !InstancesTabControl.Items.Contains(tab))
            return;

        if (_tabs.TryGetValue(tab, out TabInfo? info))
        {
            _tabs.Remove(tab);
            Task.Run(() =>
            {
                try
                {
                    if (info.ChildHwnd != IntPtr.Zero)
                        ShowWindow(info.ChildHwnd, SW_HIDE);
                    if (info.Process != null && !info.Process.HasExited)
                        info.Process.Kill();
                    if (info.Process != null)
                    {
                        try { File.Delete(Path.Combine(Path.GetTempPath(), "VibeSkuaTabs", $"{info.Process.Id}.txt")); } catch { }
                    }
                }
                catch { }
            });
        }
        InstancesTabControl.Items.Remove(tab);
        UpdateTabScrollButtons();

        if (_tabs.Count == 0 || !InstancesTabControl.Items.OfType<TabItem>().Any())
        {
            _lastSelectedTab = null;
            _lastActiveHwnd = IntPtr.Zero;
            _isGridViewEnabled = false;

            // Army Queue passes this flag so its monitored host process exits after the
            // final account finishes. Without it, keep the normal reusable tab host open.
            if (_initialArgs.Contains("--exit-on-script-stop", StringComparer.OrdinalIgnoreCase))
            {
                Close();
                return;
            }

            // Keep the normal host alive when the final account tab closes so the user
            // can open another tab without restarting VibeSkua.
            if (!IsVisible)
                Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            ScheduleReposition();
            return;
        }

        if (_isGridViewEnabled)
        {
            if (_tabs.Count <= 1)
            {
                ExitGridView();
                return;
            }

            DoReposition();
            return;
        }

        var remaining = InstancesTabControl.Items.OfType<TabItem>().FirstOrDefault();
        if (remaining != null && InstancesTabControl.SelectedItem == null)
            InstancesTabControl.SelectedItem = remaining;
    }

    public void AddNewInstance(string rawArgs)
    {
        AddNewInstance(ParseRawCommandLine(rawArgs));
    }

    public void AddNewInstance(string[]? clientArgs = null)
    {
        clientArgs ??= Array.Empty<string>();

        int availableId = 1;
        var currentTitles = InstancesTabControl.Items.OfType<TabItem>()
            .Select(t => t.Header as StackPanel)
            .Where(p => p != null)
            .Select(p => p!.Children.OfType<TextBlock>().FirstOrDefault(tb => tb.Text != "✕")?.Text)
            .Where(t => t != null && (t.StartsWith("User ") || t.StartsWith("Skua ")))
            .Select(t =>
            {
                string numStr = t!.StartsWith("User ") ? t.Substring(5) : t.Substring(5);
                int.TryParse(numStr, out int id);
                return id;
            })
            .ToList();

        while (currentTitles.Contains(availableId))
        {
            availableId++;
        }

        string tabName = "User " + availableId;
        for (int i = 0; i < clientArgs.Length; i++)
        {
            if ((clientArgs[i] == "-u" || clientArgs[i] == "--user") && i + 1 < clientArgs.Length)
            {
                if (!string.IsNullOrWhiteSpace(clientArgs[i + 1]))
                    tabName = clientArgs[i + 1];
                break;
            }
        }

        #region Tab Header UI

        StackPanel headerPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        MaterialIcon tabIcon = new MaterialIcon
        {
            Kind = MaterialIconKind.ShieldAccountOutline,
            Width = 13,
            Height = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        tabIcon[!MaterialIcon.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaAccentBrush");

        TextBox editTitle = new TextBox
        {
            Text = tabName,
            IsVisible = false,
            MinWidth = 60,
            MaxWidth = 180,
            Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 1, 4, 1),
            Margin = new Thickness(0, 0, 6, 0),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
        editTitle[!TextBox.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaSurfaceAltBrush");
        editTitle[!TextBox.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaTextBrush");
        editTitle[!TextBox.CaretBrushProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaTextBrush");
        editTitle[!TextBox.BorderBrushProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaBorderBrush");

        TextBlock title = new TextBlock
        {
            Text = tabName,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12
        };
        title[!TextBlock.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaTextBrush");

        Border closeBtn = new Border
        {
            Background = Brushes.Transparent,
            Cursor = new global::Avalonia.Input.Cursor(global::Avalonia.Input.StandardCursorType.Hand),
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            VerticalAlignment = VerticalAlignment.Center
        };
        TextBlock closeTxt = new TextBlock
        {
            Text = "✕",
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        closeTxt[!TextBlock.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaMutedTextBrush");
        closeBtn.Child = closeTxt;

        headerPanel.Children.Add(tabIcon);
        headerPanel.Children.Add(editTitle);
        headerPanel.Children.Add(title);
        headerPanel.Children.Add(closeBtn);

        closeBtn.PointerEntered += (s, ev) => { closeTxt.Foreground = Brushes.White; closeBtn.Background = new SolidColorBrush(Color.Parse("#EF4444")); };
        closeBtn.PointerExited += (s, ev) => { closeTxt[!TextBlock.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaMutedTextBrush"); closeBtn.Background = Brushes.Transparent; };

        TabInfo info = new TabInfo();

        Action closeEditMode = () =>
        {
            if (!editTitle.IsVisible) return;
            string newName = string.IsNullOrWhiteSpace(editTitle.Text) ? title.Text : editTitle.Text;
            if (title.Text != newName)
            {
                title.Text = newName;
                info.IsCustomNamed = true;
            }
            title.IsVisible = true;
            editTitle.IsVisible = false;
        };

        title.DoubleTapped += (s, ev) =>
        {
            editTitle.Text = title.Text;
            title.IsVisible = false;
            editTitle.IsVisible = true;

            Dispatcher.UIThread.Post(() =>
            {
                editTitle.Focus();
                editTitle.SelectAll();
            }, DispatcherPriority.Input);

            ev.Handled = true;
        };

        editTitle.LostFocus += (s, ev) => closeEditMode();

        editTitle.KeyDown += (s, ev) =>
        {
            if (ev.Key == Key.Enter)
            {
                closeEditMode();
                ev.Handled = true;
            }
            else if (ev.Key == Key.Escape)
            {
                editTitle.Text = title.Text; // revert
                closeEditMode();
                ev.Handled = true;
            }
        };

        #endregion Tab Header UI

        TabItem newTab = new TabItem { Header = headerPanel };
        DragDrop.SetAllowDrop(newTab, true);

        InstancesTabControl.Items.Add(newTab);
        if (InstancesTabControl.SelectedItem == null)
        {
            InstancesTabControl.SelectedItem = newTab;
        }
        Dispatcher.UIThread.Post(() =>
        {
            TabScrollViewer.Offset = new Vector(Math.Max(0, TabScrollViewer.Extent.Width - TabScrollViewer.Viewport.Width), 0);
            UpdateTabScrollButtons();
        }, DispatcherPriority.Background);

        _tabs[newTab] = info;

        EnqueueSpawn(() =>
        {
            if (_isClosing || !_tabs.ContainsKey(newTab))
            {
                _isSpawning = false;
                ProcessNextSpawn();
                return;
            }

            Process p = new Process();
            p.StartInfo.FileName = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            p.StartInfo.ArgumentList.Add("--embed");
            p.StartInfo.ArgumentList.Add(_hostHwnd.ToInt64().ToString());
            p.StartInfo.ArgumentList.Add("--host-pid");
            p.StartInfo.ArgumentList.Add(Process.GetCurrentProcess().Id.ToString());
            if (!string.IsNullOrWhiteSpace(App.HostInstanceId))
            {
                p.StartInfo.ArgumentList.Add("--host-instance");
                p.StartInfo.ArgumentList.Add(App.HostInstanceId);
            }

            foreach (string arg in clientArgs)
            {
                if (!string.IsNullOrEmpty(arg))
                    p.StartInfo.ArgumentList.Add(arg);
            }

            p.EnableRaisingEvents = true;
            p.Exited += (s, e) =>
            {
                string exitCode;
                try
                {
                    exitCode = p.ExitCode.ToString();
                }
                catch (Exception exception)
                {
                    exitCode = $"unavailable ({exception.GetType().Name}: {exception.Message})";
                }

                Dispatcher.UIThread.Post(() =>
                {
                    bool tabStillRegistered = _tabs.ContainsKey(newTab);
                    string logEntry = $"{DateTimeOffset.UtcNow:O} | Tab='{tabName}' | PID={p.Id} | ExitCode={exitCode} | HostClosing={_isClosing} | TabRegistered={tabStillRegistered} | ChildHwnd=0x{info.ChildHwnd.ToInt64():X}";
                    try
                    {
                        string logDirectory = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs");
                        Directory.CreateDirectory(logDirectory);
                        lock (ChildProcessExitLogLock)
                            File.AppendAllText(Path.Combine(logDirectory, "process-exits.log"), logEntry + Environment.NewLine);
                    }
                    catch (Exception exception)
                    {
                        Trace.WriteLine($"VibeSkua child process exit log failed: {exception}");
                    }

                    Trace.WriteLine($"VibeSkua child process exited: {logEntry}");

                    if (!_isClosing && tabStillRegistered)
                        CloseTab(newTab);
                });
            };

            try
            {
                p.Start();
            }
            catch (Exception ex)
            {
                p.Dispose();
                _isSpawning = false;
                Trace.WriteLine($"VibeSkua failed to start child process for tab '{tabName}': {ex}");
                Dispatcher.UIThread.Post(() =>
                {
                    if (_tabs.ContainsKey(newTab))
                        CloseTab(newTab);
                    ProcessNextSpawn();
                }, DispatcherPriority.Background);
                return;
            }

            info.Process = p;
            try
            {
                EnsureChildJobObject();
                if (_childJobHandle != IntPtr.Zero)
                    AssignProcessToJobObject(_childJobHandle, p.Handle);
            }
            catch { }
            HotKeys.RegisterSkuaPid((uint)p.Id);

            int pid = p.Id;
            Task.Run(async () =>
            {
                bool childWindowFound = false;

                for (int attempt = 0; attempt < 120 && !p.HasExited && info.ChildHwnd == IntPtr.Zero && !_isClosing; attempt++)
                {
                    await Task.Delay(50);

                    if (info.ChildHwnd != IntPtr.Zero)
                    {
                        childWindowFound = true;
                        break;
                    }

                    IntPtr childHwnd = FindWindowByProcessId(pid);
                    if (childHwnd != IntPtr.Zero)
                    {
                        childWindowFound = true;
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (!_isClosing && info.ChildHwnd == IntPtr.Zero)
                                AttachChildWindow(info, childHwnd);
                        });
                        break;
                    }
                }

                // AttachChildWindow advances the spawn queue itself. Do not advance it again
                // when a window was found but its UI-thread attachment has not run yet.
                if (!childWindowFound && info.ChildHwnd == IntPtr.Zero)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (info.ChildHwnd == IntPtr.Zero)
                        {
                            _isSpawning = false;
                            ProcessNextSpawn();
                        }
                    });
                }
            });
        });

        #region Tab Interactions

        closeBtn.PointerPressed += (s, ev) =>
        {
            CloseTab(newTab);
            ev.Handled = true;
        };

        headerPanel.PointerReleased += (s, ev) =>
        {
            if (ev.InitialPressMouseButton == MouseButton.Middle)
                CloseTab(newTab);
        };

        ContextMenu ctx = new ContextMenu();
        MenuItem closeItem = new MenuItem { Header = "Close Tab" };
        closeItem.Click += (s, ev) => CloseTab(newTab);
        MenuItem closeOthersItem = new MenuItem { Header = "Close Other Tabs" };
        closeOthersItem.Click += (s, ev) =>
        {
            var toRemove = InstancesTabControl.Items.OfType<TabItem>().Where(t => t != newTab && t.Header is StackPanel).ToList();
            foreach (var tab in toRemove) CloseTab(tab);
        };
        MenuItem closeRightItem = new MenuItem { Header = "Close Tabs to the Right" };
        closeRightItem.Click += (s, ev) =>
        {
            int idx = InstancesTabControl.Items.IndexOf(newTab);
            var toRemove = InstancesTabControl.Items.OfType<TabItem>().Where((t, i) => i > idx && t.Header is StackPanel).ToList();
            foreach (var tab in toRemove) CloseTab(tab);
        };
        ctx.Items.Add(closeItem);
        ctx.Items.Add(closeOthersItem);
        ctx.Items.Add(closeRightItem);
        ctx.Items.Add(new Separator());

        MenuItem addToGroupItem = new MenuItem
        {
            Header = "Add to Group",
            Icon = new MaterialIcon { Kind = MaterialIconKind.AccountMultiplePlus, Width = 14, Height = 14 }
        };
        ctx.Items.Add(addToGroupItem);

        void UpdateGroupSubMenu()
        {
            PopulateAddToGroupMenu(addToGroupItem, newTab, info, title.Text);
        }

        UpdateGroupSubMenu();
        headerPanel.ContextRequested += (s, ev) => UpdateGroupSubMenu();
        newTab.ContextRequested += (s, ev) => UpdateGroupSubMenu();

        headerPanel.ContextMenu = ctx;
        newTab.ContextMenu = ctx;

        Point startPoint = new Point();
        headerPanel.PointerPressed += (s, ev) =>
        {
            var prop = ev.GetCurrentPoint(headerPanel).Properties;
            if (prop.IsRightButtonPressed)
            {
                InstancesTabControl.SelectedItem = newTab;
                return;
            }

            if (!prop.IsLeftButtonPressed)
                return;

            startPoint = ev.GetPosition(null);
            InstancesTabControl.SelectedItem = newTab;

            if (_isGridViewEnabled)
            {
                _isGridViewEnabled = false;
                UpdateGridViewBorderColor();
                foreach (var tInfo in _tabs.Values)
                {
                    if (tInfo.ChildHwnd != IntPtr.Zero)
                    {
                        PostMessage(tInfo.ChildHwnd, WM_SKUA_GRIDVIEW, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                DoReposition();
            }
            ev.Handled = true;
        };

        newTab.PointerPressed += (s, ev) =>
        {
            if (ev.GetCurrentPoint(newTab).Properties.IsRightButtonPressed)
            {
                InstancesTabControl.SelectedItem = newTab;
            }
        };

        headerPanel.PointerMoved += async (s, ev) =>
        {
            if (editTitle.IsVisible) return;

            if (ev.GetCurrentPoint(null).Properties.IsLeftButtonPressed)
            {
                Point mousePos = ev.GetPosition(null);
                Vector diff = startPoint - mousePos;
                if (Math.Abs(diff.X) > 4 || Math.Abs(diff.Y) > 4)
                {
                    var dragData = new global::Avalonia.Input.DataObject();
                    dragData.Set("TabItem", newTab);
                    await DragDrop.DoDragDrop(ev, dragData, DragDropEffects.Move);
                }
            }
        };

        newTab.AddHandler(DragDrop.DropEvent, (s, ev) =>
        {
            if (ev.Data.Contains("TabItem"))
            {
                TabItem? droppedTab = ev.Data.Get("TabItem") as TabItem;
                if (droppedTab != null && droppedTab != newTab)
                {
                    int targetIndex = InstancesTabControl.Items.IndexOf(newTab);
                    InstancesTabControl.Items.Remove(droppedTab);
                    InstancesTabControl.Items.Insert(targetIndex, droppedTab);
                    InstancesTabControl.SelectedItem = droppedTab;
                }
            }
        });

        #endregion Tab Interactions
    }

    #region Group Assignment Helper

    private string? GetTabLoggedInUsername(TabItem tab, TabInfo? info, string currentTitle)
    {
        if (info?.Process != null && !info.Process.HasExited)
        {
            try
            {
                string file = Path.Combine(Path.GetTempPath(), "VibeSkuaTabs", $"{info.Process.Id}.txt");
                if (File.Exists(file))
                {
                    string name = File.ReadAllText(file).Trim();
                    if (!string.IsNullOrWhiteSpace(name) &&
                        !name.StartsWith("User ", StringComparison.OrdinalIgnoreCase) &&
                        !name.StartsWith("Skua ", StringComparison.OrdinalIgnoreCase))
                    {
                        return name;
                    }
                }
            }
            catch { }
        }

        string title = currentTitle?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(title) &&
            !title.StartsWith("User ", StringComparison.OrdinalIgnoreCase) &&
            !title.StartsWith("Skua ", StringComparison.OrdinalIgnoreCase))
        {
            return title;
        }

        return null;
    }

    private void PopulateAddToGroupMenu(MenuItem parentItem, TabItem tab, TabInfo? info, string currentTitle)
    {
        parentItem.Items.Clear();

        string? loggedInUsername = GetTabLoggedInUsername(tab, info, currentTitle);
        if (string.IsNullOrWhiteSpace(loggedInUsername))
        {
            MenuItem notLoggedInItem = new MenuItem
            {
                Header = "Please log in first",
                Icon = new MaterialIcon { Kind = MaterialIconKind.AlertCircleOutline, Width = 14, Height = 14 }
            };
            notLoggedInItem.Click += (s, ev) =>
            {
                Ioc.Default.GetService<IDialogService>()?.ShowMessageBox(
                    "Please log in to your game account first before adding it to an account group.",
                    "Account Not Logged In");
            };
            parentItem.Items.Add(notLoggedInItem);
            return;
        }

        string accountName = loggedInUsername;
        ISettingsService? settings = Ioc.Default.GetService<ISettingsService>();
        settings?.ReloadSettings();
        List<GroupData> groups = settings?.Get<List<GroupData>>("AccountGroups") ?? new List<GroupData>();

        if (groups.Count > 0)
        {
            foreach (var group in groups)
            {
                bool alreadyInGroup = group.Accounts.Contains(accountName, StringComparer.OrdinalIgnoreCase);
                MenuItem groupItem = new MenuItem
                {
                    Header = group.Name,
                    Icon = alreadyInGroup ? new MaterialIcon { Kind = MaterialIconKind.Check, Width = 14, Height = 14 } : null
                };
                groupItem.Click += (s, ev) =>
                {
                    AddAccountToGroupAndManaged(accountName, group, settings);
                };
                parentItem.Items.Add(groupItem);
            }
            parentItem.Items.Add(new Separator());
        }
        else
        {
            MenuItem emptyItem = new MenuItem { Header = "No groups created", IsEnabled = false };
            parentItem.Items.Add(emptyItem);
            parentItem.Items.Add(new Separator());
        }

        MenuItem newGroupItem = new MenuItem
        {
            Header = "+ New Group...",
            Icon = new MaterialIcon { Kind = MaterialIconKind.Plus, Width = 14, Height = 14 }
        };
        newGroupItem.Click += (s, ev) =>
        {
            var dialogService = Ioc.Default.GetService<IDialogService>();
            if (dialogService == null) return;

            var inputVm = new InputDialogViewModel("New Group", "Enter group name:", "Group Name", false);
            if (dialogService.ShowDialog(inputVm) == true && !string.IsNullOrWhiteSpace(inputVm.DialogTextInput))
            {
                string newGroupName = inputVm.DialogTextInput.Trim();
                var existing = groups.FirstOrDefault(g => g.Name.Equals(newGroupName, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    existing = new GroupData { Name = newGroupName };
                    groups.Add(existing);
                }
                AddAccountToGroupAndManaged(accountName, existing, settings);
            }
        };
        parentItem.Items.Add(newGroupItem);
    }

    private void AddAccountToGroupAndManaged(string accountName, GroupData group, ISettingsService? settings)
    {
        if (settings == null) return;
        var dialogService = Ioc.Default.GetService<IDialogService>();

        settings.ReloadSettings();

        // 1. Ensure the account exists in ManagedAccounts so it appears in VibeSkua Manager!
        var accs = settings.Get<Dictionary<string, AccountData>>("ManagedAccounts") ?? new();
        if (!accs.ContainsKey(accountName))
        {
            accs[accountName] = new AccountData
            {
                DisplayName = accountName,
                Password = string.Empty,
                Tags = new List<string>(),
                UseCheck = false
            };
            settings.Set("ManagedAccounts", accs);
        }

        // 2. Ensure account is in the selected group
        var currentGroups = settings.Get<List<GroupData>>("AccountGroups") ?? new List<GroupData>();
        var targetGroup = currentGroups.FirstOrDefault(g => g.Name.Equals(group.Name, StringComparison.OrdinalIgnoreCase));
        if (targetGroup == null)
        {
            targetGroup = new GroupData { Name = group.Name };
            currentGroups.Add(targetGroup);
        }

        if (!targetGroup.Accounts.Contains(accountName, StringComparer.OrdinalIgnoreCase))
        {
            targetGroup.Accounts.Add(accountName);
            settings.Set("AccountGroups", currentGroups);
            dialogService?.ShowMessageBox($"Added '{accountName}' to group '{targetGroup.Name}'.", "Add to Group");
        }
        else
        {
            dialogService?.ShowMessageBox($"'{accountName}' is already in group '{targetGroup.Name}'.", "Add to Group");
        }
    }

    #endregion Group Assignment Helper

    #endregion Tab Management
}