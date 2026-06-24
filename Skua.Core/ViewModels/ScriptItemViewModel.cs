using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace Skua.Core.ViewModels;

public partial class ScriptItemViewModel : ObservableObject
{
    public ScriptItemViewModel(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Status = "Queued";
    }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _path;

    [ObservableProperty]
    private string _status;

    [ObservableProperty]
    private string _duration = "00:00";

    public System.Guid Id { get; set; } = System.Guid.NewGuid();

    public string Storage => $"Queue_{System.IO.Path.GetFileNameWithoutExtension(Path)}_{Id}";
}
