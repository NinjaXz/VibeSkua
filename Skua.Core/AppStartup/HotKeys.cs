using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Skua.Core.AppStartup;

public class HotKeys
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    public static bool IsHostProcess { get; set; } = false;
    public static int ParentHostProcessId { get; set; } = 0;
    public static IntPtr ActiveChildHwnd { get; set; } = IntPtr.Zero;
    public static IntPtr HostHwnd { get; set; } = IntPtr.Zero;
    public static Func<uint, IntPtr>? GetChildHwndForPid { get; set; }

    private static readonly List<string> _actionList = new()
    {
        "ToggleScript",
        "LoadScript",
        "OpenBank",
        "OpenConsole",
        "SearchScripts",
        "ToggleAutoAttack",
        "ToggleAutoHunt",
        "ToggleLagKiller",
        "ToggleHeadlessMode",
        "ToggleStreamerMode",
        "ToggleHidePlayers",
        "ToggleDisableFX",
        "ToggleMagnetise",
        "ToggleInfiniteRange",
        "Logout",
        "StopScript",
        "JumpToHome",
        "ArmyStartScripts",
        "ArmyStopScripts",
        "ArmyLoginAll",
        "ArmyLogoutAll",
        "ArmyLoadScriptAll",
        "ArmyToggleLagKiller",
        "ArmyToggleHeadlessMode",
        "ArmyToggleHidePlayers",
        "ArmyToggleDisableFX",
        "ArmyToggleInfiniteRange",
        "ArmyToggleUseFunctionBasedSkills",
        "ArmyToggleStreamerMode",
        "ToggleRejectAllDrops",
        "ToggleAggroAllMonsters",
        "ToggleAggroMonsters",
        "ToggleAttackWithoutTarget",
        "ToggleDisableDeathAds",
        "ToggleRestPackets",
        "ToggleShowFPS",
        "ToggleSkipCutscenes",
        "ToggleCustomDropsUI",
        "ToggleQuestLogTurnIns",
        "ToggleBattlePets",
        "ToggleStaticPlayerArt",
        "ToggleChatFilter",
        "ToggleChatUI",
        "ToggleAurasUI",
        "ToggleDraggableDrops",
        "ToggleDisableDamageNumbers",
        "ToggleDisableSoundFx",
        "ToggleDisableQuestPopup",
        "ToggleDisableQuestTracker",
        "ToggleQuestPinner",
        "ToggleQuestProgressNotifications",
        "ToggleVisualSkillCooldowns",
        "ToggleHideGroundItems",
        "ToggleHideHealingBubbles",
        "ToggleDisableAuraAnimations",
        "ToggleHidePlayerNames",
        "ToggleDisableDamageStrobe",
        "ToggleDisableMonsterAnimation",
        "ToggleDisableRedWarning",
        "ToggleDisableSelfAnimation",
        "ToggleDisableSkillAnimation",
        "ToggleDisableWeaponAnimation",
        "ToggleFreezeMonsterPosition",
        "ToggleHideUI",
        "ToggleInvisibleMonsters",
        "ToggleShowNameTags",
        "ToggleShowShadows",
        "ToggleHideGuildNamesOnly",
        "ToggleHideYourNameOnly",
        "ToggleShowYourGroundItemOnly",
        "ToggleShowYourAuraAnimationOnly",
        "ToggleShowMonsterType",
        "ToggleUntargetDead",
        "ToggleUntargetSelf",
        "PlayerRest",
        "RejectAllDrops",
        "TogglePauseScript"
    };

    public static int GetActionId(string actionName) => _actionList.IndexOf(actionName);
    public static string? GetActionNameById(int id) => id >= 0 && id < _actionList.Count ? _actionList[id] : null;

    private static Dictionary<string, IRelayCommand> _hotkeyRegistry = new();

    public static void RegisterHotkey(string name, IRelayCommand command)
    {
        _hotkeyRegistry[name] = command;
    }

    public static Dictionary<string, IRelayCommand> GetRegistry() => _hotkeyRegistry;

    public static string GetFormattedTitle(string binding)
    {
        if (binding == "ToggleScript") return "Start Script";
        if (binding == "ArmyLoadScriptAll") return "Load Script to All";
        string decamelized = Regex.Replace(binding, "([a-z])([A-Z])", "$1 $2");
        if (decamelized.StartsWith("Toggle ")) return decamelized.Substring(7);
        if (decamelized.StartsWith("Toggle")) return decamelized.Substring(6);
        return decamelized;
    }

    public static string GetDescription(string binding)
    {
        return binding switch
        {
            // IScriptOption
            "ToggleRejectAllDrops" => "When enabled, will reject all items that drop.",
            "ToggleAggroAllMonsters" => "Provokes all monsters in the MAP, causing them to attack you simultaneously.",
            "ToggleAggroMonsters" => "Provokes all monsters in the current room, causing them to attack you simultaneously.",
            "ToggleAttackWithoutTarget" => "Setting this to true will make the bot use skills even without a target. Use with caution.",
            "ToggleDisableDeathAds" => "Disables the death advertisement.",
            "ToggleDisableFX" => "Disables all player combat animations to improve frame-rate.",
            "ToggleHidePlayers" => "When enabled, all player avatars are hidden to reduce lag.",
            "ToggleInfiniteRange" => "Allows you to attack targets from any range without moving.",
            "ToggleLagKiller" => "Disables drawing the world to reduce lag and CPU usage.",
            "ToggleHeadlessMode" => "Suspends DWM repaints and hides the game renderer completely to drastically drop CPU/GPU usage.",
            "ToggleMagnetise" => "When enabled, this will cause all targeted monsters to teleport directly to you.",
            "ToggleRestPackets" => "A rest packet will be sent every second, causing the player to heal when not in combat.",
            "ToggleShowFPS" => "Toggles the visibility of the in-game FPS counter.",
            "ToggleSkipCutscenes" => "Determines whether cutscenes should be automatically skipped.",
            "ToggleStreamerMode" => "Anonymizes the player's name, guild, and hides the room number to protect the user's identity.",
            
            // IScriptLite
            "ToggleCustomDropsUI" => "Toggles the custom drops user interface.",
            "ToggleQuestLogTurnIns" => "Allows turning in quests directly from the quest log.",
            "ToggleBattlePets" => "Toggles the visibility and use of battle pets.",
            "ToggleStaticPlayerArt" => "Toggles static player art to improve performance.",
            "ToggleChatFilter" => "Toggles the chat filtering system.",
            "ToggleChatUI" => "Toggles the visibility of the chat UI.",
            "ToggleAurasUI" => "Toggles the class actives and auras UI.",
            "ToggleDraggableDrops" => "Allows you to drag the drops UI around the screen.",
            "ToggleDisableDamageNumbers" => "Disables the floating damage numbers during combat.",
            "ToggleDisableSoundFx" => "Disables all sound effects in the game.",
            "ToggleDisableQuestPopup" => "Disables the automatic popup for quest completions.",
            "ToggleDisableQuestTracker" => "Disables the on-screen quest tracker.",
            "ToggleQuestPinner" => "Toggles the quest pinner feature.",
            "ToggleQuestProgressNotifications" => "Toggles notifications for quest progress.",
            "ToggleVisualSkillCooldowns" => "Toggles the visual cooldown indicators on skills.",
            "ToggleHideGroundItems" => "Hides items that drop on the ground.",
            "ToggleHideHealingBubbles" => "Hides the green healing bubbles.",
            "ToggleDisableAuraAnimations" => "Disables the visual animations for auras.",
            "ToggleHidePlayerNames" => "Hides the names of other players.",
            "ToggleDisableDamageStrobe" => "Disables the damage strobe visual effect.",
            "ToggleDisableMonsterAnimation" => "Disables all animations for monsters.",
            "ToggleDisableRedWarning" => "Disables the red screen warning indicator.",
            "ToggleDisableSelfAnimation" => "Disables all self-animations.",
            "ToggleDisableSkillAnimation" => "Prevents skill animations from playing to improve performance.",
            "ToggleDisableWeaponAnimation" => "Prevents weapon animations from playing.",
            "ToggleFreezeMonsterPosition" => "Freezes monster positions during gameplay.",
            "ToggleHideUI" => "Completely hides the user interface.",
            "ToggleInvisibleMonsters" => "Makes all monsters invisible to improve performance.",
            "ToggleShowNameTags" => "Toggles the visibility of name tags.",
            "ToggleShowShadows" => "Toggles the visibility of shadows.",
            "ToggleHideGuildNamesOnly" => "Hides only the guild names of players.",
            "ToggleHideYourNameOnly" => "Hides only your own character's name.",
            "ToggleShowYourGroundItemOnly" => "Shows only the ground items that belong to you.",
            "ToggleShowYourAuraAnimationOnly" => "Shows only your own aura animations.",
            "ToggleShowMonsterType" => "Displays the type of each monster.",
            "ToggleUntargetDead" => "Automatically untargets dead entities.",
            "ToggleUntargetSelf" => "Prevents the player from targeting themselves.",
            
            // Utilities
            "Logout" => "Safely logs your character out to the login screen.",
            "StopScript" => "Stops the currently running bot script.",
            "TogglePauseScript" => "Pauses the running bot script, or resumes it if it is paused.",
            "PlayerRest" => "Forces your character to sit and rest.",
            "RejectAllDrops" => "Instantly rejects all items in the drop UI.",
            "LoadScript" => "Opens the file browser to load a new script.",
            "OpenBank" => "Opens your bank interface.",
            "SearchScripts" => "Opens the Script Repo to search for scripts.",
            "JumpToHome" => "Jumps your character to their home.",
            
            // Army Control
            "ArmyStartScripts" => "Army Control: Starts the currently loaded script on all active army clients.",
            "ArmyStopScripts" => "Army Control: Stops the script on all active army clients.",
            "ArmyLoginAll" => "Army Control: Initiates login on all active army clients.",
            "ArmyLogoutAll" => "Army Control: Safely logs out all active army clients.",
            "ArmyToggleLagKiller" => "Army Control: Toggles Lag Killer on all active army clients.",
            "ArmyToggleHeadlessMode" => "Army Control: Toggles Headless Mode on all active army clients.",
            "ArmyToggleHidePlayers" => "Army Control: Toggles Hide Players on all active army clients.",
            "ArmyToggleDisableFX" => "Army Control: Toggles Disable FX on all active army clients.",
            "ArmyToggleInfiniteRange" => "Army Control: Toggles Infinite Range on all active army clients.",
            "ArmyToggleUseFunctionBasedSkills" => "Army Control: Toggles Use Function-Based Skills on all active army clients.",
            "ArmyToggleStreamerMode" => "Army Control: Toggles Streamer Mode on all active army clients.",
            "ArmyLoadScriptAll" => "Army Control: Opens the Script Repo to select and load a script to all active army clients.",
            
            // Fallback
            _ => $"Executes the {GetFormattedTitle(binding)} action."
        };
    }

    public const int WM_SKUA_OPEN_BANK = 0x0400 + 460;
    public const int WM_SKUA_RELOAD_HOTKEYS = 0x0400 + 461;
    public const int WM_SKUA_EXECUTE_HOTKEY = 0x0400 + 462;
    public const int WM_SKUA_LOAD_SCRIPT_ALL = 0x0400 + 463;

    private static readonly HashSet<uint> _skuaPids = new() { (uint)Environment.ProcessId };

    public static void RegisterSkuaPid(uint pid)
    {
        if (pid == 0) return;
        lock (_skuaPids)
        {
            _skuaPids.Add(pid);
        }
    }

    public static bool IsSkuaProcess(uint pid)
    {
        if (pid == 0)
            return false;
        if (pid == (uint)Environment.ProcessId)
            return true;
        lock (_skuaPids)
        {
            if (_skuaPids.Contains(pid))
                return true;
        }
        try
        {
            using var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            if (string.Equals(proc.ProcessName, System.Diagnostics.Process.GetCurrentProcess().ProcessName, StringComparison.OrdinalIgnoreCase))
            {
                lock (_skuaPids)
                {
                    _skuaPids.Add(pid);
                }
                return true;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    public static void BroadcastReloadHotkeys()
    {
        try
        {
            if (HostHwnd != IntPtr.Zero)
            {
                PostMessage(HostHwnd, WM_SKUA_RELOAD_HOTKEYS, IntPtr.Zero, IntPtr.Zero);
            }

            HashSet<uint> pids;
            lock (_skuaPids)
            {
                pids = new HashSet<uint>(_skuaPids);
            }

            try
            {
                var skuaProcesses = System.Diagnostics.Process.GetProcessesByName(System.Diagnostics.Process.GetCurrentProcess().ProcessName);
                foreach (var p in skuaProcesses)
                    pids.Add((uint)p.Id);
            }
            catch { }

            EnumWindows((hWnd, lp) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pids.Contains(pid))
                {
                    PostMessage(hWnd, WM_SKUA_RELOAD_HOTKEYS, IntPtr.Zero, IntPtr.Zero);
                }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
    }

    public static void ExecuteHotkeyAction(string actionName)
    {
        if (_hotkeyRegistry.TryGetValue(actionName, out var command))
        {
            if (command.CanExecute(null))
                command.Execute(null);
        }
    }

    public static void ExecuteHotkeyActionDirect(string actionName)
    {
        if (_hotkeyRegistry.Count == 0)
        {
            try
            {
                Ioc.Default.GetRequiredService<IHotKeyService>();
            }
            catch { }
        }

        if (_hotkeyRegistry.TryGetValue(actionName, out var command))
        {
            command.Execute(null);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct COPYDATASTRUCT
    {
        public IntPtr dwData;
        public int cbData;
        public IntPtr lpData;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, ref COPYDATASTRUCT lParam);

    private static void ExecuteOrForward(string actionName, Action localExecute)
    {
        if (IsHostProcess)
        {
            IntPtr childHwnd = IntPtr.Zero;
            IntPtr fg = GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                GetWindowThreadProcessId(fg, out uint fgPid);
                if (fgPid != (uint)Environment.ProcessId && GetChildHwndForPid != null)
                {
                    childHwnd = GetChildHwndForPid(fgPid);
                }
            }

            if (childHwnd == IntPtr.Zero)
                childHwnd = ActiveChildHwnd;

            if (childHwnd != IntPtr.Zero)
            {
                if (actionName == "OpenBank")
                {
                    PostMessage(childHwnd, WM_SKUA_OPEN_BANK, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                int actionId = GetActionId(actionName);
                if (actionId >= 0)
                {
                    PostMessage(childHwnd, WM_SKUA_EXECUTE_HOTKEY, new IntPtr(actionId), IntPtr.Zero);
                    return;
                }

                COPYDATASTRUCT cds = new COPYDATASTRUCT();
                cds.dwData = (IntPtr)0x484B;
                cds.cbData = (actionName.Length + 1) * 2;
                cds.lpData = Marshal.StringToHGlobalUni(actionName);
                SendMessage(childHwnd, 0x004A, IntPtr.Zero, ref cds);
                Marshal.FreeHGlobal(cds.lpData);
                return;
            }
        }

        localExecute();
    }

    public static Dictionary<string, IRelayCommand> CreateHotKeys(IServiceProvider s)
    {
        _hotkeyRegistry["ToggleScript"] = new RelayCommand(() => ExecuteOrForward("ToggleScript", ToggleScriptLocal));
        _hotkeyRegistry["LoadScript"] = new RelayCommand(() => ExecuteOrForward("LoadScript", LoadScriptLocal));
        _hotkeyRegistry["OpenBank"] = new RelayCommand(() => ExecuteOrForward("OpenBank", () => Ioc.Default.GetRequiredService<IScriptBank>().Open()));
        _hotkeyRegistry["OpenConsole"] = new RelayCommand(() => ExecuteOrForward("OpenConsole", OpenConsoleLocal));
        _hotkeyRegistry["SearchScripts"] = new RelayCommand(() => ExecuteOrForward("SearchScripts", SearchScriptsLocal));
        _hotkeyRegistry["ToggleAutoAttack"] = new RelayCommand(() => ExecuteOrForward("ToggleAutoAttack", ToggleAutoAttackLocal));
        _hotkeyRegistry["ToggleAutoHunt"] = new RelayCommand(() => ExecuteOrForward("ToggleAutoHunt", ToggleAutoHuntLocal));
        _hotkeyRegistry["ToggleLagKiller"] = new RelayCommand(() => ExecuteOrForward("ToggleLagKiller", ToggleLagKillerLocal));

        // Custom Vast Actions
        _hotkeyRegistry["ToggleHeadlessMode"] = new RelayCommand(() => ExecuteOrForward("ToggleHeadlessMode", () => Ioc.Default.GetRequiredService<IScriptOption>().HeadlessMode ^= true));
        _hotkeyRegistry["ToggleStreamerMode"] = new RelayCommand(() => ExecuteOrForward("ToggleStreamerMode", () => Ioc.Default.GetRequiredService<IScriptOption>().StreamerMode ^= true));
        _hotkeyRegistry["ToggleHidePlayers"] = new RelayCommand(() => ExecuteOrForward("ToggleHidePlayers", () => Ioc.Default.GetRequiredService<IScriptOption>().HidePlayers ^= true));
        _hotkeyRegistry["ToggleDisableFX"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableFX", () => Ioc.Default.GetRequiredService<IScriptOption>().DisableFX ^= true));
        _hotkeyRegistry["ToggleMagnetise"] = new RelayCommand(() => ExecuteOrForward("ToggleMagnetise", () => Ioc.Default.GetRequiredService<IScriptOption>().Magnetise ^= true));
        _hotkeyRegistry["ToggleInfiniteRange"] = new RelayCommand(() => ExecuteOrForward("ToggleInfiniteRange", () => Ioc.Default.GetRequiredService<IScriptOption>().InfiniteRange ^= true));
        _hotkeyRegistry["Logout"] = new RelayCommand(() => ExecuteOrForward("Logout", () => Ioc.Default.GetRequiredService<IScriptServers>().Logout()));
        _hotkeyRegistry["StopScript"] = new RelayCommand(() => ExecuteOrForward("StopScript", () => Ioc.Default.GetRequiredService<IScriptManager>().StopScript()));
        _hotkeyRegistry["TogglePauseScript"] = new RelayCommand(() => ExecuteOrForward("TogglePauseScript", TogglePauseScriptLocal));
        _hotkeyRegistry["JumpToHome"] = new RelayCommand(() => ExecuteOrForward("JumpToHome", () => Ioc.Default.GetRequiredService<IScriptMap>().Join("home", "Enter", "Spawn")));

        // Army Control
        _hotkeyRegistry["ArmyStartScripts"] = new RelayCommand(() => ExecuteOrForward("ArmyStartScripts", () => BroadcastArmyMessage(0x0400 + 450, 99, 1)));
        _hotkeyRegistry["ArmyStopScripts"] = new RelayCommand(() => ExecuteOrForward("ArmyStopScripts", () => BroadcastArmyMessage(0x0400 + 450, 99, 0)));
        _hotkeyRegistry["ArmyLoginAll"] = new RelayCommand(() => ExecuteOrForward("ArmyLoginAll", () => BroadcastArmyMessage(0x0400 + 447, 0, 0)));
        _hotkeyRegistry["ArmyLogoutAll"] = new RelayCommand(() => ExecuteOrForward("ArmyLogoutAll", () => BroadcastArmyMessage(0x0400 + 448, 0, 0)));
        _hotkeyRegistry["ArmyLoadScriptAll"] = new RelayCommand(ArmyLoadScriptAllLocal);
        
        _hotkeyRegistry["ArmyToggleLagKiller"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleLagKiller", () => BroadcastArmyMessage(0x0400 + 450, 1, (Ioc.Default.GetRequiredService<IScriptOption>().LagKiller ^= true) ? 1 : 0)));
        _hotkeyRegistry["ArmyToggleHeadlessMode"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleHeadlessMode", () => BroadcastArmyMessage(0x0400 + 450, 2, (Ioc.Default.GetRequiredService<IScriptOption>().HeadlessMode ^= true) ? 1 : 0)));
        _hotkeyRegistry["ArmyToggleHidePlayers"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleHidePlayers", () => BroadcastArmyMessage(0x0400 + 450, 3, (Ioc.Default.GetRequiredService<IScriptOption>().HidePlayers ^= true) ? 1 : 0)));
        _hotkeyRegistry["ArmyToggleDisableFX"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleDisableFX", () => BroadcastArmyMessage(0x0400 + 450, 4, (Ioc.Default.GetRequiredService<IScriptOption>().DisableFX ^= true) ? 1 : 0)));
        _hotkeyRegistry["ArmyToggleInfiniteRange"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleInfiniteRange", () => BroadcastArmyMessage(0x0400 + 450, 5, (Ioc.Default.GetRequiredService<IScriptOption>().InfiniteRange ^= true) ? 1 : 0)));
        _hotkeyRegistry["ArmyToggleUseFunctionBasedSkills"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleUseFunctionBasedSkills", () => BroadcastArmyMessage(0x0400 + 450, 8, (Ioc.Default.GetRequiredService<IScriptOption>().UseFunctionBasedSkills ^= true) ? 1 : 0)));
        _hotkeyRegistry["ArmyToggleStreamerMode"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleStreamerMode", () => BroadcastArmyMessage(0x0400 + 450, 9, (Ioc.Default.GetRequiredService<IScriptOption>().StreamerMode ^= true) ? 1 : 0)));

        // IScriptOption Toggles
        _hotkeyRegistry["ToggleRejectAllDrops"] = new RelayCommand(() => ExecuteOrForward("ToggleRejectAllDrops", () => Ioc.Default.GetRequiredService<IScriptOption>().RejectAllDrops ^= true));
        _hotkeyRegistry["ToggleAggroAllMonsters"] = new RelayCommand(() => ExecuteOrForward("ToggleAggroAllMonsters", () => Ioc.Default.GetRequiredService<IScriptOption>().AggroAllMonsters ^= true));
        _hotkeyRegistry["ToggleAggroMonsters"] = new RelayCommand(() => ExecuteOrForward("ToggleAggroMonsters", () => Ioc.Default.GetRequiredService<IScriptOption>().AggroMonsters ^= true));
        _hotkeyRegistry["ToggleAttackWithoutTarget"] = new RelayCommand(() => ExecuteOrForward("ToggleAttackWithoutTarget", () => Ioc.Default.GetRequiredService<IScriptOption>().AttackWithoutTarget ^= true));
        _hotkeyRegistry["ToggleDisableDeathAds"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableDeathAds", () => Ioc.Default.GetRequiredService<IScriptOption>().DisableDeathAds ^= true));
        _hotkeyRegistry["ToggleRestPackets"] = new RelayCommand(() => ExecuteOrForward("ToggleRestPackets", () => Ioc.Default.GetRequiredService<IScriptOption>().RestPackets ^= true));
        _hotkeyRegistry["ToggleShowFPS"] = new RelayCommand(() => ExecuteOrForward("ToggleShowFPS", () => Ioc.Default.GetRequiredService<IScriptOption>().ShowFPS ^= true));
        _hotkeyRegistry["ToggleSkipCutscenes"] = new RelayCommand(() => ExecuteOrForward("ToggleSkipCutscenes", () => Ioc.Default.GetRequiredService<IScriptOption>().SkipCutscenes ^= true));

        // IScriptLite Toggles
        _hotkeyRegistry["ToggleCustomDropsUI"] = new RelayCommand(() => ExecuteOrForward("ToggleCustomDropsUI", () => Ioc.Default.GetRequiredService<IScriptLite>().CustomDropsUI ^= true));
        _hotkeyRegistry["ToggleQuestLogTurnIns"] = new RelayCommand(() => ExecuteOrForward("ToggleQuestLogTurnIns", () => Ioc.Default.GetRequiredService<IScriptLite>().QuestLogTurnIns ^= true));
        _hotkeyRegistry["ToggleBattlePets"] = new RelayCommand(() => ExecuteOrForward("ToggleBattlePets", () => Ioc.Default.GetRequiredService<IScriptLite>().BattlePets ^= true));
        _hotkeyRegistry["ToggleStaticPlayerArt"] = new RelayCommand(() => ExecuteOrForward("ToggleStaticPlayerArt", () => Ioc.Default.GetRequiredService<IScriptLite>().StaticPlayerArt ^= true));
        _hotkeyRegistry["ToggleChatFilter"] = new RelayCommand(() => ExecuteOrForward("ToggleChatFilter", () => Ioc.Default.GetRequiredService<IScriptLite>().ChatFilter ^= true));
        _hotkeyRegistry["ToggleChatUI"] = new RelayCommand(() => ExecuteOrForward("ToggleChatUI", () => Ioc.Default.GetRequiredService<IScriptLite>().ChatUI ^= true));
        _hotkeyRegistry["ToggleAurasUI"] = new RelayCommand(() => ExecuteOrForward("ToggleAurasUI", () => Ioc.Default.GetRequiredService<IScriptLite>().AurasUI ^= true));
        _hotkeyRegistry["ToggleDraggableDrops"] = new RelayCommand(() => ExecuteOrForward("ToggleDraggableDrops", () => Ioc.Default.GetRequiredService<IScriptLite>().DraggableDrops ^= true));
        _hotkeyRegistry["ToggleDisableDamageNumbers"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableDamageNumbers", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableDamageNumbers ^= true));
        _hotkeyRegistry["ToggleDisableSoundFx"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableSoundFx", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableSoundFx ^= true));
        _hotkeyRegistry["ToggleDisableQuestPopup"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableQuestPopup", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableQuestPopup ^= true));
        _hotkeyRegistry["ToggleDisableQuestTracker"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableQuestTracker", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableQuestTracker ^= true));
        _hotkeyRegistry["ToggleQuestPinner"] = new RelayCommand(() => ExecuteOrForward("ToggleQuestPinner", () => Ioc.Default.GetRequiredService<IScriptLite>().QuestPinner ^= true));
        _hotkeyRegistry["ToggleQuestProgressNotifications"] = new RelayCommand(() => ExecuteOrForward("ToggleQuestProgressNotifications", () => Ioc.Default.GetRequiredService<IScriptLite>().QuestProgressNotifications ^= true));
        _hotkeyRegistry["ToggleVisualSkillCooldowns"] = new RelayCommand(() => ExecuteOrForward("ToggleVisualSkillCooldowns", () => Ioc.Default.GetRequiredService<IScriptLite>().VisualSkillCooldowns ^= true));
        _hotkeyRegistry["ToggleHideGroundItems"] = new RelayCommand(() => ExecuteOrForward("ToggleHideGroundItems", () => Ioc.Default.GetRequiredService<IScriptLite>().HideGroundItems ^= true));
        _hotkeyRegistry["ToggleHideHealingBubbles"] = new RelayCommand(() => ExecuteOrForward("ToggleHideHealingBubbles", () => Ioc.Default.GetRequiredService<IScriptLite>().HideHealingBubbles ^= true));
        _hotkeyRegistry["ToggleDisableAuraAnimations"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableAuraAnimations", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableAuraAnimations ^= true));
        _hotkeyRegistry["ToggleHidePlayerNames"] = new RelayCommand(() => ExecuteOrForward("ToggleHidePlayerNames", () => Ioc.Default.GetRequiredService<IScriptLite>().HidePlayerNames ^= true));
        _hotkeyRegistry["ToggleDisableDamageStrobe"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableDamageStrobe", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableDamageStrobe ^= true));
        _hotkeyRegistry["ToggleDisableMonsterAnimation"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableMonsterAnimation", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableMonsterAnimation ^= true));
        _hotkeyRegistry["ToggleDisableRedWarning"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableRedWarning", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableRedWarning ^= true));
        _hotkeyRegistry["ToggleDisableSelfAnimation"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableSelfAnimation", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableSelfAnimation ^= true));
        _hotkeyRegistry["ToggleDisableSkillAnimation"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableSkillAnimation", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableSkillAnimation ^= true));
        _hotkeyRegistry["ToggleDisableWeaponAnimation"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableWeaponAnimation", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableWeaponAnimation ^= true));
        _hotkeyRegistry["ToggleFreezeMonsterPosition"] = new RelayCommand(() => ExecuteOrForward("ToggleFreezeMonsterPosition", () => Ioc.Default.GetRequiredService<IScriptLite>().FreezeMonsterPosition ^= true));
        _hotkeyRegistry["ToggleHideUI"] = new RelayCommand(() => ExecuteOrForward("ToggleHideUI", () => Ioc.Default.GetRequiredService<IScriptLite>().HideUI ^= true));
        _hotkeyRegistry["ToggleInvisibleMonsters"] = new RelayCommand(() => ExecuteOrForward("ToggleInvisibleMonsters", () => Ioc.Default.GetRequiredService<IScriptLite>().InvisibleMonsters ^= true));
        _hotkeyRegistry["ToggleShowNameTags"] = new RelayCommand(() => ExecuteOrForward("ToggleShowNameTags", () => Ioc.Default.GetRequiredService<IScriptLite>().ShowNameTags ^= true));
        _hotkeyRegistry["ToggleShowShadows"] = new RelayCommand(() => ExecuteOrForward("ToggleShowShadows", () => Ioc.Default.GetRequiredService<IScriptLite>().ShowShadows ^= true));
        _hotkeyRegistry["ToggleHideGuildNamesOnly"] = new RelayCommand(() => ExecuteOrForward("ToggleHideGuildNamesOnly", () => Ioc.Default.GetRequiredService<IScriptLite>().HideGuildNamesOnly ^= true));
        _hotkeyRegistry["ToggleHideYourNameOnly"] = new RelayCommand(() => ExecuteOrForward("ToggleHideYourNameOnly", () => Ioc.Default.GetRequiredService<IScriptLite>().HideYourNameOnly ^= true));
        _hotkeyRegistry["ToggleShowYourGroundItemOnly"] = new RelayCommand(() => ExecuteOrForward("ToggleShowYourGroundItemOnly", () => Ioc.Default.GetRequiredService<IScriptLite>().ShowYourGroundItemOnly ^= true));
        _hotkeyRegistry["ToggleShowYourAuraAnimationOnly"] = new RelayCommand(() => ExecuteOrForward("ToggleShowYourAuraAnimationOnly", () => Ioc.Default.GetRequiredService<IScriptLite>().ShowYourAuraAnimationOnly ^= true));
        _hotkeyRegistry["ToggleShowMonsterType"] = new RelayCommand(() => ExecuteOrForward("ToggleShowMonsterType", () => Ioc.Default.GetRequiredService<IScriptLite>().ShowMonsterType ^= true));
        _hotkeyRegistry["ToggleUntargetDead"] = new RelayCommand(() => ExecuteOrForward("ToggleUntargetDead", () => Ioc.Default.GetRequiredService<IScriptLite>().UntargetDead ^= true));
        _hotkeyRegistry["ToggleUntargetSelf"] = new RelayCommand(() => ExecuteOrForward("ToggleUntargetSelf", () => Ioc.Default.GetRequiredService<IScriptLite>().UntargetSelf ^= true));

        // Utilities
        _hotkeyRegistry["PlayerRest"] = new RelayCommand(() => ExecuteOrForward("PlayerRest", () => Ioc.Default.GetRequiredService<IScriptPlayer>().Rest()));
        _hotkeyRegistry["RejectAllDrops"] = new RelayCommand(() => ExecuteOrForward("RejectAllDrops", () => Ioc.Default.GetRequiredService<IScriptDrop>().RejectAll()));

        return _hotkeyRegistry;
    }

    private static bool CanExecuteHotKey()
    {
        try
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
                return false;

            GetWindowThreadProcessId(foregroundWindow, out uint foregroundProcessId);
            return IsSkuaProcess(foregroundProcessId);
        }
        catch
        {
            return false;
        }
    }

    private static void ToggleAutoHuntLocal()
    {
        if (Ioc.Default.GetRequiredService<IScriptAuto>().IsRunning)
        {
            StrongReferenceMessenger.Default.Send<StopAutoMessage>();
            return;
        }

        StrongReferenceMessenger.Default.Send<StartAutoHuntMessage>();
    }

    private static void ToggleAutoAttackLocal()
    {
        if (Ioc.Default.GetRequiredService<IScriptAuto>().IsRunning)
        {
            StrongReferenceMessenger.Default.Send<StopAutoMessage>();
            return;
        }

        StrongReferenceMessenger.Default.Send<StartAutoAttackMessage>();
    }

    private static void OpenConsoleLocal()
    {
        Ioc.Default.GetRequiredService<IWindowService>().ShowManagedWindow("Console");
    }

    private static void SearchScriptsLocal()
    {
        Ioc.Default.GetRequiredService<IWindowService>().ShowManagedWindow("Script Repo");
    }

    private static void ArmyLoadScriptAllLocal()
    {
        if (IsHostProcess)
        {
            try
            {
                Ioc.Default.GetRequiredService<IWindowService>().ShowManagedWindow("Script Repo");
            }
            catch { }
        }
        else if (HostHwnd != IntPtr.Zero)
        {
            PostMessage(HostHwnd, WM_SKUA_LOAD_SCRIPT_ALL, IntPtr.Zero, IntPtr.Zero);
        }
        else
        {
            try
            {
                Ioc.Default.GetRequiredService<IWindowService>().ShowManagedWindow("Script Repo");
            }
            catch { }
        }
    }

    private static void ToggleScriptLocal()
    {
        StrongReferenceMessenger.Default.Send<ToggleScriptMessage, int>((int)MessageChannels.ScriptStatus);
    }

    private static void TogglePauseScriptLocal()
    {
        // Same as the Script Loader's Pause/Resume button; its status text follows ScriptPaused.
        IScriptManager manager = Ioc.Default.GetRequiredService<IScriptManager>();
        if (manager.ScriptRunning)
            manager.ScriptPaused = !manager.ScriptPaused;
    }

    private static void LoadScriptLocal()
    {
        StrongReferenceMessenger.Default.Send<LoadScriptMessage, int>(new(null), (int)MessageChannels.ScriptStatus);
    }

    private static void ToggleLagKillerLocal()
    {
        IScriptOption options = Ioc.Default.GetRequiredService<IScriptOption>();
        options.LagKiller = !options.LagKiller;
    }

    private static void BroadcastArmyMessage(uint msg, int wParam, int lParam)
    {
        var skuaProcesses = System.Diagnostics.Process.GetProcessesByName(System.Diagnostics.Process.GetCurrentProcess().ProcessName);
        var pids = skuaProcesses.Select(p => p.Id).ToHashSet();
        
        EnumWindows((hWnd, lp) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pids.Contains((int)pid))
            {
                PostMessage(hWnd, msg, new IntPtr(wParam), new IntPtr(lParam));
            }
            return true;
        }, IntPtr.Zero);
    }
}
