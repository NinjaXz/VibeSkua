using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Skua.App.Avalonia.ViewModels.Options;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.Services;
using Skua.Core.Scripts;
using Skua.Core.Utils;
using Skua.Shared.Avalonia.ViewModels.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Skua.App.Avalonia.AppStartup;

internal class Options
{
    internal static GameOptionsViewModel CreateGameOptions(IServiceProvider s)
    {
        IScriptOption scriptOpt = s.GetService<IScriptOption>()!;

        List<DisplayOptionItemViewModelBase> options = new()
        {
            new CommandOptionItemViewModel<bool>("Upgrade Name Colour", "1", CreateBoolCommand(b => Ioc.Default.GetRequiredService<IFlashUtil>().SetGameObject("world.myAvatar.pMC.pname.ti.textColor", b ? 0x8CD5FF : 0xFFFFFF))),
            new CommandOptionItemViewModel<bool>("Staff Name Colour", "1", CreateBoolCommand(b => Ioc.Default.GetRequiredService<IFlashUtil>().SetGameObject("world.myAvatar.pMC.pname.ti.textColor", b ? 0xFECB38 : 0xFFFFFF))),
            new CommandOptionItemViewModel<IRelayCommand>("Reload Map", "4", new RelayCommand(Ioc.Default.GetRequiredService<IScriptMap>().Reload)) { ActionText = "Reload" }
        };

        foreach (PropertyInfo pi in scriptOpt.GetType().GetProperties())
        {
            if (pi.Name is nameof(IScriptOption.OptionDictionary) or nameof(ObservableRecipient.IsActive) or nameof(IScriptOption.GuildColor) or nameof(IScriptOption.NameColor) or "IsIpcMessageProcessing" or nameof(IScriptOption.ReloginTryDelay) or nameof(IScriptOption.UseFunctionBasedSkills) or nameof(IScriptOption.ReloginServer))
                continue;
            if (pi.PropertyType == typeof(bool))
                options.Add(CreateOptionItem<bool>(s, pi.Name, "1", new RelayCommand<bool>(b => pi.SetValue(scriptOpt, b))));
            else if (pi.PropertyType == typeof(string))
                options.Add(CreateOptionItem<string>(s, pi.Name, "2", new RelayCommand<string>(value => pi.SetValue(scriptOpt, value))));
            else if (pi.PropertyType == typeof(int))
            {
                string? suffixText = pi.Name == nameof(IScriptOption.SetFPS) ? "FPS" : null;
                options.Add(CreateOptionItemWithSuffix<int>(s, pi.Name, "3", suffixText, new RelayCommand<string>(value =>
                {
                    if (!int.TryParse(value, out int result))
                        return;
                    pi.SetValue(scriptOpt, result);
                })));
            }
        }

        return new GameOptionsViewModel(options, s.GetService<IScriptServers>()!, scriptOpt);

        static RelayCommand<bool> CreateBoolCommand(Action<bool> action) => new(action);
        static BindingOptionItemViewModel<T, ScriptOption> CreateOptionItem<T>(IServiceProvider s, string binding, string tag, IRelayCommand command) => new(Label(s, binding), tag, binding, (ScriptOption)s.GetRequiredService<IScriptOption>(), command);
        // The decamelizer keeps "ACDrops" together
        static string Label(IServiceProvider s, string binding) => binding == nameof(IScriptOption.AcceptACDrops) ? "Accept AC Drops" : s.GetRequiredService<IDecamelizer>().Decamelize(binding, null);
        static BindingOptionItemViewModel<T, ScriptOption> CreateOptionItemWithSuffix<T>(IServiceProvider s, string binding, string tag, string? suffixText, IRelayCommand command)
        {
            BindingOptionItemViewModel<T, ScriptOption> item = new(s.GetRequiredService<IDecamelizer>().Decamelize(binding, null), tag, binding, (ScriptOption)s.GetRequiredService<IScriptOption>(), command)
            {
                SuffixText = suffixText
            };
            return item;
        }
    }

