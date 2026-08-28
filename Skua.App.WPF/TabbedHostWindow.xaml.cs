using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Skua.WPF;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Messaging;

namespace Skua.App.WPF;

public partial class TabbedHostWindow : CustomWindow
{
    #region Win32 API

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

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
    private const uint WM_CLOSE = 0x0010;
    private const int GWL_HWNDPARENT = -8;
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int SW_SHOW = 5;
    private const int SW_HIDE = 0;
    private const uint SWP_NOMOVE = 0x0001;
    private const uint SWP_NOSIZE = 0x0002;
    private const uint SWP_SHOWWINDOW = 0x0040;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_ASYNCWINDOWPOS = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    #endregion Win32 API

    #region Tab State

    private class TabInfo
    {
        public Process Process { get; set; }
        public IntPtr ChildHwnd { get; set; } = IntPtr.Zero;
        public bool IsThrottled { get; set; } = false;
        public string GroupName { get; set; } = "";
        public string GroupColor { get; set; } = "";
    }

    public static readonly (string Name, string Hex)[] GroupColorPalette = new[]
    {
        ("Cyan", "#00BCD4"),
        ("Emerald", "#4CAF50"),
        ("Amber", "#FF9800"),
        ("Rose", "#F44336"),
        ("Purple", "#9C27B0"),
        ("Indigo", "#3F51B5"),
        ("Pink", "#E91E63"),
        ("Lime", "#8BC34A"),
        ("Orange", "#FF5722"),
        ("Teal", "#009688"),
        ("Slate", "#607D8B")
    };

    private class TabGroupConfig
    {
        public Dictionary<string, string> GroupColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> KnownGroups { get; set; } = new();
    }

    private static TabGroupConfig _groupConfig = new();

    private static void LoadGroupConfig()
    {
        try
        {
            string path = Skua.Core.Models.ClientFileSources.SkuaTabGroupsFile;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var loaded = System.Text.Json.JsonSerializer.Deserialize<TabGroupConfig>(json);
                if (loaded != null)
                {
                    _groupConfig = loaded;
                    if (_groupConfig.GroupColors == null)
                        _groupConfig.GroupColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    else
                        _groupConfig.GroupColors = new Dictionary<string, string>(_groupConfig.GroupColors, StringComparer.OrdinalIgnoreCase);

                    if (_groupConfig.KnownGroups == null)
                        _groupConfig.KnownGroups = new List<string>();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    private static void SaveGroupConfig()
    {
        try
        {
            string path = Skua.Core.Models.ClientFileSources.SkuaTabGroupsFile;
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = System.Text.Json.JsonSerializer.Serialize(_groupConfig, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
        }
    }

    public static string GetDefaultColorForGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return "#00BCD4";
        if (_groupConfig.GroupColors.TryGetValue(groupName, out string? customHex) && !string.IsNullOrWhiteSpace(customHex))
            return customHex;

        int hash = groupName.Aggregate(0, (h, c) => unchecked((h * 31) + c)) & int.MaxValue;
        return GroupColorPalette[hash % GroupColorPalette.Length].Hex;
    }

    private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsGroupHeader(TabItem? tab)
    {
        return tab?.Tag is string tag && tag.StartsWith("GroupHeader:", StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetGroupHeaderName(TabItem? tab)
    {
        if (tab?.Tag is string tag && tag.StartsWith("GroupHeader:", StringComparison.OrdinalIgnoreCase))
            return tag.Substring("GroupHeader:".Length);
        return null;
    }

    private void TabScrollViewer_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer sv)
        {
            if (e.Delta < 0)
                sv.LineRight();
            else
                sv.LineLeft();
            e.Handled = true;
        }
    }

    private string _initialArgs = "";
    private readonly Dictionary<TabItem, TabInfo> _tabs = new();
    private string? _scriptLoadTargetGroup = null;
    private string _lastJumpMap = "";
    private string _lastJumpCell = "";
    private string _lastJumpPlayer = "";
    private string _lastAcceptQuestId = "";
    private string _lastAcceptQuestItem = "";
    private bool _needsReposition = false;
    private bool _isClosing = false;
    private bool _isGridViewEnabled = false;
    private string? _gridViewTargetGroup = null;
    private TabItem _lastSelectedTab;
    private IntPtr _hostHwnd = IntPtr.Zero;
    private TabInfo _prewarmedTabInfo = null;

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
        next.Invoke();
    }

    #endregion Tab State

    public TabbedHostWindow(string initialArgs = "")
    {
        _initialArgs = initialArgs ?? "";
        LoadGroupConfig();
        InitializeComponent();
        Title = global::Skua.AppInfo.Title;
        TitleText = global::Skua.AppInfo.Title;

        System.Windows.Media.CompositionTarget.Rendering += (s, e) =>
        {
            if (_needsReposition)
            {
                _needsReposition = false;
                DoReposition();
            }
        };

        var options = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IScriptOption>();
        MiscOptionsMenu.DataContext = options;
        options.PropertyChanged += Options_PropertyChanged;

        Loaded += TabbedHostWindow_Loaded;
        LocationChanged += (s, e) => _needsReposition = true;
        SizeChanged += (s, e) => ScheduleReposition();
        DpiChanged += (s, e) => ScheduleReposition();
        StateChanged += (s, e) => DoReposition(); // Immediate for minimize/restore
        Activated += (s, e) => ScheduleReposition();
        Closing += OnWindowClosing;

        StrongReferenceMessenger.Default.Register<TabbedHostWindow, Skua.Core.Messaging.LoadScriptMessage, int>(this, (int)Skua.Core.Messaging.MessageChannels.ScriptStatus, (r, m) => r.BroadcastScriptAction(m.Path, WM_SKUA_LOAD_SCRIPT));
        StrongReferenceMessenger.Default.Register<TabbedHostWindow, Skua.Core.ViewModels.ScriptSchedulerViewModel.ArmySchedulerMessage>(this, (r, m) => r.OnArmySchedulerStart(m));
        StrongReferenceMessenger.Default.Register<TabbedHostWindow, Skua.Core.ViewModels.ScriptSchedulerViewModel.ArmySchedulerStopMessage>(this, (r, m) => r.OnArmySchedulerStop(m));

        StartPipeServer();

        var titleSyncTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        titleSyncTimer.Tick += (s, e) => SyncTabTitles();
        titleSyncTimer.Start();
    }

    private void SyncTabTitles()
    {
        if (_isClosing || _tabs.Count == 0) return;

        string dir = Path.Combine(Path.GetTempPath(), "SkuaTabs");
        if (!Directory.Exists(dir)) return;

        foreach (var kvp in _tabs)
        {
            TabItem tab = kvp.Key;
            TabInfo info = kvp.Value;
            if (info == null || info.Process == null) continue;

            try
            {
                string file = Path.Combine(dir, $"{info.Process.Id}.txt");
                if (File.Exists(file))
                {
                    string username = File.ReadAllText(file).Trim();
                    if (!string.IsNullOrWhiteSpace(username) && tab.Header is StackPanel headerPanel)
                    {
                        var titleBlock = headerPanel.Children.OfType<TextBlock>().FirstOrDefault(tb => tb.Text != "✕");
                        var editBox = headerPanel.Children.OfType<TextBox>().FirstOrDefault();
                        if (titleBlock != null && (titleBlock.Text.StartsWith("Skua ") || titleBlock.Text != username))
                        {
                            titleBlock.Text = username;
                            if (editBox != null) editBox.Text = username;
                        }
                    }
                }
            }
            catch { }
        }
    }

    #region Lifecycle

    private void TabbedHostWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _hostHwnd = new WindowInteropHelper(this).Handle;
        EnqueueSpawn(() => MaintainPrewarmedInstance());
        AddNewInstance(_initialArgs);
    }

    private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        _isClosing = true;
        var tabsToClose = _tabs.Values.ToList();
        var prewarmed = _prewarmedTabInfo;
        _tabs.Clear();
        _prewarmedTabInfo = null;

        Task.Run(() =>
        {
            Parallel.ForEach(tabsToClose, info =>
            {
                try
                {
                    if (info.ChildHwnd != IntPtr.Zero)
                        ShowWindow(info.ChildHwnd, SW_HIDE);
                    if (!info.Process.HasExited)
                        info.Process.Kill();
                    try { File.Delete(Path.Combine(Path.GetTempPath(), "SkuaTabs", $"{info.Process.Id}.txt")); } catch { }
                }
                catch { }
            });

            if (prewarmed != null)
            {
                try
                {
                    if (prewarmed.ChildHwnd != IntPtr.Zero)
                        ShowWindow(prewarmed.ChildHwnd, SW_HIDE);
                    if (!prewarmed.Process.HasExited)
                        prewarmed.Process.Kill();
                }
                catch { }
            }
        });
    }

    #endregion Lifecycle

    #region Pipe Server

