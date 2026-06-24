using System;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.ViewModels;
using Skua.Core.Interfaces;

namespace Skua.App.WPF
{
    public partial class EmbeddedMainWindow : Window
    {
        private const int WM_SKUA_GRIDVIEW = 0x0400 + 444;
        private const int WM_SKUA_START_SCRIPT = 0x0400 + 445;
        private const int WM_SKUA_STOP_SCRIPT = 0x0400 + 446;
        private const int WM_SKUA_LOGIN = 0x0400 + 447;
        private const int WM_SKUA_LOGOUT = 0x0400 + 448;
        private const int WM_SKUA_JUMP_MAP = 0x0400 + 449;
        private const int WM_SKUA_SET_OPTION = 0x0400 + 450;
        private const int WM_SKUA_LOAD_SCRIPT = 0x0400 + 453;
        private const int WM_SKUA_ARMY_SCHEDULER = 0x0400 + 454;
        private const int WM_SKUA_ARMY_SCHEDULER_STOP = 0x0400 + 455;
        private const int WM_SKUA_CHECK_LOGIN = 0x0400 + 456;
        private const int WM_COPYDATA = 0x004A;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct COPYDATASTRUCT
        {
            public IntPtr dwData;
            public int cbData;
            public IntPtr lpData;
        }

        public EmbeddedMainWindow()
        {
            InitializeComponent();
            DataContext = Ioc.Default.GetRequiredService<MainViewModel>();
            Loaded += (s, e) =>
            {
                System.Windows.Interop.HwndSource source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
                source?.AddHook(WndProc);
            };
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_SKUA_GRIDVIEW)
            {
                bool isGrid = wParam.ToInt32() == 1;
                MainMenuCtrl.Visibility = isGrid ? Visibility.Collapsed : Visibility.Visible;
                GameContainerCtrl.SetGridView(isGrid);
                handled = true;
            }
            else if (msg == WM_COPYDATA)
            {
                COPYDATASTRUCT cds = (COPYDATASTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(lParam, typeof(COPYDATASTRUCT));
                if (cds.dwData == (IntPtr)0x484B)
                {
                    string commandName = System.Runtime.InteropServices.Marshal.PtrToStringUni(cds.lpData);
                    if (!string.IsNullOrEmpty(commandName))
                        Skua.Core.AppStartup.HotKeys.ExecuteHotkeyAction(commandName);
                    handled = true;
                }
            }

            else if (msg == WM_SKUA_LOGIN)
            {
                Task.Run(() => 
                {
                    var options = Ioc.Default.GetRequiredService<IScriptOption>();
                    var servers = Ioc.Default.GetRequiredService<IScriptServers>();
                    string targetServer = string.IsNullOrWhiteSpace(options.ReloginServer) ? "Twilly" : options.ReloginServer;
                    servers.Relogin(targetServer);
                });
                handled = true;
            }
            else if (msg == WM_SKUA_LOGOUT)
            {
                Ioc.Default.GetRequiredService<IScriptServers>().Logout();
                handled = true;
            }
            else if (msg == WM_SKUA_JUMP_MAP)
            {
                Task.Run(() => 
                {
                    try 
                    {
                        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_jump.txt");
                        if (System.IO.File.Exists(tempFile))
                        {
                            string targetMap = System.IO.File.ReadAllText(tempFile);
                            if (!string.IsNullOrWhiteSpace(targetMap))
                                Ioc.Default.GetRequiredService<IScriptMap>().Join(targetMap, "Enter", "Spawn", true);
                        }
                    } 
                    catch { }
                });
                handled = true;
            }
            else if (msg == WM_SKUA_LOAD_SCRIPT)
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
                                var sm = Ioc.Default.GetRequiredService<ScriptLoaderViewModel>();
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    if (sm.LoadScriptCommand.CanExecute(targetScript))
                                        sm.LoadScriptCommand.Execute(targetScript);
                                });
                            }
                        }
                    } 
                    catch { }
                });
                handled = true;
            }
            else if (msg == WM_SKUA_SET_OPTION)
            {
                int optionId = wParam.ToInt32();
                bool value = lParam.ToInt32() == 1;
                var options = Ioc.Default.GetRequiredService<IScriptOption>();
                options.IsIpcMessageProcessing = true;
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
                    case 99:
                        var sm = Ioc.Default.GetRequiredService<ScriptLoaderViewModel>();
                        var scheduler = Ioc.Default.GetRequiredService<ScriptSchedulerViewModel>();
                        if (value)
                        {
                            if (!sm.ScriptManager.ScriptRunning)
                            {
                                if (sm.ToggleScriptCommand.CanExecute(null))
                                {
                                    sm.ToggleScriptCommand.Execute(null);
                                }
                                else
                                {
                                    var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
                                    string ident = bot.Player.LoggedIn ? bot.Player.Username : "Offline Account";
                                    
                                    if (string.IsNullOrWhiteSpace(sm.ScriptManager.LoadedScript))
                                        Ioc.Default.GetRequiredService<ILogService>().ScriptLog($"[{ident}] Army start ignored: No script loaded.");
                                    else if (!bot.Player.LoggedIn)
                                        Ioc.Default.GetRequiredService<ILogService>().ScriptLog($"[{ident}] Army start ignored: Account is not logged in.");
                                    else
                                        Ioc.Default.GetRequiredService<ILogService>().ScriptLog($"[{ident}] Army start ignored: Toggle command blocked.");
                                }
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
                options.IsIpcMessageProcessing = false;
                handled = true;
            }
            else if (msg == WM_SKUA_ARMY_SCHEDULER)
            {
                Task.Run(() => 
                {
                    try 
                    {
                        string tempFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "skua_global_playlist.json");
                        if (System.IO.File.Exists(tempFile))
                        {
                            var data = System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<Skua.Core.ViewModels.ScriptSchedulerViewModel.SavedScriptItem>>(System.IO.File.ReadAllText(tempFile));
                            var scheduler = Ioc.Default.GetRequiredService<ScriptSchedulerViewModel>();
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                scheduler.ScriptQueue.Clear();
                                if (data != null)
                                {
                                    foreach (var item in data)
                                    {
                                        if (System.IO.File.Exists(item.Path))
                                        {
                                            var vm = new ScriptItemViewModel(item.Path) { Id = item.Id };
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
                    } 
                    catch { }
                });
                handled = true;
            }
            else if (msg == WM_SKUA_ARMY_SCHEDULER_STOP)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var scheduler = Ioc.Default.GetRequiredService<ScriptSchedulerViewModel>();
                    if (scheduler.StopQueueCommand.CanExecute(null))
                        scheduler.StopQueueCommand.Execute(null);
                });
                handled = true;
            }
            else if (msg == WM_SKUA_CHECK_LOGIN)
            {
                var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
                handled = true;
                return new IntPtr(bot.Player.LoggedIn ? 1 : 0);
            }
            else if (msg == 0x0400 + 452) // WM_SKUA_THROTTLE
            {
                bool throttle = wParam.ToInt32() == 1;
                var bot = Ioc.Default.GetRequiredService<IScriptInterface>();
                if (!bot.Options.HeadlessMode)
                {
                    try { bot.Flash?.SetGameObject("stage.frameRate", throttle ? 2 : 24); } catch { }
                }
                if (throttle)
                {
                    Skua.Core.Utils.MemoryUtils.TrimWorkingSet();
                }
                handled = true;
            }
            return IntPtr.Zero;
        }
    }
}