    internal static ApplicationOptionsViewModel CreateAppOptions(IServiceProvider s)
    {
        List<DisplayOptionItemViewModelBase> appOptions = new()
        {
            CreateSettingOptionItem<bool>("Show Username in Title", "Whether to display logged-in character name in window title", "ShowUsernameInTitle"),
            CreateSettingOptionItem<bool>("Auto Update Scripts", "Whether to auto update scripts when launching the Manager or the game client (the client asks first when this is off), needs \"Check for Scripts Updates\" to be true", "AutoUpdateBotScripts"),
            CreateSettingOptionItem<bool>("Check for Script Updates", "Whether to check for scripts updates when launching the Manager or the game client", "CheckBotScriptsUpdates"),
            CreateSettingOptionItem<bool>("Auto Update AdvanceSkill Sets", "Whether to auto update advance skill sets when launching the Manager, needs \"Check for AdvanceSkill Sets updates\" to be true", "AutoUpdateAdvanceSkillSetsUpdates"),
            CreateSettingOptionItem<bool>("Check for AdvanceSkill Sets Updates", "Whether to check for scripts updates when launching the Manager", "CheckAdvanceSkillSetsUpdates"),
            CreateSettingOptionItem<bool>("Auto Update Junk Items", "Whether to auto update junk items when launching the Manager, needs \"Check for Junk Items Updates\" to be true", "AutoUpdateJunkItems"),
            CreateSettingOptionItem<bool>("Check for Junk Items Updates", "Whether to check for junk items updates when launching the Manager", "CheckJunkItemsUpdates"),
            CreateSettingOptionItem<string>("Discord Webhook URL", "URL to send Discord notifications and live statistics when scripts start, stop, or crash", "DiscordWebhookUrl"),
            new CommandOptionItemViewModel<IRelayCommand>("Test Discord Webhook", new AsyncRelayCommand(async () => {
                await Ioc.Default.GetRequiredService<IDiscordWebhookService>().TestWebhookAsync();
            })) { ActionText = "Test" },
            CreateSettingOptionItem<string>("Skua Nexus Webhook URL", "URL to send Skua Nexus notifications and live bot statistics", "NexusWebhookUrl"),
            new CommandOptionItemViewModel<IRelayCommand>("Test Skua Nexus Webhook", new AsyncRelayCommand(async () => {
                await Ioc.Default.GetRequiredService<IDiscordWebhookService>().TestNexusWebhookAsync();
            })) { ActionText = "Test" },
            CreateSettingOptionItem<bool>("Notify on Script Started", "Send alerts and initial statistics when a script starts running", "WebhookNotifyStarted"),
            CreateSettingOptionItem<bool>("Notify on Script Stopped", "Send session summary and statistics when a script finishes or stops", "WebhookNotifyStopped"),
            CreateSettingOptionItem<bool>("Notify on Script Crashed", "Send alerts when a script crashes with an error", "WebhookNotifyCrashed"),
            CreateSettingOptionItem<bool>("Notify on Relog", "Send alerts when Skua disconnects and relogs", "WebhookNotifyRelogged"),
            CreateSettingOptionItem<bool>("Notify on Item Drop", "Send webhook notifications (and screenshot) when a specific item drops", "WebhookNotifyItemDrops"),
            CreateSettingOptionItem<string>("Drop List", "Comma-separated list of items to notify on drop", "WebhookNotifyItemDropsList"),
            CreateSettingOptionItem<bool>("Enable Health Pings", "Whether to send periodic health and bot statistics pings to Discord", "WebhookNotifyHealthPing"),
            new CommandOptionItemViewModel<int>("Health Ping Interval", "Minutes between health pings (e.g. 5, 10, 30)", "WebhookPingInterval", "Min", new RelayCommand<string>(value =>
            {
                if (!int.TryParse(value, out int result) || result < 1) return;
                Ioc.Default.GetRequiredService<ISettingsService>().Set("WebhookPingInterval", result);
            }), s.GetRequiredService<ISettingsService>().Get<int>("WebhookPingInterval")),
            new CommandOptionItemViewModel<int>("Client Animation Frame-rate", "Client side animation frame-rate setting", "ClientAnim", "FPS", new RelayCommand<string>(value =>
            {
                if (!int.TryParse(value, out int result) || result < 1)
                    return;
                Ioc.Default.GetRequiredService<ISettingsService>().Set("AnimationFrameRate", result);
            }), s.GetRequiredService<ISettingsService>().Get<int>("AnimationFrameRate")) { RequiresRestart = true },
            new CommandOptionItemViewModel<int>("Relogin Try Delay", "Delay in milliseconds between login attempts when disconnected", "ReloginTryDelay", "ms", new RelayCommand<string>(value =>
            {
                if (!int.TryParse(value, out int result) || result < 0)
                    return;
                Ioc.Default.GetRequiredService<ISettingsService>().Set("ReloginTryDelay", result);
            }), s.GetRequiredService<ISettingsService>().Get<int>("ReloginTryDelay")),
            CreateSettingOptionItem<bool>("Relogin Watchdog", "Watch for unexpected disconnections, even with no script running, and relog when Auto Relogin (Game Options) is on. Off leaves relogs to kicks and server logouts", "ReloginWatchdog"),
            new CommandOptionItemViewModel<IRelayCommand>("Clear Flash Cache", "ClearFlashCache", new RelayCommand(() =>
            {
                string flash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Adobe", "Flash Player");
                string macromedia = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Macromedia", "Flash Player");

                if (Directory.Exists(flash))
                    Directory.Delete(flash, true);
                if (Directory.Exists(macromedia))
                    Directory.Delete(macromedia, true);
            })) { ActionText = "Clear" }
        };

        return new ApplicationOptionsViewModel(appOptions);

        static RelayCommand<T> CreateSettingCommand<T>(string key) => new(b => Ioc.Default.GetRequiredService<ISettingsService>().Set(key, b));
        static CommandOptionItemViewModel<T> CreateSettingOptionItem<T>(string content, string description, string key) => new(content, description, key, (IRelayCommand)CreateSettingCommand<T>(key), Ioc.Default.GetRequiredService<ISettingsService>().Get<T>(key));
    }
}
