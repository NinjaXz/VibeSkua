using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;
using Skua.Core.Models;

namespace Skua.App.Avalonia.ViewModels.MainMenu;

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "VibeSkua V2.1.2";

    public MainMenuViewModel MainMenu { get; }

    public AutoViewModel AutoViewModel => MainMenu.AutoViewModel;
    public JumpViewModel JumpViewModel => MainMenu.JumpViewModel;
    public IRelayCommand ShowBotWindowCommand => MainMenu.ShowBotWindowCommand;

    public MainViewModel(MainMenuViewModel mainMenu, IDiscordWebhookService discordWebhookService)
    {
        MainMenu = mainMenu;
        _title = $"VibeSkua V{ClientFileSources.DisplayVersion}";
        discordWebhookService.Initialize();
    }

    [RelayCommand]
    private void ShowMainWindow()
    {
        StrongReferenceMessenger.Default.Send<ShowMainWindowMessage>();
    }
}
