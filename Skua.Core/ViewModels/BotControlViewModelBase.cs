using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;

namespace Skua.Core.ViewModels;

public class BotControlViewModelBase : ObservableRecipient, IManagedWindow
{
    public virtual string Title { get; protected set; }
    public int Width { get; }
    public int Height { get; }
    public bool CanResize { get; }

    public BotControlViewModelBase(string title, int width, int height, bool canResize = true)
    {
        Title = title;
        Width = width;
        Height = height;
        CanResize = canResize;
    }

    public BotControlViewModelBase(string title)
    {
        Title = title;
        Width = 800;
        Height = 450;
        CanResize = true;
    }
}