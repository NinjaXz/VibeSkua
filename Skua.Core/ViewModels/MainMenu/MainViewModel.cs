using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Skua.Core.Interfaces;
using Skua.Core.Messaging;

namespace Skua.Core.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "VibeSkua V1.6";

    public MainViewModel(IEnumerable<TabItemViewModel> tabs, IDialogService dialogService)
    {
        _title = "VibeSkua V1.6";
        Ioc.Default.GetRequiredService<IDiscordWebhookService>().Initialize();
    }

    [RelayCommand]
    private void ShowMainWindow()
    {
        StrongReferenceMessenger.Default.Send<ShowMainWindowMessage>();
    }
}