    private void StartPipeServer()
    {
        Task.Run(() =>
        {
            while (true)
            {
                try
                {
                    using NamedPipeServerStream pipeServer = new("SkuaTabHostPipe", PipeDirection.In, 10, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                    pipeServer.WaitForConnection();
                    using StreamReader sr = new(pipeServer);
                    string argsJoined = sr.ReadLine();
                    if (argsJoined != null)
                    {
                        Dispatcher.BeginInvoke(new Action(() => AddNewInstance(argsJoined)));
                    }
                }
                catch { }
            }
        });
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
    private const int WM_SKUA_ACCEPT_QUEST = 0x0400 + 458;

    private void OnArmySchedulerStart(Skua.Core.ViewModels.ScriptSchedulerViewModel.ArmySchedulerMessage m)
    {
        m.Handled = true;
        try
        {
            var dialogService = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IDialogService>();

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
            var data = m.Queue.Select(x => new Skua.Core.ViewModels.ScriptSchedulerViewModel.SavedScriptItem { Path = x.Path, Id = x.Id, Name = x.Name }).ToList();
            System.IO.File.WriteAllText(tempFile, System.Text.Json.JsonSerializer.Serialize(data));

            foreach (var info in _tabs.Values)
                if (info.ChildHwnd != IntPtr.Zero)
                    PostMessage(info.ChildHwnd, WM_SKUA_ARMY_SCHEDULER, IntPtr.Zero, IntPtr.Zero);

            dialogService.ShowMessageBox("Army Scheduler payload has been broadcasted. All active accounts will now seamlessly rebuild their queues and begin executing the playlist.", "Army Scheduler Started");
        }
        catch { }
    }

    private void OnArmySchedulerStop(Skua.Core.ViewModels.ScriptSchedulerViewModel.ArmySchedulerStopMessage m)
    {
        m.Handled = true;
        try
        {
            foreach (var info in _tabs.Values)
                if (info.ChildHwnd != IntPtr.Zero)
                    PostMessage(info.ChildHwnd, WM_SKUA_ARMY_SCHEDULER_STOP, IntPtr.Zero, IntPtr.Zero);

            var dialogService = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IDialogService>();
            dialogService.ShowMessageBox("Stop signal has been successfully broadcasted. All accounts are now halting their schedulers.", "Army Scheduler Stopped");
        }
        catch { }
    }

    private void MenuItem_ArmyScheduler_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var windowService = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IWindowService>();
            windowService.RegisterManagedWindow("Scheduler", CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.ViewModels.ScriptSchedulerViewModel>());
            windowService.RegisterManagedWindow("Script Repo", CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.ViewModels.ScriptRepoViewModel>());
            windowService.ShowManagedWindow("Scheduler");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(ex.ToString(), "Error Opening Scheduler");
        }
    }

    public void ToggleGridView(string? targetGroup = null, bool? forceState = null)
    {
        string? normalizedGroup = string.IsNullOrWhiteSpace(targetGroup) ? null : targetGroup.Trim();

        if (forceState.HasValue)
        {
            _isGridViewEnabled = forceState.Value;
            _gridViewTargetGroup = _isGridViewEnabled ? normalizedGroup : null;
        }
        else
        {
            if (_isGridViewEnabled && string.Equals(_gridViewTargetGroup, normalizedGroup, StringComparison.OrdinalIgnoreCase))
            {
                _isGridViewEnabled = false;
                _gridViewTargetGroup = null;
            }
            else
            {
                _isGridViewEnabled = true;
                _gridViewTargetGroup = normalizedGroup;
            }
        }

        UpdateGridViewBorderColor();

        var targetTabs = _isGridViewEnabled ? GetTargetTabs(_gridViewTargetGroup).ToHashSet() : new HashSet<TabInfo>();

        foreach (var info in _tabs.Values)
        {
            if (info.ChildHwnd != IntPtr.Zero)
            {
                bool isTarget = targetTabs.Contains(info);
                PostMessage(info.ChildHwnd, WM_SKUA_GRIDVIEW, new IntPtr(isTarget ? 1 : 0), IntPtr.Zero);
            }
        }

        if (_isGridViewEnabled)
        {
            if (InstancesTabControl.SelectedItem != null && InstancesTabControl.SelectedItem != AddTabItem)
                _lastSelectedTab = InstancesTabControl.SelectedItem as TabItem;
        }
        else
        {
            if (_lastSelectedTab != null && InstancesTabControl.Items.Contains(_lastSelectedTab))
            {
                InstancesTabControl.SelectedItem = _lastSelectedTab;
            }
            else
            {
                var firstTab = InstancesTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t != AddTabItem);
                if (firstTab != null)
                    InstancesTabControl.SelectedItem = firstTab;
            }
        }

