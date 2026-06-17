using Skua.Core.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Skua.WPF.UserControls;

/// <summary>
/// Interaction logic for AssignHotKeyDialog.xaml
/// </summary>
public partial class AssignHotKeyDialog : UserControl
{
    private const string WaitingInputText = "Waiting input...";
    private const string CaptureHintText = "Press a non-modifier key (Esc to cancel).";
    private const string ModifierOnlyHintText = "Modifier keys cannot be used alone. Press another key.";
    private const string SaveWithoutKeyHintText = "Press a non-modifier key before saving.";
    private Window? _window;
    private AssignHotKeyDialogViewModel? _vm;

    public AssignHotKeyDialog()
    {
        InitializeComponent();
        Loaded += AssignHotKeyDialog_Loaded;
    }

    private void AssignHotKeyDialog_Loaded(object sender, RoutedEventArgs e)
    {
        _window = Window.GetWindow(this);
        _vm = DataContext as AssignHotKeyDialogViewModel;
        Loaded -= AssignHotKeyDialog_Loaded;
    }

    private void Button_Click(object sender, RoutedEventArgs e)
    {
        if (_window is null || _vm is null)
            return;

        if (string.Equals(_vm.KeyInput, WaitingInputText, StringComparison.Ordinal))
        {
            _vm.KeyInput = backupKey;
            _vm.InputHint = SaveWithoutKeyHintText;
            _window.KeyDown -= _window_KeyDown;
            return;
        }

        if (IsModifierKeyInput(_vm.KeyInput))
        {
            _vm.InputHint = ModifierOnlyHintText;
            return;
        }

        if (System.Linq.Enumerable.Contains(_vm.UsedGestures, _vm.KeyGesture, StringComparer.OrdinalIgnoreCase))
        {
            _vm.InputHint = "This hotkey is already assigned to another action.";
            return;
        }

        _vm.InputHint = string.Empty;
        _window.KeyDown -= _window_KeyDown;
        _window.DialogResult = true;
    }

    private string backupKey = string.Empty;

    private void AssignKey(object sender, RoutedEventArgs e)
    {
        if (_window is null || _vm is null)
            return;
        _window.KeyDown -= _window_KeyDown;
        _window.KeyDown += _window_KeyDown;
        backupKey = _vm.KeyInput;
        _vm.KeyInput = WaitingInputText;
        _vm.InputHint = CaptureHintText;
    }

    private void _window_KeyDown(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _vm!.KeyInput = backupKey;
            _vm.InputHint = string.Empty;
            _window!.KeyDown -= _window_KeyDown;
            e.Handled = true;
            return;
        }

        if (IsModifierKey(key))
        {
            _vm!.InputHint = ModifierOnlyHintText;
            e.Handled = true;
            return;
        }

        _vm!.KeyInput = key.ToString();
        _vm.InputHint = string.Empty;

        _window!.KeyDown -= _window_KeyDown;
        e.Handled = true;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt;
    }

    private static bool IsModifierKeyInput(string keyInput)
    {
        return string.Equals(keyInput, nameof(Key.LeftCtrl), StringComparison.Ordinal)
            || string.Equals(keyInput, nameof(Key.RightCtrl), StringComparison.Ordinal)
            || string.Equals(keyInput, nameof(Key.LeftShift), StringComparison.Ordinal)
            || string.Equals(keyInput, nameof(Key.RightShift), StringComparison.Ordinal)
            || string.Equals(keyInput, nameof(Key.LeftAlt), StringComparison.Ordinal)
            || string.Equals(keyInput, nameof(Key.RightAlt), StringComparison.Ordinal);
    }
}