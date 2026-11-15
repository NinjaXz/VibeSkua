using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Messaging;
using Skua.Manager.Avalonia.ViewModels;
using Skua.Shared.Avalonia.ViewModels;
using Skua.Shared.Avalonia.ViewModels.Options;
using Skua.Shared.Avalonia.ViewModels.ScriptRepo;
using System;
using System.Collections.Generic;

namespace Skua.Manager.Avalonia.AppStartup;

internal class SkuaManager
{
    internal static ManagerMainViewModel CreateViewModel(IServiceProvider s)
    {
        List<TabItemViewModel> tabs = new()
        {
            new((string)"Accounts", s.GetRequiredService<AccountManagerViewModel>()),
            new((string)"Army Queue", s.GetRequiredService<ArmyQueueViewModel>()),
            new((string)"Task Scheduler", s.GetRequiredService<SchedulerViewModel>()),
            new((string)"Launcher", s.GetRequiredService<LauncherViewModel>()),
            new((string)"Updater", s.GetRequiredService<AppUpdaterViewModel>()),
            new((string)"Scripts", s.GetRequiredService<ScriptUpdaterViewModel>()),
            new((string)"Options", s.GetRequiredService<ManagerOptionsViewModel>()),
            new((string)"Themes", s.GetRequiredService<ApplicationThemesViewModel>()),
            new((string)"About", s.GetRequiredService<AboutViewModel>()),
        };
        return new(tabs, s.GetRequiredService<IDialogService>(), s.GetRequiredService<ISettingsService>());
    }

    internal static ManagerOptionsViewModel CreateOptionsViewModel(IServiceProvider s)
    {
        List<DisplayOptionItemViewModelBase> options = new()
        {
            CreateSettingOptionItem<bool>("Use Manager theme on VibeSkua", "Whether to use, when launching from the Launcher tab, the same theme as the Manager in any launched App", "syncTheme")
        };

        return new(options, s.GetRequiredService<ISettingsService>(), s.GetRequiredService<IFileDialogService>());

        static RelayCommand<T> CreateSettingCommand<T>(string key) => new(b => Ioc.Default.GetRequiredService<ISettingsService>().Set(key, b));
        static CommandOptionItemViewModel<T> CreateSettingOptionItem<T>(string content, string description, string key) => new(content, description, key, (IRelayCommand)CreateSettingCommand<T>(key), Ioc.Default.GetRequiredService<ISettingsService>().Get<T>(key));
    }
}