        DoReposition();
    }

    private void GridViewBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;

        string? groupToToggle = null;
        if (InstancesTabControl.SelectedItem is TabItem currentTab && _tabs.TryGetValue(currentTab, out TabInfo currentInfo))
        {
            if (!string.IsNullOrWhiteSpace(currentInfo.GroupName))
                groupToToggle = currentInfo.GroupName;
        }

        ToggleGridView(groupToToggle);
    }

    private void GridViewBorder_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        PopulateGridViewContextMenu();
        if (GridViewBorder.ContextMenu != null)
        {
            GridViewBorder.ContextMenu.IsOpen = true;
        }
    }

    private void PopulateGridViewContextMenu()
    {
        if (GridViewBorder.ContextMenu == null)
            GridViewBorder.ContextMenu = new ContextMenu();

        var ctx = GridViewBorder.ContextMenu;
        ctx.Items.Clear();

        MenuItem allTabsItem = new MenuItem
        {
            Header = "All Tabs",
            IsChecked = _isGridViewEnabled && string.IsNullOrWhiteSpace(_gridViewTargetGroup)
        };
        allTabsItem.Click += (s, e) => ToggleGridView(null, true);
        ctx.Items.Add(allTabsItem);

        var activeGroups = _tabs.Values
            .Select(i => i.GroupName)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g)
            .ToList();

        if (activeGroups.Count > 0)
        {
            ctx.Items.Add(new Separator());

            foreach (string grp in activeGroups)
            {
                string targetGrp = grp;
                MenuItem grpItem = new MenuItem
                {
                    Header = $"Group: {grp}",
                    IsChecked = _isGridViewEnabled && string.Equals(_gridViewTargetGroup, grp, StringComparison.OrdinalIgnoreCase)
                };

                string hex = _tabs.Values.FirstOrDefault(t => string.Equals(t.GroupName, grp, StringComparison.OrdinalIgnoreCase))?.GroupColor ?? GetDefaultColorForGroup(grp);
                try
                {
                    var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    grpItem.Icon = new System.Windows.Shapes.Rectangle
                    {
                        Width = 12,
                        Height = 12,
                        RadiusX = 2,
                        RadiusY = 2,
                        Fill = new System.Windows.Media.SolidColorBrush(col)
                    };
                }
                catch { }

                grpItem.Click += (s, e) => ToggleGridView(targetGrp, true);
                ctx.Items.Add(grpItem);
            }
        }

        if (_isGridViewEnabled)
        {
            ctx.Items.Add(new Separator());
            MenuItem exitGridItem = new MenuItem { Header = "Exit Grid View" };
            exitGridItem.Click += (s, e) => ToggleGridView(null, false);
            ctx.Items.Add(exitGridItem);
        }
    }

    private void GridViewBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isGridViewEnabled)
            GridViewBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48)); // #2D2D30
    }

    private void GridViewBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        UpdateGridViewBorderColor();
    }
    private void GlobeBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        PopulateArmyContextMenu();
        GlobeBorder.ContextMenu.IsOpen = true;
    }

    private void GlobeBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        GlobeBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48)); // #2D2D30
    }

    private void GlobeBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        GlobeBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 38)); // #252526
    }

    private void Options_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var options = sender as Skua.Core.Interfaces.IScriptOption;
        if (options == null) return;

        int optionId = -1;
        bool val = false;

        switch (e.PropertyName)
        {
            case "LagKiller": optionId = 1; val = options.LagKiller; break;
            case "HeadlessMode": optionId = 2; val = options.HeadlessMode; break;
            case "HidePlayers": optionId = 3; val = options.HidePlayers; break;
            case "DisableFX": optionId = 4; val = options.DisableFX; break;
            case "InfiniteRange": optionId = 5; val = options.InfiniteRange; break;
            case "UseFunctionBasedSkills": optionId = 8; val = options.UseFunctionBasedSkills; break;
            case "StreamerMode": optionId = 9; val = options.StreamerMode; break;
        }

        if (optionId != -1)
        {
            foreach (var info in _tabs.Values)
                if (info.ChildHwnd != IntPtr.Zero)
                    PostMessage(info.ChildHwnd, WM_SKUA_SET_OPTION, new IntPtr(optionId), new IntPtr(val ? 1 : 0));
        }
    }

    private IEnumerable<TabInfo> GetTargetTabs(string? targetGroup = null)
    {
        if (string.IsNullOrWhiteSpace(targetGroup) || targetGroup.Equals("All Tabs", StringComparison.OrdinalIgnoreCase))
            return _tabs.Values.Where(i => i.ChildHwnd != IntPtr.Zero);

        return _tabs.Values.Where(i => i.ChildHwnd != IntPtr.Zero && string.Equals(i.GroupName, targetGroup, StringComparison.OrdinalIgnoreCase));
    }

    #region Group Header Management

    private TabItem CreateGroupHeaderTab(string groupName)
    {
        TabItem headerTab = new TabItem
        {
            Tag = $"GroupHeader:{groupName}",
            Focusable = false,
            Cursor = System.Windows.Input.Cursors.Hand,
            AllowDrop = true
        };

        // Custom template to render only the Header content (the pill)
        var template = new ControlTemplate(typeof(TabItem));
        var cpFactory = new FrameworkElementFactory(typeof(ContentPresenter));
        cpFactory.SetValue(ContentPresenter.ContentSourceProperty, "Header");
        cpFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        cpFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        template.VisualTree = cpFactory;
        headerTab.Template = template;

        Border pillBorder = new Border
        {
            Tag = "PillBorder",
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(3, 2, 3, 2),
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand
        };

        StackPanel pillPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        TextBlock iconBlock = new TextBlock
        {
            Tag = "PillIcon",
            Text = "●",
            FontSize = 10,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock nameBlock = new TextBlock
        {
            Tag = "PillName",
            Text = groupName,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock countBlock = new TextBlock
        {
            Tag = "PillCount",
            Text = "",
            FontSize = 11,
            FontWeight = FontWeights.Normal,
            Opacity = 0.85,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };

        pillPanel.Children.Add(iconBlock);
        pillPanel.Children.Add(nameBlock);
        pillPanel.Children.Add(countBlock);
        pillBorder.Child = pillPanel;
        headerTab.Header = pillBorder;

        UpdateGroupHeaderAppearance(headerTab, groupName);

        // Hover effect
        pillBorder.MouseEnter += (s, ev) =>
        {
            string hex = _tabs.Values.FirstOrDefault(t => string.Equals(t.GroupName, groupName, StringComparison.OrdinalIgnoreCase))?.GroupColor ?? GetDefaultColorForGroup(groupName);
            try
            {
                var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                pillBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(80, col.R, col.G, col.B));
            }
            catch { }
        };
        pillBorder.MouseLeave += (s, ev) =>
        {
            UpdateGroupHeaderAppearance(headerTab, groupName);
        };

        // Left Click -> Toggle Collapse/Expand
        pillBorder.PreviewMouseLeftButtonDown += (s, ev) =>
        {
            ev.Handled = true;
            ToggleGroupCollapse(groupName);
        };

        // Right Click -> Group Context Menu
        ContextMenu ctx = new ContextMenu();
        ctx.Opened += (s, ev) => PopulateGroupContextMenu(groupName, ctx);
        PopulateGroupContextMenu(groupName, ctx);
        pillBorder.ContextMenu = ctx;

        pillBorder.PreviewMouseRightButtonDown += (s, ev) =>
        {
            ev.Handled = true;
            PopulateGroupContextMenu(groupName, ctx);
            ctx.IsOpen = true;
        };

        // Drag & Drop to reorder entire group
        Point startPoint = new Point();
        pillBorder.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent, new System.Windows.Input.MouseButtonEventHandler((s, ev) =>
        {
            startPoint = ev.GetPosition(null);
        }), true);
        pillBorder.PreviewMouseMove += (s, ev) =>
        {
            if (ev.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                Point mousePos = ev.GetPosition(null);
                Vector diff = startPoint - mousePos;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    DragDrop.DoDragDrop(headerTab, headerTab, DragDropEffects.Move);
                }
            }
        };

        headerTab.Drop += (s, ev) =>
        {
            if (ev.Data.GetDataPresent(typeof(TabItem)))
            {
                TabItem droppedTab = (TabItem)ev.Data.GetData(typeof(TabItem));
                if (droppedTab != null && droppedTab != headerTab)
                {
                    int targetIndex = InstancesTabControl.Items.IndexOf(headerTab);
                    if (IsGroupHeader(droppedTab))
                    {
                        string? droppedGroup = GetGroupHeaderName(droppedTab);
                        if (!string.IsNullOrWhiteSpace(droppedGroup))
                        {
                            MoveWholeGroup(droppedGroup, targetIndex);
                        }
                    }
                    else if (_tabs.ContainsKey(droppedTab))
                    {
                        InstancesTabControl.Items.Remove(droppedTab);
                        int newIdx = Math.Min(targetIndex, InstancesTabControl.Items.Count - 1);
                        if (newIdx < 0) newIdx = 0;
                        InstancesTabControl.Items.Insert(newIdx, droppedTab);
                        InstancesTabControl.SelectedItem = droppedTab;
                        SetTabGroup(droppedTab, groupName);
                    }
                }
            }
        };

        return headerTab;
    }

    private void UpdateGroupHeaderAppearance(TabItem headerTab, string groupName)
    {
        if (headerTab.Header is not Border pillBorder) return;
        if (pillBorder.Child is not StackPanel pillPanel) return;

        var iconBlock = pillPanel.Children.OfType<TextBlock>().FirstOrDefault(tb => (string)tb.Tag == "PillIcon");
        var nameBlock = pillPanel.Children.OfType<TextBlock>().FirstOrDefault(tb => (string)tb.Tag == "PillName");
        var countBlock = pillPanel.Children.OfType<TextBlock>().FirstOrDefault(tb => (string)tb.Tag == "PillCount");

        string hex = _tabs.Values.FirstOrDefault(t => string.Equals(t.GroupName, groupName, StringComparison.OrdinalIgnoreCase))?.GroupColor ?? GetDefaultColorForGroup(groupName);
        System.Windows.Media.Color col;
        try
        {
            col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            col = System.Windows.Media.Color.FromRgb(0, 188, 212);
        }

        bool isCollapsed = _collapsedGroups.Contains(groupName);
        int memberCount = _tabs.Values.Count(t => string.Equals(t.GroupName, groupName, StringComparison.OrdinalIgnoreCase));

        if (nameBlock != null) nameBlock.Text = groupName;

        if (isCollapsed)
        {
            if (iconBlock != null)
            {
                iconBlock.Text = "▸";
                iconBlock.FontSize = 11;
            }
            if (countBlock != null)
            {
                countBlock.Text = $"({memberCount})";
                countBlock.Visibility = Visibility.Visible;
            }
            pillBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(60, col.R, col.G, col.B));
        }
        else
        {
            if (iconBlock != null)
            {
                iconBlock.Text = "●";
                iconBlock.FontSize = 10;
            }
            if (countBlock != null)
            {
                countBlock.Visibility = Visibility.Collapsed;
            }
            pillBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(35, col.R, col.G, col.B));
        }

        pillBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(col);
        var brush = new System.Windows.Media.SolidColorBrush(col);
        if (iconBlock != null) iconBlock.Foreground = brush;
        if (nameBlock != null) nameBlock.Foreground = brush;
        if (countBlock != null) countBlock.Foreground = brush;
    }

    private void ToggleGroupCollapse(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return;

        bool isNowCollapsed;
        if (_collapsedGroups.Contains(groupName))
        {
            _collapsedGroups.Remove(groupName);
            isNowCollapsed = false;
        }
        else
        {
            _collapsedGroups.Add(groupName);
            isNowCollapsed = true;
        }

        foreach (var kvp in _tabs)
        {
            if (string.Equals(kvp.Value.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
            {
                kvp.Key.Visibility = isNowCollapsed ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        if (isNowCollapsed && InstancesTabControl.SelectedItem is TabItem currentTab && _tabs.TryGetValue(currentTab, out var curInfo) && string.Equals(curInfo.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
        {
            var firstVis = InstancesTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t != AddTabItem && !IsGroupHeader(t) && t.Visibility == Visibility.Visible && _tabs.ContainsKey(t));
            if (firstVis != null)
            {
                InstancesTabControl.SelectedItem = firstVis;
            }
        }

        RefreshGroupHeaders();
        DoReposition();
    }

    private void PopulateGroupContextMenu(string groupName, ContextMenu ctx)
    {
        ctx.Items.Clear();

        // 1. Rename Group...
        MenuItem renameItem = new MenuItem { Header = "Rename Group..." };
        renameItem.Click += (s, ev) =>
        {
            var vm = new Skua.Core.ViewModels.InputDialogViewModel("Rename Tab Group", "Enter new group name:", groupName, false);
            if (CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IDialogService>().ShowDialog(vm) == true)
            {
                string newName = vm.DialogTextInput?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(newName) && !string.Equals(newName, groupName, StringComparison.OrdinalIgnoreCase))
                {
                    RenameGroup(groupName, newName);
                }
            }
        };
        ctx.Items.Add(renameItem);

        // 2. Change Group Color
        MenuItem colorItem = new MenuItem { Header = "Change Group Color" };
        foreach (var (name, hex) in GroupColorPalette)
        {
            MenuItem cItem = new MenuItem { Header = name };
            try
            {
                var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                cItem.Icon = new System.Windows.Shapes.Rectangle
                {
                    Width = 12,
                    Height = 12,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = new System.Windows.Media.SolidColorBrush(col)
                };
            }
            catch { }

            string chosenHex = hex;
            cItem.Click += (s, ev) =>
            {
                _groupConfig.GroupColors[groupName] = chosenHex;
                SaveGroupConfig();

                foreach (var kvp in _tabs)
                {
                    if (string.Equals(kvp.Value.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                    {
                        UpdateTabGroupUI(kvp.Key, groupName, chosenHex);
                    }
                }
                RefreshGroupHeaders();
                UpdateGridViewBorderColor();
            };
            colorItem.Items.Add(cItem);
        }
        ctx.Items.Add(colorItem);

        ctx.Items.Add(new Separator());

        // 3. Grid View: This Group
        MenuItem gridGroupItem = new MenuItem { Header = $"Grid View (Group '{groupName}')" };
        gridGroupItem.Click += (s, ev) => ToggleGridView(groupName, true);
        ctx.Items.Add(gridGroupItem);

        // 4. Auto-Arrange Tabs
        MenuItem arrangeItem = new MenuItem { Header = "Auto-Arrange Tabs by Group" };
        arrangeItem.Click += (s, ev) => AutoArrangeTabsByGroup();
        ctx.Items.Add(arrangeItem);

        ctx.Items.Add(new Separator());

        // 5. Delete Group (Ungroup Tabs & Remove from Config)
        MenuItem deleteGroupItem = new MenuItem { Header = $"Delete Group '{groupName}' (Ungroup Tabs)" };
        deleteGroupItem.Click += (s, ev) => DeleteGroup(groupName);
        ctx.Items.Add(deleteGroupItem);

        // 6. Close Group (Close All Tabs in Group)
        MenuItem closeGroupItem = new MenuItem { Header = $"Close Group ('{groupName}')" };
        closeGroupItem.Click += (s, ev) =>
        {
            var toRemove = _tabs.Where(kvp => string.Equals(kvp.Value.GroupName, groupName, StringComparison.OrdinalIgnoreCase))
                                .Select(kvp => kvp.Key)
                                .ToList();
            foreach (var t in toRemove) CloseTab(t);
        };
        ctx.Items.Add(closeGroupItem);
    }

    private void RenameGroup(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;

        if (_groupConfig.KnownGroups.Contains(oldName, StringComparer.OrdinalIgnoreCase))
        {
            _groupConfig.KnownGroups.RemoveAll(g => string.Equals(g, oldName, StringComparison.OrdinalIgnoreCase));
        }
        if (!_groupConfig.KnownGroups.Contains(newName, StringComparer.OrdinalIgnoreCase))
        {
            _groupConfig.KnownGroups.Add(newName);
        }

        if (_groupConfig.GroupColors.TryGetValue(oldName, out string? color))
        {
            _groupConfig.GroupColors.Remove(oldName);
            _groupConfig.GroupColors[newName] = color;
        }
        SaveGroupConfig();

        if (_collapsedGroups.Contains(oldName))
        {
            _collapsedGroups.Remove(oldName);
            _collapsedGroups.Add(newName);
        }

        if (string.Equals(_gridViewTargetGroup, oldName, StringComparison.OrdinalIgnoreCase))
        {
            _gridViewTargetGroup = newName;
        }

        foreach (var kvp in _tabs)
        {
            if (string.Equals(kvp.Value.GroupName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                kvp.Value.GroupName = newName;
                UpdateTabGroupUI(kvp.Key, newName, kvp.Value.GroupColor);
            }
        }

        var headerTab = InstancesTabControl.Items.OfType<TabItem>().FirstOrDefault(t => string.Equals(GetGroupHeaderName(t), oldName, StringComparison.OrdinalIgnoreCase));
        if (headerTab != null)
        {
            headerTab.Tag = $"GroupHeader:{newName}";
        }

        RefreshGroupHeaders();
        UpdateGridViewBorderColor();
    }

    private void DeleteGroup(string groupName)
    {
        if (string.IsNullOrWhiteSpace(groupName)) return;

        _groupConfig.KnownGroups.RemoveAll(g => string.Equals(g, groupName, StringComparison.OrdinalIgnoreCase));
        _groupConfig.GroupColors.Remove(groupName);
        SaveGroupConfig();

        _collapsedGroups.Remove(groupName);

        if (string.Equals(_gridViewTargetGroup, groupName, StringComparison.OrdinalIgnoreCase))
        {
            _gridViewTargetGroup = null;
        }

        var memberTabs = _tabs.Where(kvp => string.Equals(kvp.Value.GroupName, groupName, StringComparison.OrdinalIgnoreCase)).Select(kvp => kvp.Key).ToList();
        foreach (var tab in memberTabs)
        {
            SetTabGroup(tab, "");
        }

        RefreshGroupHeaders();
        UpdateGridViewBorderColor();
    }

    private void MoveWholeGroup(string groupName, int targetIndex)
    {
        var headerTab = InstancesTabControl.Items.OfType<TabItem>().FirstOrDefault(t => string.Equals(GetGroupHeaderName(t), groupName, StringComparison.OrdinalIgnoreCase));
        var memberTabs = _tabs.Where(kvp => string.Equals(kvp.Value.GroupName, groupName, StringComparison.OrdinalIgnoreCase)).Select(kvp => kvp.Key).ToList();

        var allGroupItems = new List<TabItem>();
        if (headerTab != null) allGroupItems.Add(headerTab);
        allGroupItems.AddRange(memberTabs);

        foreach (var item in allGroupItems)
        {
            InstancesTabControl.Items.Remove(item);
        }

        int insertIdx = Math.Min(targetIndex, InstancesTabControl.Items.Count - 1);
        if (insertIdx < 0) insertIdx = 0;

        for (int i = 0; i < allGroupItems.Count; i++)
        {
            InstancesTabControl.Items.Insert(insertIdx + i, allGroupItems[i]);
        }

        RefreshGroupHeaders();
    }

    public void RefreshGroupHeaders()
    {
        var activeGroups = _tabs.Values
            .Select(i => i.GroupName)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // 1. Remove orphan headers
        var existingHeaders = InstancesTabControl.Items.OfType<TabItem>().Where(IsGroupHeader).ToList();
        foreach (var h in existingHeaders)
        {
            string? grp = GetGroupHeaderName(h);
            if (grp == null || !activeGroups.Contains(grp, StringComparer.OrdinalIgnoreCase))
            {
                InstancesTabControl.Items.Remove(h);
            }
        }

        // 2. Ensure each active group has a header placed before its first member tab
        foreach (string grp in activeGroups)
        {
            var memberTabs = InstancesTabControl.Items.OfType<TabItem>()
                .Where(t => _tabs.TryGetValue(t, out var info) && string.Equals(info.GroupName, grp, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (memberTabs.Count == 0) continue;

            var header = InstancesTabControl.Items.OfType<TabItem>().FirstOrDefault(t => string.Equals(GetGroupHeaderName(t), grp, StringComparison.OrdinalIgnoreCase));
            if (header == null)
            {
                header = CreateGroupHeaderTab(grp);
                int firstMemberIdx = InstancesTabControl.Items.IndexOf(memberTabs[0]);
                InstancesTabControl.Items.Insert(firstMemberIdx, header);
            }
            else
            {
                int currentHeaderIdx = InstancesTabControl.Items.IndexOf(header);
                int firstMemberIdx = InstancesTabControl.Items.IndexOf(memberTabs[0]);
                if (currentHeaderIdx > firstMemberIdx)
                {
                    InstancesTabControl.Items.Remove(header);
                    firstMemberIdx = InstancesTabControl.Items.IndexOf(memberTabs[0]);
                    InstancesTabControl.Items.Insert(firstMemberIdx, header);
                }
                UpdateGroupHeaderAppearance(header, grp);
            }

            // Sync member visibility
            bool isCollapsed = _collapsedGroups.Contains(grp);
            foreach (var mTab in memberTabs)
            {
                mTab.Visibility = isCollapsed ? Visibility.Collapsed : Visibility.Visible;
            }
        }
    }

    #endregion Group Header Management

    private void SetTabGroup(TabItem tab, string groupName, string? customColor = null)
    {
        if (!_tabs.TryGetValue(tab, out TabInfo info)) return;

        string trimmed = groupName?.Trim() ?? "";

        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            if (!_groupConfig.KnownGroups.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            {
                _groupConfig.KnownGroups.Add(trimmed);
                SaveGroupConfig();
            }

            if (string.IsNullOrWhiteSpace(customColor))
            {
                var existingTabWithGroup = _tabs.Values.FirstOrDefault(t => string.Equals(t.GroupName, trimmed, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(t.GroupColor));
                if (existingTabWithGroup != null)
                    customColor = existingTabWithGroup.GroupColor;
                else if (_groupConfig.GroupColors.TryGetValue(trimmed, out string? savedColor))
                    customColor = savedColor;
            }
            else
            {
                _groupConfig.GroupColors[trimmed] = customColor;
                SaveGroupConfig();
            }
        }

        UpdateTabGroupUI(tab, trimmed, customColor);
        RefreshGroupHeaders();
    }

    private void UpdateTabGroupUI(TabItem tab, string groupName, string? customColor = null)
    {
        if (!_tabs.TryGetValue(tab, out TabInfo info)) return;

        info.GroupName = groupName?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(customColor))
            info.GroupColor = customColor;
        else if (!string.IsNullOrWhiteSpace(info.GroupName))
            info.GroupColor = GetDefaultColorForGroup(info.GroupName);
        else
            info.GroupColor = "";

        if (tab.Header is StackPanel panel)
        {
            var dot = panel.Children.OfType<System.Windows.Shapes.Ellipse>().FirstOrDefault(e => (string)e.Tag == "GroupDot");
            if (dot != null)
            {
                if (string.IsNullOrWhiteSpace(info.GroupName))
                {
                    dot.Visibility = Visibility.Collapsed;
                }
                else
                {
                    string hex = string.IsNullOrWhiteSpace(info.GroupColor) ? GetDefaultColorForGroup(info.GroupName) : info.GroupColor;
                    try
                    {
                        var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                        dot.Fill = new System.Windows.Media.SolidColorBrush(col);
                        dot.Visibility = Visibility.Visible;
                    }
                    catch
                    {
                        dot.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(info.GroupName) && _collapsedGroups.Contains(info.GroupName))
        {
            tab.Visibility = Visibility.Collapsed;
        }
        else
        {
            tab.Visibility = Visibility.Visible;
        }
    }

    public void AutoArrangeTabsByGroup()
    {
        var existingHeaders = InstancesTabControl.Items.OfType<TabItem>().Where(IsGroupHeader).ToList();
        foreach (var h in existingHeaders) InstancesTabControl.Items.Remove(h);

        var clientTabs = InstancesTabControl.Items.OfType<TabItem>()
            .Where(t => _tabs.ContainsKey(t))
            .ToList();

        if (clientTabs.Count == 0) return;

        var selectedTab = InstancesTabControl.SelectedItem as TabItem;

        var sortedTabs = clientTabs
            .OrderBy(t => string.IsNullOrWhiteSpace(_tabs[t].GroupName) ? 1 : 0) // grouped tabs first, ungrouped last
            .ThenBy(t => _tabs[t].GroupName, StringComparer.OrdinalIgnoreCase) // group name A-Z
            .ThenBy(t =>
            {
                if (t.Header is StackPanel sp)
                {
                    var tb = sp.Children.OfType<TextBlock>().FirstOrDefault(b => b.Text != "✕");
                    return tb?.Text ?? "";
                }
                return "";
            })
            .ToList();

        foreach (var tab in clientTabs) InstancesTabControl.Items.Remove(tab);

        int insertPos = 0;
        string? currentGroup = null;

        foreach (var tab in sortedTabs)
        {
            string grp = _tabs[tab].GroupName;
            if (!string.IsNullOrWhiteSpace(grp) && !string.Equals(grp, currentGroup, StringComparison.OrdinalIgnoreCase))
            {
                currentGroup = grp;
                var header = CreateGroupHeaderTab(grp);
                InstancesTabControl.Items.Insert(insertPos++, header);
            }
            else if (string.IsNullOrWhiteSpace(grp))
            {
                currentGroup = null;
            }

            InstancesTabControl.Items.Insert(insertPos++, tab);
        }

        RefreshGroupHeaders();

        if (selectedTab != null && InstancesTabControl.Items.Contains(selectedTab) && selectedTab.Visibility == Visibility.Visible)
        {
            InstancesTabControl.SelectedItem = selectedTab;
        }
        else
        {
            var firstVis = InstancesTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t != AddTabItem && !IsGroupHeader(t) && t.Visibility == Visibility.Visible && _tabs.ContainsKey(t));
            if (firstVis != null) InstancesTabControl.SelectedItem = firstVis;
        }

        DoReposition();
    }

    private void MenuItem_ArrangeTabs_Click(object sender, RoutedEventArgs e)
    {
        AutoArrangeTabsByGroup();
    }

    private void PopulateTabContextMenu(TabItem tab, ContextMenu ctx)
    {
        ctx.Items.Clear();
        if (!_tabs.TryGetValue(tab, out TabInfo info)) return;

        // 1. Assign to Group
        MenuItem assignGroupItem = new MenuItem { Header = "Assign to Group" };

        var existingGroups = _groupConfig.KnownGroups
            .Concat(_tabs.Values.Select(i => i.GroupName))
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g)
            .ToList();

        foreach (string grp in existingGroups)
        {
            MenuItem grpItem = new MenuItem
            {
                Header = grp,
                IsChecked = string.Equals(info.GroupName, grp, StringComparison.OrdinalIgnoreCase)
            };
            string targetGrp = grp;
            grpItem.Click += (s, ev) => SetTabGroup(tab, targetGrp);
            assignGroupItem.Items.Add(grpItem);
        }

        if (existingGroups.Count > 0)
            assignGroupItem.Items.Add(new Separator());

        MenuItem newGroupItem = new MenuItem { Header = "+ New Group..." };
        newGroupItem.Click += (s, ev) =>
        {
            var vm = new Skua.Core.ViewModels.InputDialogViewModel("New Tab Group", "Enter group name:", "e.g. Ultra Team", false);
            if (CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IDialogService>().ShowDialog(vm) == true)
            {
                string newName = vm.DialogTextInput?.Trim() ?? "";
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    SetTabGroup(tab, newName);
                }
            }
        };
        assignGroupItem.Items.Add(newGroupItem);

        if (!string.IsNullOrWhiteSpace(info.GroupName))
        {
            MenuItem removeGroupItem = new MenuItem { Header = "Remove from Group" };
            removeGroupItem.Click += (s, ev) => SetTabGroup(tab, "");
            assignGroupItem.Items.Add(removeGroupItem);
        }

        ctx.Items.Add(assignGroupItem);

        // 2. Change Group Color
        if (!string.IsNullOrWhiteSpace(info.GroupName))
        {
            MenuItem colorItem = new MenuItem { Header = "Change Group Color" };
            foreach (var (name, hex) in GroupColorPalette)
            {
                MenuItem cItem = new MenuItem { Header = name };
                try
                {
                    var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    cItem.Icon = new System.Windows.Shapes.Rectangle
                    {
                        Width = 12,
                        Height = 12,
                        RadiusX = 2,
                        RadiusY = 2,
                        Fill = new System.Windows.Media.SolidColorBrush(col)
                    };
                }
                catch { }

                string chosenHex = hex;
                cItem.Click += (s, ev) =>
                {
                    _groupConfig.GroupColors[info.GroupName] = chosenHex;
                    SaveGroupConfig();

                    foreach (var kvp in _tabs)
                    {
                        if (string.Equals(kvp.Value.GroupName, info.GroupName, StringComparison.OrdinalIgnoreCase))
                        {
                            UpdateTabGroupUI(kvp.Key, info.GroupName, chosenHex);
                        }
                    }
                    RefreshGroupHeaders();
                    UpdateGridViewBorderColor();
                };
                colorItem.Items.Add(cItem);
            }
            ctx.Items.Add(colorItem);
        }

        ctx.Items.Add(new Separator());

        // 3. Tab Group Batch Actions & Arrangement
        if (!string.IsNullOrWhiteSpace(info.GroupName))
        {
            MenuItem gridGroupItem = new MenuItem { Header = $"Grid View (Group '{info.GroupName}')" };
            gridGroupItem.Click += (s, ev) => ToggleGridView(info.GroupName, true);
            ctx.Items.Add(gridGroupItem);

            MenuItem deleteGroupItem = new MenuItem { Header = $"Delete Group '{info.GroupName}' (Ungroup Tabs)" };
            deleteGroupItem.Click += (s, ev) => DeleteGroup(info.GroupName);
            ctx.Items.Add(deleteGroupItem);

            MenuItem closeGroupItem = new MenuItem { Header = $"Close Group ('{info.GroupName}')" };
            closeGroupItem.Click += (s, ev) =>
            {
                var toRemove = _tabs.Where(kvp => string.Equals(kvp.Value.GroupName, info.GroupName, StringComparison.OrdinalIgnoreCase))
                                    .Select(kvp => kvp.Key)
                                    .ToList();
                foreach (var t in toRemove) CloseTab(t);
            };
            ctx.Items.Add(closeGroupItem);
        }

        MenuItem arrangeItem = new MenuItem { Header = "Auto-Arrange Tabs by Group" };
        arrangeItem.Click += (s, ev) => AutoArrangeTabsByGroup();
        ctx.Items.Add(arrangeItem);

        ctx.Items.Add(new Separator());

        // 4. Tab Closing Actions
        MenuItem closeItem = new MenuItem { Header = "Close Tab" };
        closeItem.Click += (s, ev) => CloseTab(tab);

        MenuItem closeOthersItem = new MenuItem { Header = "Close Other Tabs" };
        closeOthersItem.Click += (s, ev) =>
        {
            var toRemove = InstancesTabControl.Items.OfType<TabItem>().Where(t => t != tab && _tabs.ContainsKey(t)).ToList();
            foreach (var t in toRemove) CloseTab(t);
        };

        MenuItem closeRightItem = new MenuItem { Header = "Close Tabs to the Right" };
        closeRightItem.Click += (s, ev) =>
        {
            int idx = InstancesTabControl.Items.IndexOf(tab);
            var toRemove = InstancesTabControl.Items.OfType<TabItem>().Where((t, i) => i > idx && _tabs.ContainsKey(t)).ToList();
            foreach (var t in toRemove) CloseTab(t);
        };

        ctx.Items.Add(closeItem);
        ctx.Items.Add(closeOthersItem);
        ctx.Items.Add(closeRightItem);
    }

    private void PopulateArmyContextMenu()
    {
        var groups = _tabs.Values
            .Select(i => i.GroupName)
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g)
            .ToList();

        SetupArmyMenuItem(MenuStartScripts, "Start Scripts", StartScriptsAction, groups);
        SetupArmyMenuItem(MenuStopScripts, "Stop Scripts", StopScriptsAction, groups);
        SetupArmyMenuItem(MenuLoadScripts, "Load Script to", LoadScriptAction, groups);
        SetupArmyMenuItem(MenuLoginClients, "Login Clients", LoginClientsAction, groups);
        SetupArmyMenuItem(MenuLogoutClients, "Logout Clients", LogoutClientsAction, groups);
        SetupArmyMenuItem(MenuJumpMap, "Jump to Map...", JumpMapAction, groups);
        SetupArmyMenuItem(MenuJumpPlayer, "Jump to Player...", JumpPlayerAction, groups);
        SetupArmyMenuItem(MenuAcceptQuest, "Accept Quest...", AcceptQuestAction, groups);
    }

    private void SetupArmyMenuItem(MenuItem item, string baseTitle, Action<string?> action, List<string> groups)
    {
        if (item == null) return;
        item.Items.Clear();

        if (groups.Count == 0)
        {
            item.Header = baseTitle.Contains("...") ? baseTitle.Replace("...", " (All)...") : $"{baseTitle} (All)";
        }
        else
        {
            item.Header = baseTitle;

            MenuItem allItem = new MenuItem { Header = "All Tabs" };
            allItem.Click += (s, e) => action(null);
            item.Items.Add(allItem);

            item.Items.Add(new Separator());

            foreach (string grp in groups)
            {
                string targetGrp = grp;
                MenuItem grpItem = new MenuItem { Header = $"Group: {grp}" };

                string hex = _tabs.Values.FirstOrDefault(t => string.Equals(t.GroupName, grp, StringComparison.OrdinalIgnoreCase))?.GroupColor ?? GetDefaultColorForGroup(grp);
                try
                {
                    var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    grpItem.Icon = new System.Windows.Shapes.Rectangle
                    {
                        Width = 12,
                        Height = 12,
                        RadiusX = 2,
                        RadiusY = 2,
                        Fill = new System.Windows.Media.SolidColorBrush(col)
                    };
                }
                catch { }

                grpItem.Click += (s, e) => action(targetGrp);
                item.Items.Add(grpItem);
            }
        }
    }

    private async void StartScriptsAction(string? targetGroup = null)
    {
        var targetTabs = GetTargetTabs(targetGroup).ToList();
        if (targetTabs.Count == 0) return;

        if (MenuStartScripts != null) MenuStartScripts.IsEnabled = false;
        if (MenuStopScripts != null) MenuStopScripts.IsEnabled = false;

        foreach (var info in targetTabs)
            if (info.ChildHwnd != IntPtr.Zero)
                PostMessage(info.ChildHwnd, WM_SKUA_SET_OPTION, new IntPtr(99), new IntPtr(1));

        await Task.Delay(2000);

        if (MenuStartScripts != null) MenuStartScripts.IsEnabled = true;
        if (MenuStopScripts != null) MenuStopScripts.IsEnabled = true;
    }

    private async void StopScriptsAction(string? targetGroup = null)
    {
        var targetTabs = GetTargetTabs(targetGroup).ToList();
        if (targetTabs.Count == 0) return;

        if (MenuStartScripts != null) MenuStartScripts.IsEnabled = false;
        if (MenuStopScripts != null) MenuStopScripts.IsEnabled = false;

        foreach (var info in targetTabs)
            if (info.ChildHwnd != IntPtr.Zero)
                PostMessage(info.ChildHwnd, WM_SKUA_SET_OPTION, new IntPtr(99), new IntPtr(0));

        await Task.Delay(4000);

        if (MenuStartScripts != null) MenuStartScripts.IsEnabled = true;
        if (MenuStopScripts != null) MenuStopScripts.IsEnabled = true;
    }

    private void LoadScriptAction(string? targetGroup = null)
    {
        _scriptLoadTargetGroup = targetGroup;
        var windowService = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IWindowService>();
        windowService.RegisterManagedWindow("Script Repo", CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.ViewModels.ScriptRepoViewModel>());
        windowService.ShowManagedWindow("Script Repo");
    }

    private async void LoginClientsAction(string? targetGroup = null)
    {
        var targetTabs = GetTargetTabs(targetGroup).ToList();
        if (targetTabs.Count == 0) return;

        if (MenuLoginClients != null) MenuLoginClients.IsEnabled = false;
        string originalHeader = MenuLoginClients?.Header?.ToString() ?? "Login Clients";

        int count = 1;
        int total = targetTabs.Count;

        foreach (var info in targetTabs)
        {
            if (info.ChildHwnd != IntPtr.Zero)
            {
                if (MenuLoginClients != null) MenuLoginClients.Header = $"Logging in... ({count}/{total})";
                PostMessage(info.ChildHwnd, WM_SKUA_LOGIN, IntPtr.Zero, IntPtr.Zero);
                await Task.Delay(2000);
                count++;
            }
        }

        if (MenuLoginClients != null)
        {
            MenuLoginClients.Header = originalHeader;
            MenuLoginClients.IsEnabled = true;
        }
    }

    private void LogoutClientsAction(string? targetGroup = null)
    {
        foreach (var info in GetTargetTabs(targetGroup))
            if (info.ChildHwnd != IntPtr.Zero)
                PostMessage(info.ChildHwnd, WM_SKUA_LOGOUT, IntPtr.Zero, IntPtr.Zero);
    }

    private void JumpMapAction(string? targetGroup = null)
    {
        string title = string.IsNullOrWhiteSpace(targetGroup) ? "Jump Army to Map / Cell" : $"Jump [{targetGroup}] to Map / Cell";
        var vm = new Skua.Core.ViewModels.InputDialogViewModel(title, "Enter target location:", "Map (e.g., yulgar-829472)", "Cell (e.g., Enter)", false);
        vm.DialogTextInput = _lastJumpMap;
        vm.SecondTextInput = _lastJumpCell;

        if (CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IDialogService>().ShowDialog(vm) == true)
        {
            string targetMap = vm.DialogTextInput?.Trim() ?? "";
            string targetCell = vm.SecondTextInput?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(targetMap) || !string.IsNullOrWhiteSpace(targetCell))
            {
                _lastJumpMap = targetMap;
                _lastJumpCell = targetCell;
                string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_jump.txt");
                System.IO.File.WriteAllLines(tempFile, new[] { targetMap, targetCell });

                foreach (var info in GetTargetTabs(targetGroup))
                    if (info.ChildHwnd != IntPtr.Zero)
                        PostMessage(info.ChildHwnd, WM_SKUA_JUMP_MAP, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }

    private void JumpPlayerAction(string? targetGroup = null)
    {
        string title = string.IsNullOrWhiteSpace(targetGroup) ? "Jump Army to Player" : $"Jump [{targetGroup}] to Player";
        var vm = new Skua.Core.ViewModels.InputDialogViewModel(title, "Enter target player username:", "e.g., Artix", false);
        vm.DialogTextInput = _lastJumpPlayer;

        if (CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IDialogService>().ShowDialog(vm) == true)
        {
            string targetPlayer = vm.DialogTextInput?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(targetPlayer))
            {
                _lastJumpPlayer = targetPlayer;
                string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_jump_player.txt");
                System.IO.File.WriteAllText(tempFile, targetPlayer);

                foreach (var info in GetTargetTabs(targetGroup))
                    if (info.ChildHwnd != IntPtr.Zero)
                        PostMessage(info.ChildHwnd, WM_SKUA_JUMP_PLAYER, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }

    private void AcceptQuestAction(string? targetGroup = null)
    {
        string title = string.IsNullOrWhiteSpace(targetGroup) ? "Accept Quest (Army)" : $"Accept Quest (Group [{targetGroup}])";
        var vm = new Skua.Core.ViewModels.InputDialogViewModel(title, "Enter quest to accept:", "Quest ID (e.g., 1907)", "Item to accept (optional, e.g., 40816)", false);
        vm.DialogTextInput = _lastAcceptQuestId;
        vm.SecondTextInput = _lastAcceptQuestItem;

        if (CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IDialogService>().ShowDialog(vm) == true)
        {
            string questId = vm.DialogTextInput?.Trim() ?? "";
            string itemToAccept = vm.SecondTextInput?.Trim() ?? "";
            if (!string.IsNullOrWhiteSpace(questId))
            {
                _lastAcceptQuestId = questId;
                _lastAcceptQuestItem = itemToAccept;
                string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_accept_quest.txt");
                System.IO.File.WriteAllLines(tempFile, new[] { questId, itemToAccept });

                foreach (var info in GetTargetTabs(targetGroup))
                    if (info.ChildHwnd != IntPtr.Zero)
                        PostMessage(info.ChildHwnd, WM_SKUA_ACCEPT_QUEST, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }

    private void MenuItem_StartAllScripts_Click(object sender, RoutedEventArgs e)
    {
        var item = sender as MenuItem;
        if (item != null && item.Items.Count > 0) return;
        StartScriptsAction(null);
    }

    private void MenuItem_StopAllScripts_Click(object sender, RoutedEventArgs e)
    {
        var item = sender as MenuItem;
        if (item != null && item.Items.Count > 0) return;
        StopScriptsAction(null);
    }

    private void MenuItem_LoadScriptAll_Click(object sender, RoutedEventArgs e)
    {
        var item = sender as MenuItem;
        if (item != null && item.Items.Count > 0) return;
        LoadScriptAction(null);
    }

    private void MenuItem_LoginAll_Click(object sender, RoutedEventArgs e)
    {
        var item = sender as MenuItem;
        if (item != null && item.Items.Count > 0) return;
        LoginClientsAction(null);
    }

    private void MenuItem_LogoutAll_Click(object sender, RoutedEventArgs e)
    {
        var item = sender as MenuItem;
        if (item != null && item.Items.Count > 0) return;
        LogoutClientsAction(null);
    }

    private void MenuItem_JumpMap_Click(object sender, RoutedEventArgs e)
    {
        var item = sender as MenuItem;
        if (item != null && item.Items.Count > 0) return;
        JumpMapAction(null);
    }

    private void MenuItem_JumpPlayer_Click(object sender, RoutedEventArgs e)
    {
        var item = sender as MenuItem;
        if (item != null && item.Items.Count > 0) return;
        JumpPlayerAction(null);
    }

    private void MenuItem_AcceptQuest_Click(object sender, RoutedEventArgs e)
    {
        var item = sender as MenuItem;
        if (item != null && item.Items.Count > 0) return;
        AcceptQuestAction(null);
    }

    private void BroadcastScriptAction(string scriptPath, int msg)
    {
        try
        {
            string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_script.txt");
            System.IO.File.WriteAllText(tempFile, scriptPath);

            var targets = GetTargetTabs(_scriptLoadTargetGroup).ToList();
            _scriptLoadTargetGroup = null;

            foreach (var info in targets)
            {
                if (info.ChildHwnd != IntPtr.Zero)
                {
                    PostMessage(info.ChildHwnd, (uint)msg, IntPtr.Zero, IntPtr.Zero);
                }
            }
        }
        catch { }
    }

    private void UpdateGridViewBorderColor()
    {
        if (_isGridViewEnabled)
        {
            if (!string.IsNullOrWhiteSpace(_gridViewTargetGroup))
            {
                GridViewText.Text = $"⊞ Grid ({_gridViewTargetGroup})";
                string hex = _tabs.Values.FirstOrDefault(t => string.Equals(t.GroupName, _gridViewTargetGroup, StringComparison.OrdinalIgnoreCase))?.GroupColor ?? GetDefaultColorForGroup(_gridViewTargetGroup);
                try
                {
                    var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                    GridViewBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(50, col.R, col.G, col.B));
                    GridViewBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(col);
                    GridViewText.Foreground = new System.Windows.Media.SolidColorBrush(col);
                }
                catch
                {
                    GridViewBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(62, 62, 66));
                    GridViewBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 204));
                    GridViewText.Foreground = (System.Windows.Media.Brush)FindResource("PrimaryHueMidBrush");
                }
            }
            else
            {
                GridViewText.Text = "⊞ Grid View";
                GridViewBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(62, 62, 66)); // #3E3E42
                GridViewBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 204)); // #007ACC
                GridViewText.Foreground = (System.Windows.Media.Brush)FindResource("PrimaryHueMidBrush");
            }
        }
        else
        {
            GridViewText.Text = "⊞ Grid View";
            GridViewBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 37, 38)); // #252526
            GridViewBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51)); // #333
            GridViewText.Foreground = (System.Windows.Media.Brush)FindResource("PrimaryHueMidBrush"); // Always user color
        }
    }

    private void AddTabItem_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        Dispatcher.BeginInvoke(new Action(() => AddNewInstance("")));
    }

    private void InstancesTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource != InstancesTabControl) return;
        if (IsGroupHeader(InstancesTabControl.SelectedItem as TabItem)) return;

        if (_isGridViewEnabled && InstancesTabControl.SelectedItem != null && InstancesTabControl.SelectedItem != AddTabItem)
        {
            _isGridViewEnabled = false;
            _gridViewTargetGroup = null;
            UpdateGridViewBorderColor();
            foreach (var info in _tabs.Values)
            {
                if (info.ChildHwnd != IntPtr.Zero)
                    PostMessage(info.ChildHwnd, WM_SKUA_GRIDVIEW, IntPtr.Zero, IntPtr.Zero);
            }
        }

        if (InstancesTabControl.SelectedItem is FrameworkElement fe)
            fe.BringIntoView();

        DoReposition();
    }

    private void ScheduleReposition()
    {
        _needsReposition = true;
    }

    /// <summary>
    /// Position the active tab's window over the HostContainer area (screen coords).
    /// Inactive tabs stay at (-32000,-32000) as independent top-level windows.
    /// NO REPARENTING — each child keeps its own message pump so Flash never stalls.
    /// </summary>
    private void DoReposition()
    {
        if (!IsLoaded || _isClosing) return;

        TabItem selectedTab = InstancesTabControl.SelectedItem as TabItem;
        if (selectedTab == AddTabItem || IsGroupHeader(selectedTab)) return;
        if (!_isGridViewEnabled && selectedTab == null) return;

        // When minimized, do NOT hide the child windows (SW_HIDE causes Flash to suspend and disconnect sockets).
        // Instead, move them off-screen so they stay "visible" and their message pumps keep running.
        if (WindowState == WindowState.Minimized)
        {
            foreach (var info in _tabs.Values)
            {
                if (info.ChildHwnd != IntPtr.Zero)
                    SetWindowPos(info.ChildHwnd, HWND_TOP, -32000, -32000, 0, 0, SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_NOSIZE);
            }
            return;
        }

        // Get HostContainer position in screen coordinates
        Point screenTL = HostContainer.PointToScreen(new Point(0, 0));
        Point screenBR = HostContainer.PointToScreen(new Point(HostContainer.ActualWidth, HostContainer.ActualHeight));
        int x = (int)screenTL.X;
        int y = (int)screenTL.Y;
        int w = Math.Max((int)Math.Round(screenBR.X - screenTL.X), 1);
        int h = Math.Max((int)Math.Round(screenBR.Y - screenTL.Y), 1);

        uint baseFlags = SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS;

        var activeWindows = _tabs.Values.Where(v => v.ChildHwnd != IntPtr.Zero).ToList();
        var targetWindows = _isGridViewEnabled ? GetTargetTabs(_gridViewTargetGroup).ToList() : new List<TabInfo>();

        bool isLoading = false;
        if (_isGridViewEnabled)
        {
            isLoading = targetWindows.Count == 0 || targetWindows.Any(v => v.ChildHwnd == IntPtr.Zero);
        }
        else
        {
            if (_tabs.TryGetValue(selectedTab, out TabInfo activeInfo) && activeInfo.ChildHwnd == IntPtr.Zero)
                isLoading = true;
        }
        LoadingIndicator.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;

        if (activeWindows.Count == 0) return;

        if (_isGridViewEnabled)
        {
            var backgroundWindows = _tabs.Values.Where(v => v.ChildHwnd != IntPtr.Zero && !targetWindows.Contains(v)).ToList();

            int n = targetWindows.Count;
            if (n > 0)
            {
                int cols = (int)Math.Ceiling(Math.Sqrt(n));
                int rows = (int)Math.Ceiling((double)n / cols);
                int cellW = w / cols;
                int cellH = h / rows;

                for (int i = 0; i < n; i++)
                {
                    int r = i / cols;
                    int c = i % cols;
                    SetWindowPos(targetWindows[i].ChildHwnd, HWND_TOP, x + c * cellW, y + r * cellH, cellW, cellH, baseFlags);
                    if (targetWindows[i].IsThrottled)
                    {
                        targetWindows[i].IsThrottled = false;
                        PostMessage(targetWindows[i].ChildHwnd, WM_SKUA_THROTTLE, new IntPtr(0), IntPtr.Zero);
                    }
                }
            }

            foreach (var bg in backgroundWindows)
            {
                if (!bg.IsThrottled)
                {
                    bg.IsThrottled = true;
                    PostMessage(bg.ChildHwnd, WM_SKUA_THROTTLE, new IntPtr(1), IntPtr.Zero);
                }
                SetWindowPos(bg.ChildHwnd, HWND_TOP, -32000, -32000, 1, 1, baseFlags);
            }
        }
        else
        {
            IntPtr activeChildHwnd = IntPtr.Zero;
            if (_tabs.TryGetValue(selectedTab, out TabInfo activeInfo) && activeInfo.ChildHwnd != IntPtr.Zero)
            {
                activeChildHwnd = activeInfo.ChildHwnd;
            }

            Skua.Core.AppStartup.HotKeys.ActiveChildHwnd = activeChildHwnd;

            if (activeChildHwnd != IntPtr.Zero)
            {
                SetWindowPos(activeChildHwnd, HWND_TOP, x, y, w, h, baseFlags);
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

                if (activeChildHwnd != IntPtr.Zero)
                {
                    // Stack inactive tabs behind the active one and shrink to 1x1 to save GPU
                    SetWindowPos(info.ChildHwnd, activeChildHwnd, x, y, 1, 1, baseFlags);
                }
                else
                {
                    // Fallback
                    SetWindowPos(info.ChildHwnd, HWND_TOP, -32000, -32000, 1, 1, baseFlags);
                }
            }
        }
    }

    #endregion Tab Switching & Positioning

    #region HWND Detection

    private static IntPtr FindWindowByProcessId(int processId)
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hWnd, lParam) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if ((int)pid == processId && IsWindowVisible(hWnd))
            {
                result = hWnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    #endregion HWND Detection

    #region Tab Management

    private void CloseTab(TabItem tab)
    {
        if (_tabs.TryGetValue(tab, out TabInfo info))
        {
            _tabs.Remove(tab);
            Task.Run(() =>
            {
                try
                {
                    if (info.ChildHwnd != IntPtr.Zero)
                        ShowWindow(info.ChildHwnd, SW_HIDE);
                    if (!info.Process.HasExited)
                        info.Process.Kill();
                    try { File.Delete(Path.Combine(Path.GetTempPath(), "SkuaTabs", $"{info.Process.Id}.txt")); } catch { }
                }
                catch { }
            });
        }
        InstancesTabControl.Items.Remove(tab);
        RefreshGroupHeaders();

        var remaining = InstancesTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t != AddTabItem && !IsGroupHeader(t) && t.Visibility == Visibility.Visible && _tabs.ContainsKey(t));
        if (remaining != null && (InstancesTabControl.SelectedItem == null || InstancesTabControl.SelectedItem == AddTabItem || IsGroupHeader(InstancesTabControl.SelectedItem as TabItem)))
            InstancesTabControl.SelectedItem = remaining;
    }

    private void MaintainPrewarmedInstance()
    {
        if (_isClosing || _prewarmedTabInfo != null)
        {
            _isSpawning = false;
            ProcessNextSpawn();
            return;
        }

        TabInfo info = new TabInfo();
        Process p = new Process();
        p.StartInfo.FileName = Process.GetCurrentProcess().MainModule.FileName;
        p.StartInfo.Arguments = $"--embed 0 --host-pid {Process.GetCurrentProcess().Id}".Trim();
        p.Start();
        info.Process = p;
        _prewarmedTabInfo = info;

        int pid = p.Id;
        Task.Run(async () =>
        {
            IntPtr childHwnd = IntPtr.Zero;
            for (int attempt = 0; attempt < 150 && !p.HasExited; attempt++)
            {
                await Task.Delay(200);
                childHwnd = FindWindowByProcessId(pid);
                if (childHwnd != IntPtr.Zero) break;
            }

            if (childHwnd != IntPtr.Zero && !_isClosing)
            {
                Dispatcher.Invoke(() =>
                {
                    if (_isClosing) return;

                    // Apply properties globally to this HWND
                    int exStyle = GetWindowLong(childHwnd, GWL_EXSTYLE);
                    exStyle |= WS_EX_TOOLWINDOW;
                    SetWindowLong(childHwnd, GWL_EXSTYLE, exStyle);
                    SetWindowLongAny(childHwnd, GWL_HWNDPARENT, _hostHwnd);

                    if (_isGridViewEnabled)
                    {
                        PostMessage(childHwnd, WM_SKUA_GRIDVIEW, new IntPtr(1), IntPtr.Zero);
                    }

                    if (_tabs.Values.Contains(info))
                    {
                        info.ChildHwnd = childHwnd;
                        DoReposition();
                    }
                    else if (_prewarmedTabInfo == info)
                    {
                        info.ChildHwnd = childHwnd;
                        SetWindowPos(childHwnd, HWND_TOP, -32000, -32000, 800, 600, SWP_SHOWWINDOW | SWP_NOACTIVATE);
                    }

                    _isSpawning = false;
                    ProcessNextSpawn();
                });
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    _isSpawning = false;
                    ProcessNextSpawn();
                });
            }
        });
    }

    private void AddNewInstance(string extraArgs)
    {
        if (string.IsNullOrWhiteSpace(extraArgs))
            extraArgs = "";

        int availableId = 1;
        var currentTitles = InstancesTabControl.Items.OfType<TabItem>()
            .Select(t => t.Header as StackPanel)
            .Where(p => p != null)
            .Select(p => p.Children.OfType<TextBlock>().FirstOrDefault(tb => tb.Text != "✕")?.Text)
            .Where(t => t != null && t.StartsWith("Skua "))
            .Select(t => { int.TryParse(t.Substring(5), out int id); return id; })
            .ToList();

        while (currentTitles.Contains(availableId))
        {
            availableId++;
        }

        string tabName = "Skua " + availableId;
        var userMatch = Regex.Match(extraArgs, @"(?:--user|-u)\s+(?:""([^""]+)""|([^\s]+))");
        if (userMatch.Success) tabName = userMatch.Groups[1].Success ? userMatch.Groups[1].Value : userMatch.Groups[2].Value;

        var groupMatch = Regex.Match(extraArgs, @"(?:--group|-g)\s+(?:""([^""]+)""|([^\s]+))");
        string initialGroup = groupMatch.Success ? (groupMatch.Groups[1].Success ? groupMatch.Groups[1].Value : groupMatch.Groups[2].Value) : "";

        #region Tab Header UI

        StackPanel headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        
        System.Windows.Shapes.Ellipse groupDot = new System.Windows.Shapes.Ellipse
        {
            Tag = "GroupDot",
            Width = 7,
            Height = 7,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };

        TextBox editTitle = new TextBox
        {
            Text = tabName,
            Visibility = Visibility.Collapsed,
            MinWidth = 60,
            MaxWidth = 180,
            Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1E1E1E")),
            Foreground = (System.Windows.Media.Brush)FindResource("PrimaryHueMidBrush"),
            CaretBrush = (System.Windows.Media.Brush)FindResource("PrimaryHueMidBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 10, 0),
            BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3E3E42")),
            BorderThickness = new Thickness(1),
            Style = null
        };

        TextBlock title = new TextBlock
        {
            Text = tabName,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
            Foreground = (System.Windows.Media.Brush)FindResource("PrimaryHueMidBrush")
        };
        Border closeBtn = new Border
        {
            Background = System.Windows.Media.Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.Hand,
            Padding = new Thickness(6, 2, 6, 2),
            CornerRadius = new CornerRadius(3)
        };
        TextBlock closeTxt = new TextBlock
        {
            Text = "✕",
            Foreground = System.Windows.Media.Brushes.Gray,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        closeBtn.Child = closeTxt;
        headerPanel.Children.Add(groupDot);
        headerPanel.Children.Add(editTitle);
        headerPanel.Children.Add(title);
        headerPanel.Children.Add(closeBtn);

        closeBtn.MouseEnter += (s, ev) => { closeTxt.Foreground = System.Windows.Media.Brushes.White; closeBtn.Background = System.Windows.Media.Brushes.Red; };
        closeBtn.MouseLeave += (s, ev) => { closeTxt.Foreground = System.Windows.Media.Brushes.Gray; closeBtn.Background = System.Windows.Media.Brushes.Transparent; };

        Action closeEditMode = () =>
        {
            if (editTitle.Visibility != Visibility.Visible) return;
            title.Text = string.IsNullOrWhiteSpace(editTitle.Text) ? title.Text : editTitle.Text;
            title.Visibility = Visibility.Visible;
            editTitle.Visibility = Visibility.Collapsed;
        };

        title.MouseLeftButtonDown += (s, ev) =>
        {
            if (ev.ClickCount == 2)
            {
                editTitle.Text = title.Text;
                title.Visibility = Visibility.Collapsed;
                editTitle.Visibility = Visibility.Visible;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    editTitle.Focus();
                    System.Windows.Input.Keyboard.Focus(editTitle);
                    editTitle.SelectAll();
                }), System.Windows.Threading.DispatcherPriority.Input);

                ev.Handled = true;
            }
        };

        editTitle.LostFocus += (s, ev) => closeEditMode();
        editTitle.LostKeyboardFocus += (s, ev) => closeEditMode();

        editTitle.KeyDown += (s, ev) =>
        {
            if (ev.Key == System.Windows.Input.Key.Enter)
            {
                closeEditMode();
                ev.Handled = true;
            }
            else if (ev.Key == System.Windows.Input.Key.Escape)
            {
                editTitle.Text = title.Text; // revert
                closeEditMode();
                ev.Handled = true;
            }
        };

        #endregion Tab Header UI

        TabItem newTab = new TabItem { Header = headerPanel, AllowDrop = true };

        TabInfo info;
        if (string.IsNullOrWhiteSpace(extraArgs) && _prewarmedTabInfo != null)
        {
            info = _prewarmedTabInfo;
            _prewarmedTabInfo = null;
            _tabs[newTab] = info;

            if (!string.IsNullOrWhiteSpace(initialGroup))
            {
                SetTabGroup(newTab, initialGroup);
            }

            if (info.ChildHwnd != IntPtr.Zero)
            {
                DoReposition();
            }

            EnqueueSpawn(() => MaintainPrewarmedInstance());
        }
        else
        {
            info = new TabInfo();
            _tabs[newTab] = info;

            if (!string.IsNullOrWhiteSpace(initialGroup))
            {
                SetTabGroup(newTab, initialGroup);
            }

            EnqueueSpawn(() =>
            {
                if (_isClosing) return;

                Process p = new Process();
                p.StartInfo.FileName = Process.GetCurrentProcess().MainModule.FileName;
                p.StartInfo.Arguments = $"--embed 0 --host-pid {Process.GetCurrentProcess().Id} {extraArgs}".Trim();
                p.Start();
                info.Process = p;

                int pid = p.Id;
                Task.Run(async () =>
                {
                    IntPtr childHwnd = IntPtr.Zero;
                    for (int attempt = 0; attempt < 150 && !p.HasExited; attempt++)
                    {
                        await Task.Delay(200);
                        childHwnd = FindWindowByProcessId(pid);
                        if (childHwnd != IntPtr.Zero) break;
                    }

                    if (childHwnd != IntPtr.Zero && !_isClosing)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (_isClosing) return;
                            info.ChildHwnd = childHwnd;

                            int exStyle = GetWindowLong(childHwnd, GWL_EXSTYLE);
                            exStyle |= WS_EX_TOOLWINDOW;
                            SetWindowLong(childHwnd, GWL_EXSTYLE, exStyle);
                            SetWindowLongAny(childHwnd, GWL_HWNDPARENT, _hostHwnd);

                            if (_isGridViewEnabled)
                            {
                                bool isTarget = string.IsNullOrWhiteSpace(_gridViewTargetGroup) || string.Equals(info.GroupName, _gridViewTargetGroup, StringComparison.OrdinalIgnoreCase);
                                if (isTarget)
                                {
                                    PostMessage(childHwnd, WM_SKUA_GRIDVIEW, new IntPtr(1), IntPtr.Zero);
                                }
                            }

                            DoReposition();

                            _isSpawning = false;
                            ProcessNextSpawn();
                        });
                    }
                    else
                    {
                        Dispatcher.Invoke(() =>
                        {
                            _isSpawning = false;
                            ProcessNextSpawn();
                        });
                    }
                });
            });
        }

        #region Tab Interactions

        closeBtn.PreviewMouseLeftButtonDown += (s, ev) =>
        {
            CloseTab(newTab);
            ev.Handled = true;
        };

        headerPanel.MouseUp += (s, ev) =>
        {
            if (ev.ChangedButton == System.Windows.Input.MouseButton.Middle)
                CloseTab(newTab);
        };

        ContextMenu ctx = new ContextMenu();
        ctx.Opened += (s, ev) => PopulateTabContextMenu(newTab, ctx);
        PopulateTabContextMenu(newTab, ctx);
        headerPanel.ContextMenu = ctx;

        Point startPoint = new Point();
        headerPanel.PreviewMouseLeftButtonDown += (s, ev) =>
        {
            startPoint = ev.GetPosition(null);
            InstancesTabControl.SelectedItem = newTab;

            if (_isGridViewEnabled)
            {
                _isGridViewEnabled = false;
                _gridViewTargetGroup = null;
                UpdateGridViewBorderColor();
                foreach (var i in _tabs.Values)
                {
                    if (i.ChildHwnd != IntPtr.Zero)
                    {
                        PostMessage(i.ChildHwnd, WM_SKUA_GRIDVIEW, IntPtr.Zero, IntPtr.Zero);
                    }
                }
                DoReposition();
            }
        };
        headerPanel.MouseLeftButtonDown += (s, ev) =>
        {
            // Prevent TabItem from receiving the click and stealing focus from our TextBox
            ev.Handled = true;
        };
        headerPanel.PreviewMouseMove += (s, ev) =>
        {
            if (editTitle.Visibility == Visibility.Visible) return;

            if (ev.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                Point mousePos = ev.GetPosition(null);
                Vector diff = startPoint - mousePos;
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    DragDrop.DoDragDrop(newTab, newTab, DragDropEffects.Move);
                }
            }
        };
        newTab.Drop += (s, ev) =>
        {
            if (ev.Data.GetDataPresent(typeof(TabItem)))
            {
                TabItem droppedTab = (TabItem)ev.Data.GetData(typeof(TabItem));
                if (droppedTab != null && droppedTab != newTab)
                {
                    int targetIndex = InstancesTabControl.Items.IndexOf(newTab);
                    if (IsGroupHeader(droppedTab))
                    {
                        string? droppedGroup = GetGroupHeaderName(droppedTab);
                        if (!string.IsNullOrWhiteSpace(droppedGroup))
                        {
                            MoveWholeGroup(droppedGroup, targetIndex);
                        }
                    }
                    else if (_tabs.ContainsKey(droppedTab))
                    {
                        InstancesTabControl.Items.Remove(droppedTab);
                        int newIdx = Math.Min(targetIndex, InstancesTabControl.Items.Count - 1);
                        if (newIdx < 0) newIdx = 0;
                        InstancesTabControl.Items.Insert(newIdx, droppedTab);
                        InstancesTabControl.SelectedItem = droppedTab;
                        RefreshGroupHeaders();
                    }
                }
            }
        };

        #endregion Tab Interactions

        int insertIdx = InstancesTabControl.Items.Count - 1;
        InstancesTabControl.Items.Insert(insertIdx, newTab);
        RefreshGroupHeaders();
        Dispatcher.BeginInvoke(new Action(() => InstancesTabControl.SelectedItem = newTab));
    }

    #endregion Tab Management
}