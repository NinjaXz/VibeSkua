using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Core.Interfaces;

namespace Skua.Core.ViewModels;

public partial class OptionContainerViewModel : ObservableObject
{
    public OptionContainerViewModel(IOptionContainer container)
    {
        Container = container;
        Options = new();
        foreach (IOption option in container.Options)
            Options.Add(new(container, option));

        if (container.MultipleOptions.Count > 0)
        {
            foreach (List<IOption> optionList in container.MultipleOptions.Values)
            {
                foreach (IOption option in optionList)
                    Options.Add(new(container, option));
            }
        }

        Title = GetTitle();
    }

    private static string GetTitle()
    {
        string title = "Options";
        try
        {
            string? username = null;
            var player = Ioc.Default.GetService<IScriptPlayer>();
            if (player != null)
            {
                username = player.Username;
            }
            if (string.IsNullOrWhiteSpace(username) || username == "null" || username == "undefined")
            {
                var flashUtil = Ioc.Default.GetService<IFlashUtil>();
                if (flashUtil != null)
                {
                    username = flashUtil.Call<string>("getGameObjectS", "loginInfo.strUsername");
                    if (string.IsNullOrWhiteSpace(username) || username == "null" || username == "undefined")
                    {
                        username = flashUtil.Call<string>("getGameObject", "world.myAvatar.objData.strUsername");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(username) && username != "null" && username != "undefined")
            {
                title = $"Options - {username}";
            }
        }
        catch { }

        return title;
    }

    [ObservableProperty]
    private string _title = "Options";

    public IOptionContainer Container { get; set; }

    public List<OptionContainerItemViewModel> Options { get; }

    [ObservableProperty]
    private OptionContainerItemViewModel? _selectedOption;
}