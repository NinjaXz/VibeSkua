using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Skua.Core.Interfaces;
using Skua.Core.ViewModels;

namespace Skua.Core.AppStartup;

internal class MainMenu
{
    internal static MainMenuViewModel CreateViewModel(IServiceProvider s)
    {
        ManagedWindows.Register(s);

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
                new("Junk Items")
            }),
            new("Combat", new List<MainMenuItemViewModel>()
            {
                new("Skills"),
                new("Stats"),
                new("Runtime")
            }),
            new("Bank", new RelayCommand(Ioc.Default.GetRequiredService<IScriptBank>().Open)),
            new("Diagnostics", new List<MainMenuItemViewModel>()
            {
                new("Logs"),
                new("Console"),
                new("Spammer"),
                new("Logger"),
                new("Interceptor")
            })
        };

        return new(menuItems, s.GetRequiredService<AutoViewModel>(), s.GetRequiredService<JumpViewModel>(), s.GetRequiredService<IWindowService>());
    }
}