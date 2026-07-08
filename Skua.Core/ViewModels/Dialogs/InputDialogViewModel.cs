namespace Skua.Core.ViewModels;

public class InputDialogViewModel : DialogViewModelBase
{
    public InputDialogViewModel(string title)
        : base(title)
    {
        NumberOnly = true;
        TextBoxHint = "Quantity";
    }

    public InputDialogViewModel(string title, string dialogHint)
        : base(title)
    {
        NumberOnly = true;
        DialogHint = dialogHint;
        TextBoxHint = "Quantity";
    }

    public InputDialogViewModel(string title, string dialogHint, string textBoxHint)
        : base(title)
    {
        NumberOnly = true;
        DialogHint = dialogHint;
        TextBoxHint = textBoxHint;
    }

    public InputDialogViewModel(string title, bool numericInputOnly)
        : base(title)
    {
        NumberOnly = numericInputOnly;
    }

    public InputDialogViewModel(string title, string dialogHint, bool numericInputOnly)
        : base(title)
    {
        NumberOnly = numericInputOnly;
        DialogHint = dialogHint;
    }

    public InputDialogViewModel(string title, string dialogHint, string textBoxHint, bool numericInputOnly)
        : base(title)
    {
        NumberOnly = numericInputOnly;
        DialogHint = dialogHint;
        TextBoxHint = textBoxHint;
    }

    public InputDialogViewModel(string title, string dialogHint, string textBoxHint, string secondTextBoxHint, bool numericInputOnly = false)
        : base(title)
    {
        NumberOnly = numericInputOnly;
        DialogHint = dialogHint;
        TextBoxHint = textBoxHint;
        SecondTextBoxHint = secondTextBoxHint;
    }

    public bool NumberOnly { get; }
    private string _dialogTextInput = string.Empty;

    public string DialogTextInput
    {
        get => _dialogTextInput; set => SetProperty(ref _dialogTextInput, value);
    }

    private string _secondTextInput = string.Empty;

    public string SecondTextInput
    {
        get => _secondTextInput; set => SetProperty(ref _secondTextInput, value);
    }

    public string DialogHint { get; } = string.Empty;
    public string TextBoxHint { get; } = string.Empty;
    public string SecondTextBoxHint { get; } = string.Empty;
    public bool ShowSecondInput => !string.IsNullOrEmpty(SecondTextBoxHint);
}