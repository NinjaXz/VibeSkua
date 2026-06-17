using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
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
    public static IntPtr ActiveChildHwnd { get; set; } = IntPtr.Zero;

    private static Dictionary<string, IRelayCommand> _hotkeyRegistry = new();

    public static void RegisterHotkey(string name, IRelayCommand command)
    {
        _hotkeyRegistry[name] = command;
    }

    public static Dictionary<string, IRelayCommand> GetRegistry() => _hotkeyRegistry;

    public static string GetFormattedTitle(string binding)
    {
        if (binding == "ToggleScript") return "Start Script";
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
            
            // Fallback
            _ => $"Executes the {GetFormattedTitle(binding)} action."
        };
    }

    public static void ExecuteHotkeyAction(string actionName)
    {
        if (_hotkeyRegistry.TryGetValue(actionName, out var command))
        {
            if (command.CanExecute(null))
                command.Execute(null);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COPYDATASTRUCT
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
            if (ActiveChildHwnd != IntPtr.Zero)
            {
                COPYDATASTRUCT cds = new COPYDATASTRUCT();
                cds.dwData = (IntPtr)0x484B;
                cds.cbData = (actionName.Length + 1) * 2;
                cds.lpData = Marshal.StringToHGlobalUni(actionName);
                SendMessage(ActiveChildHwnd, 0x004A, IntPtr.Zero, ref cds);
                Marshal.FreeHGlobal(cds.lpData);
            }
        }
        else
        {
            localExecute();
        }
    }

    internal static Dictionary<string, IRelayCommand> CreateHotKeys(IServiceProvider s)
    {
        _hotkeyRegistry["ToggleScript"] = new RelayCommand(() => ExecuteOrForward("ToggleScript", ToggleScriptLocal), CanExecuteHotKey);
        _hotkeyRegistry["LoadScript"] = new RelayCommand(() => ExecuteOrForward("LoadScript", LoadScriptLocal), CanExecuteHotKey);
        _hotkeyRegistry["OpenBank"] = new RelayCommand(() => ExecuteOrForward("OpenBank", Ioc.Default.GetRequiredService<IScriptBank>().Open), CanExecuteHotKey);
        _hotkeyRegistry["OpenConsole"] = new RelayCommand(() => ExecuteOrForward("OpenConsole", OpenConsoleLocal), CanExecuteHotKey);
        _hotkeyRegistry["SearchScripts"] = new RelayCommand(() => ExecuteOrForward("SearchScripts", SearchScriptsLocal), CanExecuteHotKey);
        _hotkeyRegistry["ToggleAutoAttack"] = new RelayCommand(() => ExecuteOrForward("ToggleAutoAttack", ToggleAutoAttackLocal), CanExecuteHotKey);
        _hotkeyRegistry["ToggleAutoHunt"] = new RelayCommand(() => ExecuteOrForward("ToggleAutoHunt", ToggleAutoHuntLocal), CanExecuteHotKey);
        _hotkeyRegistry["ToggleLagKiller"] = new RelayCommand(() => ExecuteOrForward("ToggleLagKiller", ToggleLagKillerLocal), CanExecuteHotKey);

        // Custom Vast Actions
        _hotkeyRegistry["ToggleHeadlessMode"] = new RelayCommand(() => ExecuteOrForward("ToggleHeadlessMode", () => Ioc.Default.GetRequiredService<IScriptOption>().HeadlessMode ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleStreamerMode"] = new RelayCommand(() => ExecuteOrForward("ToggleStreamerMode", () => Ioc.Default.GetRequiredService<IScriptOption>().StreamerMode ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleHidePlayers"] = new RelayCommand(() => ExecuteOrForward("ToggleHidePlayers", () => Ioc.Default.GetRequiredService<IScriptOption>().HidePlayers ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDisableFX"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableFX", () => Ioc.Default.GetRequiredService<IScriptOption>().DisableFX ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleMagnetise"] = new RelayCommand(() => ExecuteOrForward("ToggleMagnetise", () => Ioc.Default.GetRequiredService<IScriptOption>().Magnetise ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleInfiniteRange"] = new RelayCommand(() => ExecuteOrForward("ToggleInfiniteRange", () => Ioc.Default.GetRequiredService<IScriptOption>().InfiniteRange ^= true), CanExecuteHotKey);
        _hotkeyRegistry["Logout"] = new RelayCommand(() => ExecuteOrForward("Logout", () => Ioc.Default.GetRequiredService<IScriptServers>().Logout()), CanExecuteHotKey);
        _hotkeyRegistry["StopScript"] = new RelayCommand(() => ExecuteOrForward("StopScript", () => Ioc.Default.GetRequiredService<IScriptManager>().StopScript()), CanExecuteHotKey);
        _hotkeyRegistry["JumpToHome"] = new RelayCommand(() => ExecuteOrForward("JumpToHome", () => Ioc.Default.GetRequiredService<IScriptMap>().Join("home", "Enter", "Spawn")), CanExecuteHotKey);

        // Army Control
        _hotkeyRegistry["ArmyStartScripts"] = new RelayCommand(() => ExecuteOrForward("ArmyStartScripts", () => BroadcastArmyMessage(0x0400 + 450, 99, 1)), CanExecuteHotKey);
        _hotkeyRegistry["ArmyStopScripts"] = new RelayCommand(() => ExecuteOrForward("ArmyStopScripts", () => BroadcastArmyMessage(0x0400 + 450, 99, 0)), CanExecuteHotKey);
        _hotkeyRegistry["ArmyLoginAll"] = new RelayCommand(() => ExecuteOrForward("ArmyLoginAll", () => BroadcastArmyMessage(0x0400 + 447, 0, 0)), CanExecuteHotKey);
        _hotkeyRegistry["ArmyLogoutAll"] = new RelayCommand(() => ExecuteOrForward("ArmyLogoutAll", () => BroadcastArmyMessage(0x0400 + 448, 0, 0)), CanExecuteHotKey);
        
        _hotkeyRegistry["ArmyToggleLagKiller"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleLagKiller", () => BroadcastArmyMessage(0x0400 + 450, 1, (Ioc.Default.GetRequiredService<IScriptOption>().LagKiller ^= true) ? 1 : 0)), CanExecuteHotKey);
        _hotkeyRegistry["ArmyToggleHeadlessMode"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleHeadlessMode", () => BroadcastArmyMessage(0x0400 + 450, 2, (Ioc.Default.GetRequiredService<IScriptOption>().HeadlessMode ^= true) ? 1 : 0)), CanExecuteHotKey);
        _hotkeyRegistry["ArmyToggleHidePlayers"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleHidePlayers", () => BroadcastArmyMessage(0x0400 + 450, 3, (Ioc.Default.GetRequiredService<IScriptOption>().HidePlayers ^= true) ? 1 : 0)), CanExecuteHotKey);
        _hotkeyRegistry["ArmyToggleDisableFX"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleDisableFX", () => BroadcastArmyMessage(0x0400 + 450, 4, (Ioc.Default.GetRequiredService<IScriptOption>().DisableFX ^= true) ? 1 : 0)), CanExecuteHotKey);
        _hotkeyRegistry["ArmyToggleInfiniteRange"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleInfiniteRange", () => BroadcastArmyMessage(0x0400 + 450, 5, (Ioc.Default.GetRequiredService<IScriptOption>().InfiniteRange ^= true) ? 1 : 0)), CanExecuteHotKey);
        _hotkeyRegistry["ArmyToggleUseFunctionBasedSkills"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleUseFunctionBasedSkills", () => BroadcastArmyMessage(0x0400 + 450, 8, (Ioc.Default.GetRequiredService<IScriptOption>().UseFunctionBasedSkills ^= true) ? 1 : 0)), CanExecuteHotKey);
        _hotkeyRegistry["ArmyToggleStreamerMode"] = new RelayCommand(() => ExecuteOrForward("ArmyToggleStreamerMode", () => BroadcastArmyMessage(0x0400 + 450, 9, (Ioc.Default.GetRequiredService<IScriptOption>().StreamerMode ^= true) ? 1 : 0)), CanExecuteHotKey);

        // IScriptOption Toggles
        _hotkeyRegistry["ToggleRejectAllDrops"] = new RelayCommand(() => ExecuteOrForward("ToggleRejectAllDrops", () => Ioc.Default.GetRequiredService<IScriptOption>().RejectAllDrops ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleAggroAllMonsters"] = new RelayCommand(() => ExecuteOrForward("ToggleAggroAllMonsters", () => Ioc.Default.GetRequiredService<IScriptOption>().AggroAllMonsters ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleAggroMonsters"] = new RelayCommand(() => ExecuteOrForward("ToggleAggroMonsters", () => Ioc.Default.GetRequiredService<IScriptOption>().AggroMonsters ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleAttackWithoutTarget"] = new RelayCommand(() => ExecuteOrForward("ToggleAttackWithoutTarget", () => Ioc.Default.GetRequiredService<IScriptOption>().AttackWithoutTarget ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDisableDeathAds"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableDeathAds", () => Ioc.Default.GetRequiredService<IScriptOption>().DisableDeathAds ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleRestPackets"] = new RelayCommand(() => ExecuteOrForward("ToggleRestPackets", () => Ioc.Default.GetRequiredService<IScriptOption>().RestPackets ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleShowFPS"] = new RelayCommand(() => ExecuteOrForward("ToggleShowFPS", () => Ioc.Default.GetRequiredService<IScriptOption>().ShowFPS ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleSkipCutscenes"] = new RelayCommand(() => ExecuteOrForward("ToggleSkipCutscenes", () => Ioc.Default.GetRequiredService<IScriptOption>().SkipCutscenes ^= true), CanExecuteHotKey);

        // IScriptLite Toggles
        _hotkeyRegistry["ToggleCustomDropsUI"] = new RelayCommand(() => ExecuteOrForward("ToggleCustomDropsUI", () => Ioc.Default.GetRequiredService<IScriptLite>().CustomDropsUI ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleQuestLogTurnIns"] = new RelayCommand(() => ExecuteOrForward("ToggleQuestLogTurnIns", () => Ioc.Default.GetRequiredService<IScriptLite>().QuestLogTurnIns ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleBattlePets"] = new RelayCommand(() => ExecuteOrForward("ToggleBattlePets", () => Ioc.Default.GetRequiredService<IScriptLite>().BattlePets ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleStaticPlayerArt"] = new RelayCommand(() => ExecuteOrForward("ToggleStaticPlayerArt", () => Ioc.Default.GetRequiredService<IScriptLite>().StaticPlayerArt ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleChatFilter"] = new RelayCommand(() => ExecuteOrForward("ToggleChatFilter", () => Ioc.Default.GetRequiredService<IScriptLite>().ChatFilter ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleChatUI"] = new RelayCommand(() => ExecuteOrForward("ToggleChatUI", () => Ioc.Default.GetRequiredService<IScriptLite>().ChatUI ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleAurasUI"] = new RelayCommand(() => ExecuteOrForward("ToggleAurasUI", () => Ioc.Default.GetRequiredService<IScriptLite>().AurasUI ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDraggableDrops"] = new RelayCommand(() => ExecuteOrForward("ToggleDraggableDrops", () => Ioc.Default.GetRequiredService<IScriptLite>().DraggableDrops ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDisableDamageNumbers"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableDamageNumbers", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableDamageNumbers ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDisableSoundFx"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableSoundFx", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableSoundFx ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDisableQuestPopup"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableQuestPopup", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableQuestPopup ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDisableQuestTracker"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableQuestTracker", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableQuestTracker ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleQuestPinner"] = new RelayCommand(() => ExecuteOrForward("ToggleQuestPinner", () => Ioc.Default.GetRequiredService<IScriptLite>().QuestPinner ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleQuestProgressNotifications"] = new RelayCommand(() => ExecuteOrForward("ToggleQuestProgressNotifications", () => Ioc.Default.GetRequiredService<IScriptLite>().QuestProgressNotifications ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleVisualSkillCooldowns"] = new RelayCommand(() => ExecuteOrForward("ToggleVisualSkillCooldowns", () => Ioc.Default.GetRequiredService<IScriptLite>().VisualSkillCooldowns ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleHideGroundItems"] = new RelayCommand(() => ExecuteOrForward("ToggleHideGroundItems", () => Ioc.Default.GetRequiredService<IScriptLite>().HideGroundItems ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleHideHealingBubbles"] = new RelayCommand(() => ExecuteOrForward("ToggleHideHealingBubbles", () => Ioc.Default.GetRequiredService<IScriptLite>().HideHealingBubbles ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDisableAuraAnimations"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableAuraAnimations", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableAuraAnimations ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleHidePlayerNames"] = new RelayCommand(() => ExecuteOrForward("ToggleHidePlayerNames", () => Ioc.Default.GetRequiredService<IScriptLite>().HidePlayerNames ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDisableDamageStrobe"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableDamageStrobe", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableDamageStrobe ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDisableMonsterAnimation"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableMonsterAnimation", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableMonsterAnimation ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDisableRedWarning"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableRedWarning", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableRedWarning ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDisableSelfAnimation"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableSelfAnimation", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableSelfAnimation ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDisableSkillAnimation"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableSkillAnimation", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableSkillAnimation ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleDisableWeaponAnimation"] = new RelayCommand(() => ExecuteOrForward("ToggleDisableWeaponAnimation", () => Ioc.Default.GetRequiredService<IScriptLite>().DisableWeaponAnimation ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleFreezeMonsterPosition"] = new RelayCommand(() => ExecuteOrForward("ToggleFreezeMonsterPosition", () => Ioc.Default.GetRequiredService<IScriptLite>().FreezeMonsterPosition ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleHideUI"] = new RelayCommand(() => ExecuteOrForward("ToggleHideUI", () => Ioc.Default.GetRequiredService<IScriptLite>().HideUI ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleInvisibleMonsters"] = new RelayCommand(() => ExecuteOrForward("ToggleInvisibleMonsters", () => Ioc.Default.GetRequiredService<IScriptLite>().InvisibleMonsters ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleShowNameTags"] = new RelayCommand(() => ExecuteOrForward("ToggleShowNameTags", () => Ioc.Default.GetRequiredService<IScriptLite>().ShowNameTags ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleShowShadows"] = new RelayCommand(() => ExecuteOrForward("ToggleShowShadows", () => Ioc.Default.GetRequiredService<IScriptLite>().ShowShadows ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleHideGuildNamesOnly"] = new RelayCommand(() => ExecuteOrForward("ToggleHideGuildNamesOnly", () => Ioc.Default.GetRequiredService<IScriptLite>().HideGuildNamesOnly ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleHideYourNameOnly"] = new RelayCommand(() => ExecuteOrForward("ToggleHideYourNameOnly", () => Ioc.Default.GetRequiredService<IScriptLite>().HideYourNameOnly ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleShowYourGroundItemOnly"] = new RelayCommand(() => ExecuteOrForward("ToggleShowYourGroundItemOnly", () => Ioc.Default.GetRequiredService<IScriptLite>().ShowYourGroundItemOnly ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleShowYourAuraAnimationOnly"] = new RelayCommand(() => ExecuteOrForward("ToggleShowYourAuraAnimationOnly", () => Ioc.Default.GetRequiredService<IScriptLite>().ShowYourAuraAnimationOnly ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleShowMonsterType"] = new RelayCommand(() => ExecuteOrForward("ToggleShowMonsterType", () => Ioc.Default.GetRequiredService<IScriptLite>().ShowMonsterType ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleUntargetDead"] = new RelayCommand(() => ExecuteOrForward("ToggleUntargetDead", () => Ioc.Default.GetRequiredService<IScriptLite>().UntargetDead ^= true), CanExecuteHotKey);
        _hotkeyRegistry["ToggleUntargetSelf"] = new RelayCommand(() => ExecuteOrForward("ToggleUntargetSelf", () => Ioc.Default.GetRequiredService<IScriptLite>().UntargetSelf ^= true), CanExecuteHotKey);

        // Utilities
        _hotkeyRegistry["PlayerRest"] = new RelayCommand(() => ExecuteOrForward("PlayerRest", () => Ioc.Default.GetRequiredService<IScriptPlayer>().Rest()), CanExecuteHotKey);
        _hotkeyRegistry["RejectAllDrops"] = new RelayCommand(() => ExecuteOrForward("RejectAllDrops", () => Ioc.Default.GetRequiredService<IScriptDrop>().RejectAll()), CanExecuteHotKey);

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
            return foregroundProcessId == (uint)Environment.ProcessId;
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

    private static void ToggleScriptLocal()
    {
        StrongReferenceMessenger.Default.Send<ToggleScriptMessage, int>((int)MessageChannels.ScriptStatus);
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
