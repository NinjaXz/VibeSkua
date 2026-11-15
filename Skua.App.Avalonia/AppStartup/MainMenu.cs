using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Skua.App.Avalonia.ViewModels;
using Skua.App.Avalonia.ViewModels.AdvancedSkills;
using Skua.App.Avalonia.ViewModels.AppLogs;
using Skua.App.Avalonia.ViewModels.CoreBotsOptions;
using Skua.App.Avalonia.ViewModels.Diagnostics;
using Skua.App.Avalonia.ViewModels.FastTravel;
using Skua.App.Avalonia.ViewModels.Grabber;
using Skua.App.Avalonia.ViewModels.HotKeys;
using Skua.App.Avalonia.ViewModels.MainMenu;
using Skua.App.Avalonia.ViewModels.Options;
using Skua.App.Avalonia.ViewModels.Packets;
using Skua.App.Avalonia.ViewModels.Plugins;
using Skua.App.Avalonia.ViewModels.Runtime;
using Skua.Core.Interfaces;
using Skua.Shared.Avalonia.ViewModels;
using Skua.Shared.Avalonia.ViewModels.ScriptRepo;
using System;
using System.Collections.Generic;

namespace Skua.App.Avalonia.AppStartup;

internal class MainMenu
{
    public static void RegisterManagedWindows(IWindowService windowService)
    {
        // Initialize the singleton event collector before the user opens its window.
        Ioc.Default.GetRequiredService<NotificationCenterViewModel>();
        windowService.RegisterManagedWindow<ScriptLoaderViewModel>("Scripts");
        windowService.RegisterManagedWindow<ScriptLoaderViewModel>("Script Loader");
        windowService.RegisterManagedWindow<ScriptSchedulerViewModel>("Scheduler");
        windowService.RegisterManagedWindow<ScriptRepoViewModel>("Script Repo");
        windowService.RegisterManagedWindow<LoadoutsViewModel>("Loadouts");
        windowService.RegisterManagedWindow<LoadoutsViewModel>("Loadouts Manager");

        windowService.RegisterManagedWindow<GameOptionsViewModel>("Game");
        windowService.RegisterManagedWindow<ApplicationOptionsViewModel>("Application");
        windowService.RegisterManagedWindow<CoreBotsViewModel>("CoreBots");
        windowService.RegisterManagedWindow<ApplicationThemesViewModel>("Application Themes");
        windowService.RegisterManagedWindow<HotKeysViewModel>("HotKeys");

        windowService.RegisterManagedWindow<RuntimeHelpersViewModel>("Runtime");
        windowService.RegisterManagedWindow<NotifyDropViewModel>("Notify Drop");
        windowService.RegisterManagedWindow<FastTravelViewModel>("Fast Travel");
        windowService.RegisterManagedWindow<CurrentDropsViewModel>("Current Drops");
        windowService.RegisterManagedWindow<JunkItemsViewModel>("Junk Items");

        windowService.RegisterManagedWindow<LoaderViewModel>("Loader");
        windowService.RegisterManagedWindow<GrabberViewModel>("Grabber");
        windowService.RegisterManagedWindow<ScriptStatsViewModel>("Stats");
        windowService.RegisterManagedWindow<ConsoleViewModel>("Console");
        windowService.RegisterManagedWindow<DiagnosticsViewModel>("Diagnostics");
        windowService.RegisterManagedWindow<NotificationCenterViewModel>("Notification Center");
        windowService.RegisterManagedWindow<CalendarViewModel>("Calendar");
        windowService.RegisterManagedWindow<AuraTrackerViewModel>("Aura Tracker");

        windowService.RegisterManagedWindow<AdvancedSkillsViewModel>("Skills");

        windowService.RegisterManagedWindow<PacketSpammerViewModel>("Spammer");
        windowService.RegisterManagedWindow<PacketLoggerViewModel>("Logger");
        windowService.RegisterManagedWindow<PacketInterceptorViewModel>("Interceptor");

        windowService.RegisterManagedWindow<LogsViewModel>("Logs");
        windowService.RegisterManagedWindow<PluginsViewModel>("Plugins");
    }

    internal static MainMenuViewModel CreateViewModel(IServiceProvider s)
    {
        IWindowService windowService = s.GetRequiredService<IWindowService>();
        RegisterManagedWindows(windowService);

        List<MainMenuItemViewModel> menuItems = new()
        {
            new("Scripts", new List<MainMenuItemViewModel>()
            {
                new("Script Loader"),
                new("Scheduler")
            }),
            new("Options", new List<MainMenuItemViewModel>()
            {
                new("Game"),
                new("Application"),
                new("CoreBots"),
                new("Application Themes"),
                new("HotKeys")
            }),
            new("Tools & Helpers", new List<MainMenuItemViewModel>()
            {
                new("Loadouts Manager"),
                new("Fast Travel"),
                new("Loader"),
                new("Grabber"),
                new("Current Drops"),
                new("Junk Items"),
                new("Calendar")
            }),
            new("Combat", new List<MainMenuItemViewModel>()
            {
                new("Skills"),
                new("Aura Tracker"),
                new("Stats"),
                new("Runtime")
            }),
            new("Bank", new RelayCommand(Ioc.Default.GetRequiredService<IScriptBank>().Open)),
            new("Diagnostics", new List<MainMenuItemViewModel>()
            {
                new("Notification Center"),
                new("Logs"),
                new("Console"),
                new("Spammer"),
                new("Logger"),
                new("Interceptor")
            })
        };

        return new(menuItems, s.GetRequiredService<AutoViewModel>(), s.GetRequiredService<JumpViewModel>(), s.GetRequiredService<IWindowService>(), s.GetRequiredService<IPluginHelper>());
    }
}
