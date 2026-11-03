using Microsoft.Extensions.DependencyInjection;
using Skua.App.Avalonia.Plugins;
using Skua.App.Avalonia.Services;
using Skua.App.Avalonia.ViewModels;
using Skua.App.Avalonia.ViewModels.AdvancedSkills;
using Skua.App.Avalonia.ViewModels.AppLogs;
using Skua.App.Avalonia.ViewModels.CoreBotsOptions;
using Skua.App.Avalonia.ViewModels.Diagnostics;
using Skua.App.Avalonia.ViewModels.Dialogs;
using Skua.App.Avalonia.ViewModels.FastTravel;
using Skua.App.Avalonia.ViewModels.Grabber;
using Skua.App.Avalonia.ViewModels.HotKeys;
using Skua.App.Avalonia.ViewModels.MainMenu;
using Skua.App.Avalonia.ViewModels.Options;
using Skua.App.Avalonia.ViewModels.Packets;
using Skua.App.Avalonia.ViewModels.Plugins;
using Skua.App.Avalonia.ViewModels.Runtime;
using Skua.Core.GameProxy;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Options;
using Skua.Core.Plugins;
using Skua.Core.Scripts;
using Skua.Core.Scripts.Helpers;
using Skua.Core.Services;
using Skua.Core.Skills;
using Skua.Shared.Avalonia.ViewModels;
using Skua.Shared.Avalonia.ViewModels.ScriptRepo;
using System.Collections.Generic;

namespace Skua.App.Avalonia.AppStartup;

public static class Services
{
    public static IServiceCollection AddCompiler(this IServiceCollection services)
    {
        services.AddTransient(Core.AppStartup.Services.CreateCompiler);

        return services;
    }
    
    public static IServiceCollection AddScriptableObjects(this IServiceCollection services)
    {
        services.AddSingleton<IScriptInterface, ScriptInterface>();
        services.AddSingleton<IScriptManager, ScriptManager>();
        services.AddSingleton<IScriptStatus, ScriptManager>();

        services.AddSingleton<IScriptInventoryHelper, ScriptInventoryHelper>();
        services.AddSingleton<IScriptInventory, ScriptInventory>();
        services.AddSingleton<IScriptHouseInv, ScriptHouseInv>();
        services.AddSingleton<IScriptTempInv, ScriptTempInv>();
        services.AddSingleton<IScriptBank, ScriptBank>();

        services.AddSingleton<IAdvancedSkillContainer, AdvancedSkillContainer>();
        services.AddSingleton<IUltraBossHelper, UltraBossHelper>();
        services.AddSingleton<IScriptCombat, ScriptCombat>();
        services.AddSingleton<IScriptKill, ScriptKill>();
        services.AddSingleton<IScriptHunt, ScriptHunt>();
        services.AddSingleton<IScriptSkill, ScriptSkill>();
        services.AddSingleton<IScriptAuto, ScriptAuto>();
        services.AddSingleton<IScriptSelfAuras, ScriptSelfAuras>();
        services.AddSingleton<IScriptTargetAuras, ScriptTargetAuras>();

        services.AddSingleton<IScriptFaction, ScriptFaction>();
        services.AddSingleton<IScriptMonster, ScriptMonster>();
        services.AddSingleton<IScriptPlayer, ScriptPlayer>();
        services.AddSingleton<IScriptQuest, ScriptQuest>();
        services.AddSingleton<IScriptBoost, ScriptBoost>();
        services.AddSingleton<IScriptShop, ScriptShop>();
        services.AddSingleton<IScriptDrop, ScriptDrop>();
        services.AddSingleton<IScriptMap, ScriptMap>();

        services.AddSingleton<IScriptServers, ScriptServers>();
        services.AddSingleton<IScriptEvent, ScriptEvent>();
        services.AddSingleton<IScriptSend, ScriptSend>();

        services.AddTransient<IScriptOptionContainer, ScriptOptionContainer>();
        services.AddTransient<IOptionContainer, OptionContainer>();
        services.AddSingleton<IScriptOption, ScriptOption>();
        services.AddSingleton<IScriptLite, ScriptLite>();

        services.AddSingleton<IScriptBotStats, ScriptBotStats>();
        services.AddSingleton<IScriptHandlers, ScriptHandlers>();
        services.AddSingleton<IScriptWait, ScriptWait>();
        services.AddSingleton<IScriptAccounts, ScriptAccounts>();

        services.AddSingleton<ICaptureProxy, CaptureProxy>();

        services.AddSingleton<IPluginManager, PluginManager>();
        services.AddTransient<IPluginContainer, PluginContainer>();
        services.AddSingleton<IPluginHelper, PluginHelper>();

        services.AddSingleton<IMapService, MapService>();
        services.AddSingleton<ILogService, LogService>();
        services.AddSingleton<IQuestDataLoaderService, QuestDataLoaderService>();
        services.AddSingleton<IGrabberService, GrabberService>();
        services.AddSingleton<IClientFilesService, ClientFilesService>();
        services.AddSingleton<IAuraMonitorService, AuraMonitorService>();
        services.AddSingleton<IJunkService, JunkService>();
        services.AddSingleton<BackgroundThemeService>();

        return services;
    }
    
