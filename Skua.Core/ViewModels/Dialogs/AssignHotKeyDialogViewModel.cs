using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace Skua.Core.ViewModels;

public partial class AssignHotKeyDialogViewModel : DialogViewModelBase
{
    public AssignHotKeyDialogViewModel(string title, HotKey hotKey)
        : base(title)
    {
        _keyInput = hotKey.Key;
        _ctrlCheck = hotKey.Ctrl;
        _altCheck = hotKey.Alt;
        _shiftCheck = hotKey.Shift;
    }

    public AssignHotKeyDialogViewModel(string title)
        : base(title)
    {
        _keyInput = string.Empty;
    }

    public IEnumerable<string> UsedGestures { get; set; } = Enumerable.Empty<string>();

    [ObservableProperty]
    private bool _ctrlCheck;

    [ObservableProperty]
    private bool _altCheck;

    [ObservableProperty]
    private bool _shiftCheck;

    [ObservableProperty]
    private string _keyInput;

    [ObservableProperty]
    private string _inputHint = string.Empty;

    public string KeyGesture => $"{(CtrlCheck ? "Ctrl+" : string.Empty)}{(ShiftCheck ? "Shift+" : string.Empty)}{(AltCheck ? "Alt+" : string.Empty)}{KeyInput}";
}