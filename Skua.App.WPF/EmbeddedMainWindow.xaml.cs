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
            else if (msg == Skua.Core.AppStartup.HotKeys.WM_SKUA_HOTKEY)
            {
                int actionId = wParam.ToInt32();
                Skua.Core.AppStartup.HotKeys.ExecuteHotkeyAction(actionId);
                handled = true;
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
            else if (msg == WM_SKUA_SET_OPTION)
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
                    case 99:
                        var sm = Ioc.Default.GetRequiredService<ScriptLoaderViewModel>();
                        if (value && !sm.ScriptManager.ScriptRunning && sm.ToggleScriptCommand.CanExecute(null))
                            sm.ToggleScriptCommand.Execute(null);
                        else if (!value && sm.ScriptManager.ScriptRunning && sm.ToggleScriptCommand.CanExecute(null))
                            sm.ToggleScriptCommand.Execute(null);
                        break;
<<<<<<< HEAD
                }
                handled = true;
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
=======
>>>>>>> b3f7e6d407dd02159b8b36748edbf7a7b9467799
                }
                handled = true;
            }
            return IntPtr.Zero;
        }
    }
}