    public static IServiceCollection AddSkuaMainAppViewModels(this IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();
        services.AddSingleton(MainMenu.CreateViewModel);
        services.AddTransient<BotWindowViewModel>();
        services.AddSingleton<IEnumerable<BotWindowItemViewModel>>(s => new List<BotWindowItemViewModel>()
        {
            new("Script Loader", () => s.GetRequiredService<ScriptLoaderViewModel>()),
            new("Script Repo", () => s.GetRequiredService<ScriptRepoViewModel>()),
            new("Scheduler", () => s.GetRequiredService<ScriptSchedulerViewModel>()),
            new("Logs", () => s.GetRequiredService<LogsViewModel>()),
            new("Notification Center", () => s.GetRequiredService<NotificationCenterViewModel>()),
            new("Auto", () => s.GetRequiredService<AutoViewModel>()),
            new("Jump", () => s.GetRequiredService<JumpViewModel>()),
            new("Fast Travel", () => s.GetRequiredService<FastTravelViewModel>()),
            new("Current Drops", () => s.GetRequiredService<CurrentDropsViewModel>()),
            new("Junk Items", () => s.GetRequiredService<JunkItemsViewModel>()),
            new("Runtime", () => s.GetRequiredService<RuntimeHelpersViewModel>()),
            new("Loader", () => s.GetRequiredService<LoaderViewModel>()),
            new("Grabber", () => s.GetRequiredService<GrabberViewModel>()),
            new("Game", () => s.GetRequiredService<GameOptionsViewModel>()),
            new("Application", () => s.GetRequiredService<ApplicationOptionsViewModel>()),
            new("Console", () => s.GetRequiredService<ConsoleViewModel>()),
            new("Diagnostics", () => s.GetRequiredService<DiagnosticsViewModel>()),
            new("Skills", () => s.GetRequiredService<AdvancedSkillsViewModel>()),
            new("Interceptor", () => s.GetRequiredService<PacketInterceptorViewModel>()),
            new("Spammer", () => s.GetRequiredService<PacketSpammerViewModel>()),
            new("Logger", () => s.GetRequiredService<PacketLoggerViewModel>()),
            new("Application Themes", () => s.GetRequiredService<ApplicationThemesViewModel>()),
            new("HotKeys", () => s.GetRequiredService<HotKeysViewModel>()),
            new("Plugins", () => s.GetRequiredService<PluginsViewModel>())
        });

        services.AddTransient<LoaderViewModel>();

        services.AddTransient(Grabber.CreateViewModel);
        services.AddTransient(Grabber.CreateListViewModels);

        services.AddSingleton<JumpViewModel>();
        
        services.AddSingleton<ScriptSchedulerViewModel>();

        services.AddTransient<FastTravelViewModel>();
        services.AddTransient<FastTravelEditorViewModel>();
        services.AddTransient<FastTravelEditorDialogViewModel>();

        services.AddSingleton<LogsViewModel>();
        services.AddSingleton(LogTabs.CreateViewModels);

        services.AddTransient(Options.CreateGameOptions);
        services.AddTransient(Options.CreateAppOptions);

        services.AddTransient(PacketLogger.CreateViewModel);
        services.AddTransient<PacketSpammerViewModel>();
        services.AddTransient(PacketInterceptor.CreateViewModel);

        services.AddTransient<ConsoleViewModel>();
        services.AddTransient<DiagnosticsViewModel>();
        services.AddSingleton<NotificationCenterViewModel>();
        services.AddTransient<CalendarViewModel>();

        services.AddSingleton<ScriptRepoViewModel>();
        services.AddSingleton<ScriptLoaderViewModel>();
        services.AddTransient<LoadoutsViewModel>();

        services.AddTransient<AdvancedSkillsViewModel>();
        services.AddTransient<AdvancedSkillEditorViewModel>();
        services.AddTransient<SavedAdvancedSkillsViewModel>();
        services.AddTransient<SkillRulesViewModel>();

        services.AddSingleton<AutoViewModel>();

        services.AddTransient<BoostsViewModel>();
        services.AddTransient<ScriptStatsViewModel>();
        services.AddTransient<RuntimeHelpersViewModel>();
        services.AddTransient<NotifyDropViewModel>();
        services.AddTransient<ToPickupDropsViewModel>();
        services.AddTransient<RegisteredQuestsViewModel>();
        services.AddTransient<CurrentDropsViewModel>();
        services.AddTransient<JunkItemsViewModel>();

        services.AddThemeViewModels();

        services.AddTransient<PluginsViewModel>();

        services.AddTransient<HotKeysViewModel>();
        // Hotkey command map is registered in App startup for this app host.

        services.AddTransient(CoreBots.CreateViewModel);
        services.AddTransient(CoreBots.CreateOptions);
        services.AddTransient<CBOClassEquipmentViewModel>();
        services.AddTransient<CBOClassSelectViewModel>();
        services.AddTransient<CBOLoadoutViewModel>();

        return services;
    }
    
    public static IServiceCollection AddThemeViewModels(this IServiceCollection services)
    {
        services.AddSingleton<ApplicationThemesViewModel>();
        services.AddSingleton<ThemeSettingsViewModel>();
        services.AddSingleton<ColorSchemeEditorViewModel>();
        services.AddSingleton<BackgroundThemeViewModel>();

        return services;
    }
    
    
}
