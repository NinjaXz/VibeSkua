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

namespace Skua.App.WPF
{
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
        #endregion

        #region Tab State
        private class TabInfo
        {
            public Process Process { get; set; }
            public IntPtr ChildHwnd { get; set; } = IntPtr.Zero;
        }

        private string _initialArgs = "";
        private readonly Dictionary<TabItem, TabInfo> _tabs = new();
        private bool _needsReposition = false;
        private bool _isClosing = false;
        private bool _isGridViewEnabled = false;
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
        #endregion

        public TabbedHostWindow(string initialArgs = "")
        {
            _initialArgs = initialArgs ?? "";
            InitializeComponent();

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

            StartPipeServer();
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
            foreach (var info in _tabs.Values)
            {
                try
                {
                    if (info.ChildHwnd != IntPtr.Zero)
                    {
                        ShowWindow(info.ChildHwnd, SW_HIDE);
                        PostMessage(info.ChildHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }
                    if (!info.Process.HasExited)
                    {
                        info.Process.CloseMainWindow();
                        info.Process.Kill();
                    }
                }
                catch { }
            }
            _tabs.Clear();

            if (_prewarmedTabInfo != null)
            {
                try
                {
                    if (_prewarmedTabInfo.ChildHwnd != IntPtr.Zero)
                    {
                        ShowWindow(_prewarmedTabInfo.ChildHwnd, SW_HIDE);
                        PostMessage(_prewarmedTabInfo.ChildHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }
                    if (!_prewarmedTabInfo.Process.HasExited)
                    {
                        _prewarmedTabInfo.Process.CloseMainWindow();
                        _prewarmedTabInfo.Process.Kill();
                    }
                }
                catch { }
                _prewarmedTabInfo = null;
            }
        }
        #endregion

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
        #endregion

        #region Tab Switching & Positioning
        private const int WM_SKUA_GRIDVIEW = 0x0400 + 444;
        private const int WM_SKUA_START_SCRIPT = 0x0400 + 445;
        private const int WM_SKUA_STOP_SCRIPT = 0x0400 + 446;
        private const int WM_SKUA_LOGIN = 0x0400 + 447;
        private const int WM_SKUA_LOGOUT = 0x0400 + 448;
        private const int WM_SKUA_JUMP_MAP = 0x0400 + 449;
        private const int WM_SKUA_SET_OPTION = 0x0400 + 450;

        private void GridViewBorder_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;
            _isGridViewEnabled = !_isGridViewEnabled;
            UpdateGridViewBorderColor();
            
            foreach (var info in _tabs.Values)
            {
                if (info.ChildHwnd != IntPtr.Zero)
                {
                    PostMessage(info.ChildHwnd, WM_SKUA_GRIDVIEW, new IntPtr(_isGridViewEnabled ? 1 : 0), IntPtr.Zero);
                }
            }

            // Deselect tabs if in Grid View so it doesn't look like a single tab is active
            if (_isGridViewEnabled)
            {
                if (InstancesTabControl.SelectedItem != null && InstancesTabControl.SelectedItem != AddTabItem)
                    _lastSelectedTab = InstancesTabControl.SelectedItem as TabItem;
                InstancesTabControl.SelectedItem = null;
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
            }
            
            if (optionId != -1)
            {
                foreach (var info in _tabs.Values)
                    if (info.ChildHwnd != IntPtr.Zero)
                        PostMessage(info.ChildHwnd, WM_SKUA_SET_OPTION, new IntPtr(optionId), new IntPtr(val ? 1 : 0));
            }
        }

        private void MenuItem_StartAllScripts_Click(object sender, RoutedEventArgs e)
        {
            foreach (var info in _tabs.Values)
                if (info.ChildHwnd != IntPtr.Zero)
                    PostMessage(info.ChildHwnd, WM_SKUA_START_SCRIPT, IntPtr.Zero, IntPtr.Zero);
        }

        private void MenuItem_StopAllScripts_Click(object sender, RoutedEventArgs e)
        {
            foreach (var info in _tabs.Values)
                if (info.ChildHwnd != IntPtr.Zero)
                    PostMessage(info.ChildHwnd, WM_SKUA_STOP_SCRIPT, IntPtr.Zero, IntPtr.Zero);
        }

        private void MenuItem_LoginAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var info in _tabs.Values)
                if (info.ChildHwnd != IntPtr.Zero)
                    PostMessage(info.ChildHwnd, WM_SKUA_LOGIN, IntPtr.Zero, IntPtr.Zero);
        }

        private void MenuItem_LogoutAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var info in _tabs.Values)
                if (info.ChildHwnd != IntPtr.Zero)
                    PostMessage(info.ChildHwnd, WM_SKUA_LOGOUT, IntPtr.Zero, IntPtr.Zero);
        }

        private string _lastJumpMap = "";

        private void MenuItem_JumpMap_Click(object sender, RoutedEventArgs e)
        {
            var vm = new Skua.Core.ViewModels.InputDialogViewModel("Jump Army to Map", "Enter map name and room number:", "e.g., yulgar-829472", false);
            vm.DialogTextInput = _lastJumpMap;
            
            if (CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default.GetRequiredService<Skua.Core.Interfaces.IDialogService>().ShowDialog(vm) == true)
            {
                string targetMap = vm.DialogTextInput;
                if (!string.IsNullOrWhiteSpace(targetMap))
                {
                    _lastJumpMap = targetMap;
                    string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_jump.txt");
                    System.IO.File.WriteAllText(tempFile, targetMap);
                    
                    foreach (var info in _tabs.Values)
                        if (info.ChildHwnd != IntPtr.Zero)
                            PostMessage(info.ChildHwnd, WM_SKUA_JUMP_MAP, IntPtr.Zero, IntPtr.Zero);
                }
            }
        }

        private void UpdateGridViewBorderColor()
        {
            if (_isGridViewEnabled)
            {
                GridViewBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(62, 62, 66)); // #3E3E42
                GridViewBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 204)); // #007ACC
                GridViewText.Foreground = (System.Windows.Media.Brush)FindResource("PrimaryHueMidBrush");
            }
            else
            {
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

            if (_isGridViewEnabled && InstancesTabControl.SelectedItem != null && InstancesTabControl.SelectedItem != AddTabItem)
            {
                _isGridViewEnabled = false;
                UpdateGridViewBorderColor();
                foreach (var info in _tabs.Values)
                {
                    if (info.ChildHwnd != IntPtr.Zero)
                        PostMessage(info.ChildHwnd, WM_SKUA_GRIDVIEW, IntPtr.Zero, IntPtr.Zero);
                }
            }

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
            if (selectedTab == AddTabItem) return;
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
            
            bool isLoading = false;
            if (_isGridViewEnabled)
            {
                isLoading = _tabs.Values.Any(v => v.ChildHwnd == IntPtr.Zero);
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
                int n = activeWindows.Count;
                int cols = (int)Math.Ceiling(Math.Sqrt(n));
                int rows = (int)Math.Ceiling((double)n / cols);
                int cellW = w / cols;
                int cellH = h / rows;

                for (int i = 0; i < n; i++)
                {
                    int r = i / cols;
                    int c = i % cols;
                    SetWindowPos(activeWindows[i].ChildHwnd, HWND_TOP, x + c * cellW, y + r * cellH, cellW, cellH, baseFlags);
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
                }

                foreach (var kvp in _tabs)
                {
                    if (kvp.Key == selectedTab) continue;

                    TabInfo info = kvp.Value;
                    if (info.ChildHwnd == IntPtr.Zero) continue;

                    if (activeChildHwnd != IntPtr.Zero)
                    {
                        // Stack inactive tabs behind the active one so they stay physically on-screen
                        SetWindowPos(info.ChildHwnd, activeChildHwnd, x, y, w, h, baseFlags);
                    }
                    else
                    {
                        // Fallback
                        SetWindowPos(info.ChildHwnd, HWND_TOP, -32000, -32000, w, h, baseFlags);
                    }
                }
            }
        }
        #endregion

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
        #endregion

        #region Tab Management
        private void CloseTab(TabItem tab)
        {
            if (_tabs.TryGetValue(tab, out TabInfo info))
            {
                try
                {
                    if (info.ChildHwnd != IntPtr.Zero)
                    {
                        ShowWindow(info.ChildHwnd, SW_HIDE);
                        PostMessage(info.ChildHwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }
                    if (!info.Process.HasExited)
                    {
                        info.Process.CloseMainWindow();
                        info.Process.Kill();
                    }
                }
                catch { }
                _tabs.Remove(tab);
            }
            InstancesTabControl.Items.Remove(tab);

            var remaining = InstancesTabControl.Items.OfType<TabItem>().FirstOrDefault(t => t != AddTabItem);
            if (remaining != null && (InstancesTabControl.SelectedItem == null || InstancesTabControl.SelectedItem == AddTabItem))
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
            var match = Regex.Match(extraArgs, @"(?:--user|-u)\s+""?([^""\s]+)""?");
            if (match.Success) tabName = match.Groups[1].Value;

            #region Tab Header UI
            StackPanel headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
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
                Style = null // remove default styles that might override appearance
            };
            
            // To add corner radius in code-behind without a control template, we rely on the border thickness and padding 
            // since TextBox doesn't have CornerRadius property directly, the flat dark theme is standard and clean.
            
            TextBlock title = new TextBlock
            {
                Text = tabName, VerticalAlignment = VerticalAlignment.Center,
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
                Text = "✕", Foreground = System.Windows.Media.Brushes.Gray,
                FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center
            };
            closeBtn.Child = closeTxt;
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
                    
                    Dispatcher.BeginInvoke(new Action(() => {
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
            #endregion

            TabItem newTab = new TabItem { Header = headerPanel, AllowDrop = true };
            
            TabInfo info;
            if (string.IsNullOrWhiteSpace(extraArgs) && _prewarmedTabInfo != null)
            {
                info = _prewarmedTabInfo;
                _prewarmedTabInfo = null;
                _tabs[newTab] = info;

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
                                    PostMessage(childHwnd, WM_SKUA_GRIDVIEW, new IntPtr(1), IntPtr.Zero);
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
            headerPanel.ContextMenu = ctx;

            Point startPoint = new Point();
            headerPanel.PreviewMouseLeftButtonDown += (s, ev) => 
            {
                startPoint = ev.GetPosition(null);
                InstancesTabControl.SelectedItem = newTab;
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
                        InstancesTabControl.Items.Remove(droppedTab);
                        InstancesTabControl.Items.Insert(targetIndex, droppedTab);
                        InstancesTabControl.SelectedItem = droppedTab;
                    }
                }
            };
            #endregion

            int insertIdx = InstancesTabControl.Items.Count - 1;
            InstancesTabControl.Items.Insert(insertIdx, newTab);
            Dispatcher.BeginInvoke(new Action(() => InstancesTabControl.SelectedItem = newTab));
        }
        #endregion
    }
}
