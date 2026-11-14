using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Skua.App.Avalonia.ViewModels.AdvancedSkills;
using Skua.App.Avalonia.ViewModels.Dialogs;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Models;
using Skua.Shared.Avalonia.Controls;
using Skua.Shared.Avalonia.Controls.Options;
using Skua.Shared.Avalonia.Controls.Shell;
using Skua.Shared.Avalonia.ViewModels.Dialogs;
using CommunityToolkit.Mvvm.DependencyInjection;
using Skua.Shared.Avalonia.ViewModels.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CustomDialogViewModel = Skua.Shared.Avalonia.ViewModels.Dialogs.CustomDialogViewModel;

namespace Skua.App.Avalonia.Services;

public class DialogService : IDialogService
{
    public bool? ShowDialog<TViewModel>(TViewModel viewModel) where TViewModel : class
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => ShowDialog(viewModel));
        return ShowDialogInternal(viewModel, DefaultTitle(viewModel));
    }

    public bool? ShowDialog<TViewModel>(TViewModel viewModel, string title) where TViewModel : class
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => ShowDialog(viewModel, title));
        return ShowDialogInternal(viewModel, title);
    }

    // A dialog shown without a title uses its own (DialogViewModelBase.Title) instead of the view model's type name.
    private static string DefaultTitle(object viewModel)
    {
        return viewModel is DialogViewModelBase { Title: { Length: > 0 } title } ? title : viewModel.GetType().Name;
    }

    public bool? ShowDialog<TViewModel>(TViewModel viewModel, Action<TViewModel> callback) where TViewModel : class
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => ShowDialog(viewModel, callback));
        bool? result = ShowDialogInternal(viewModel, DefaultTitle(viewModel));
        if (result == true)
            callback?.Invoke(viewModel);
        return result;
    }

    public void ShowDialog(IOptionContainer optionContainer, Action<IOptionContainerViewModel> callback)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => ShowDialog(optionContainer, callback));
            return;
        }
        OptionContainerViewModel vm = new(optionContainer);
        bool? result = ShowOptionContainerDialog(vm, "Script Options");
        if (result == true)
        {
            callback?.Invoke(vm);
        }
    }

    public void ShowMessageBox(string message, string caption)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Invoke(() => ShowMessageBox(message, caption));
            return;
        }
        _ = ShowButtonsDialog(caption, message, ["OK"]);
    }

    public bool? ShowMessageBox(string message, string caption, bool yesAndNo)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => ShowMessageBox(message, caption, yesAndNo));
        string[] buttons = yesAndNo ? ["No", "Yes"] : ["OK"];
        string clicked = ShowButtonsDialog(caption, message, buttons);
        return clicked == "Yes" || clicked == "OK";
    }

    public DialogResult ShowMessageBox(string message, string caption, params string[] buttons)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            return Dispatcher.UIThread.Invoke(() => ShowMessageBox(message, caption, buttons));
        string[] actualButtons = buttons.Length == 0 ? ["OK"] : buttons;
        string clicked = ShowButtonsDialog(caption, message, actualButtons);
        int index = Array.IndexOf(actualButtons, clicked);
        return index < 0 ? DialogResult.Cancelled : new DialogResult(clicked, index);
    }

    public IInputDialogViewModel CreateInputDialog(string title, string dialogHint)
    {
        return new InputDialogViewModel(title, dialogHint);
    }

    private static bool? ShowDialogInternal<TViewModel>(TViewModel viewModel, string title) where TViewModel : class
    {
        return viewModel switch
        {
            OptionContainerViewModel options => ShowOptionContainerDialog(options, string.IsNullOrWhiteSpace(title) || title == nameof(OptionContainerViewModel) ? options.Title : title),
            IOptionContainerViewModel optionsVm => ShowOptionContainerDialog(optionsVm as OptionContainerViewModel ?? new OptionContainerViewModel(optionsVm.Container), title),
            InputDialogViewModel input => ShowInputDialog(input, title),
            AssignHotKeyDialogViewModel hotKey => ShowAssignHotKeyDialog(hotKey, title),
            SkillRuleEditorDialogViewModel skillRules => ShowSkillRuleEditorDialog(skillRules, title),
            FastTravelEditorDialogViewModel fastTravel => ShowFastTravelEditorDialog(fastTravel, title),
            MessageBoxDialogViewModel messageBox => ShowMessageBoxInternal(messageBox.Message, messageBox.Title, messageBox.YesAndNo),
            CustomDialogViewModel custom => ShowCustomDialog(custom, title),
            _ => ShowFallbackDialog(viewModel.GetType().Name, title)
        };
    }

    private static bool? ShowInputDialog(InputDialogViewModel vm, string title)
    {
        Window dialog = CreateBaseDialog(title);
        TextBox input = new() { Text = vm.DialogTextInput, Watermark = vm.TextBoxHint };
        if (vm.NumberOnly)
        {
            input.TextChanged += (_, _) =>
            {
                string digitsOnly = new(input.Text?.Where(char.IsDigit).ToArray() ?? Array.Empty<char>());
                if (input.Text != digitsOnly)
                {
                    int caret = Math.Min(input.CaretIndex, digitsOnly.Length);
                    input.Text = digitsOnly;
                    input.CaretIndex = caret;
                }
            };
        }

        bool? result = null;
        Button ok = new() { Content = "OK", MinWidth = 84, Classes = { "skua-button", "skua-primary" } };
        Button cancel = new() { Content = "Cancel", MinWidth = 84, Classes = { "skua-button" }, IsCancel = true };
        ok.IsDefault = true;

        ok.Click += (_, _) =>
        {
            vm.DialogTextInput = input.Text ?? string.Empty;
            result = true;
            dialog.Close();
        };
        cancel.Click += (_, _) =>
        {
            result = false;
            dialog.Close();
        };

        StackPanel content = new()
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = vm.DialogHint, TextWrapping = TextWrapping.Wrap, FontSize = 13 },
                input,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok }
                }
            }
        };

        dialog.Content = WrapWithShell(title, content);
        Show(dialog, input);
        return result;
    }

    private static bool? ShowAssignHotKeyDialog(AssignHotKeyDialogViewModel vm, string title)
    {
        Window dialog = CreateBaseDialog(string.IsNullOrWhiteSpace(title) ? "Assign HotKey" : title);
        dialog.Width = 440;

        CheckBox ctrl = new() { Content = "Ctrl", IsChecked = vm.CtrlCheck };
        CheckBox shift = new() { Content = "Shift", IsChecked = vm.ShiftCheck };
        CheckBox alt = new() { Content = "Alt", IsChecked = vm.AltCheck };

        StackPanel modifiersPanel = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children = { ctrl, shift, alt }
        };

        TextBox keyInput = new()
        {
            Text = vm.KeyInput,
            IsReadOnly = true,
            FontFamily = new FontFamily("JetBrains Mono,Consolas,Courier New,monospace"),
            Watermark = "Click 'Capture Key' or press a key"
        };

        Button captureBtn = new()
        {
            Content = "Capture Key",
            Classes = { "skua-button" },
            MinWidth = 100
        };

        TextBlock hintLabel = new()
        {
            Text = string.Empty,
            Foreground = new SolidColorBrush(Color.Parse("#EF4444")),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };

        bool capturing = string.IsNullOrWhiteSpace(vm.KeyInput);
        if (capturing)
        {
            keyInput.Text = "Press any key...";
            hintLabel.Text = "Press any key (Esc to cancel)";
        }

        void StartCapture()
        {
            capturing = true;
            keyInput.Text = "Press any key...";
            hintLabel.Text = "Press any key (Esc to cancel)";
            dialog.Focus();
        }

        captureBtn.Click += (_, _) => StartCapture();
        keyInput.PointerPressed += (_, _) => StartCapture();

        dialog.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (!capturing) return;

            if (e.Key == Key.Escape)
            {
                capturing = false;
                keyInput.Text = vm.KeyInput;
                hintLabel.Text = string.Empty;
                e.Handled = true;
                return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.Key is Key.LeftCtrl or Key.RightCtrl)
                ctrl.IsChecked = true;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Key is Key.LeftShift or Key.RightShift)
                shift.IsChecked = true;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) || e.Key is Key.LeftAlt or Key.RightAlt)
                alt.IsChecked = true;

            if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt)
            {
                hintLabel.Text = "Modifier held. Now press a key...";
                e.Handled = true;
                return;
            }

            string keyName = e.Key switch
            {
                Key.Divide => "Divide",
                Key.Multiply => "Multiply",
                Key.Subtract => "Subtract",
                Key.Add => "Add",
                Key.Decimal => "Decimal",
                _ => e.Key.ToString()
            };
            keyInput.Text = keyName;
            capturing = false;
            hintLabel.Text = string.Empty;
            e.Handled = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        Button ok = new() { Content = "OK", MinWidth = 84, Classes = { "skua-button", "skua-primary" } };
        Button cancel = new() { Content = "Cancel", MinWidth = 84, Classes = { "skua-button" }, IsCancel = true };
        ok.IsDefault = true;

        bool? result = null;
        ok.Click += (_, _) =>
        {
            string chosenKey = keyInput.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(chosenKey) || chosenKey == "Press any key...")
            {
                hintLabel.Text = "Please assign a valid key.";
                return;
            }

            bool isCtrl = ctrl.IsChecked == true;
            bool isShift = shift.IsChecked == true;
            bool isAlt = alt.IsChecked == true;

            string gesture = $"{(isCtrl ? "Ctrl+" : "")}{(isShift ? "Shift+" : "")}{(isAlt ? "Alt+" : "")}{chosenKey}";
            if (vm.UsedGestures.Any(g => string.Equals(g, gesture, StringComparison.OrdinalIgnoreCase)))
            {
                hintLabel.Text = "This hotkey is already assigned to another action.";
                return;
            }

            vm.CtrlCheck = isCtrl;
            vm.ShiftCheck = isShift;
            vm.AltCheck = isAlt;
            vm.KeyInput = chosenKey;
            result = true;
            dialog.Close();
        };

        cancel.Click += (_, _) =>
        {
            result = false;
            dialog.Close();
        };

        Grid keyRow = new()
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children =
            {
                keyInput,
                captureBtn
            }
        };
        Grid.SetColumn(keyInput, 0);
        Grid.SetColumn(captureBtn, 1);
        captureBtn.Margin = new Thickness(8, 0, 0, 0);

        StackPanel content = new()
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Modifiers:", FontWeight = FontWeight.SemiBold, FontSize = 12 },
                modifiersPanel,
                new TextBlock { Text = "Assigned Key:", FontWeight = FontWeight.SemiBold, FontSize = 12 },
                keyRow,
                hintLabel,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok }
                }
            }
        };

        dialog.Content = WrapWithShell(string.IsNullOrWhiteSpace(title) ? "Assign HotKey" : title, content);
        Show(dialog);
        return result;
    }

    private static bool? ShowFastTravelEditorDialog(FastTravelEditorDialogViewModel vm, string title)
    {
        Window dialog = CreateBaseDialog(string.IsNullOrWhiteSpace(title) ? "Edit Fast Travel" : title);
        dialog.Width = 520;

        TextBox descriptionInput = new() { Text = vm.Editor.Travel.DescriptionName, Watermark = "Destination name" };
        TextBox mapInput = new() { Text = vm.Editor.Travel.MapName, Watermark = "Map name" };
        TextBox cellInput = new() { Text = vm.Editor.Travel.Cell, Watermark = "Cell" };
        TextBox padInput = new() { Text = vm.Editor.Travel.Pad, Watermark = "Pad" };

        TextBlock validationMessage = new()
        {
            Text = string.Empty,
            Foreground = new SolidColorBrush(Color.Parse("#EF4444")),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        };

        Button getCurrent = new()
        {
            Content = "Get Current",
            MinWidth = 100,
            Classes = { "skua-button" }
        };
        getCurrent.Click += (_, _) =>
        {
            vm.Editor.GetCurrentCommand.Execute(null);
            mapInput.Text = vm.Editor.Travel.MapName;
            cellInput.Text = vm.Editor.Travel.Cell;
            padInput.Text = vm.Editor.Travel.Pad;
            validationMessage.Text = string.Empty;
        };

        Button confirm = new() { Content = "Save", MinWidth = 84, Classes = { "skua-button", "skua-primary" }, IsDefault = true };
        Button cancel = new() { Content = "Cancel", MinWidth = 84, Classes = { "skua-button" }, IsCancel = true };

        bool? result = null;
        confirm.Click += (_, _) =>
        {
            vm.Editor.Travel.DescriptionName = descriptionInput.Text?.Trim() ?? string.Empty;
            vm.Editor.Travel.MapName = mapInput.Text?.Trim() ?? string.Empty;
            vm.Editor.Travel.Cell = cellInput.Text?.Trim() ?? string.Empty;
            vm.Editor.Travel.Pad = padInput.Text?.Trim() ?? string.Empty;

            if (!vm.Editor.Travel.Validate())
            {
                validationMessage.Text = "Name, map, cell and pad are required.";
                return;
            }

            result = true;
            dialog.Close();
        };

        cancel.Click += (_, _) =>
        {
            result = false;
            dialog.Close();
        };

        Grid fields = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            ColumnSpacing = 12,
            RowSpacing = 10
        };

        TextBlock descriptionLabel = new() { Text = "Name", VerticalAlignment = VerticalAlignment.Center };
        TextBlock mapLabel = new() { Text = "Map", VerticalAlignment = VerticalAlignment.Center };
        TextBlock cellLabel = new() { Text = "Cell", VerticalAlignment = VerticalAlignment.Center };
        TextBlock padLabel = new() { Text = "Pad", VerticalAlignment = VerticalAlignment.Center };

        Grid.SetRow(descriptionLabel, 0);
        Grid.SetRow(descriptionInput, 0);
        Grid.SetColumn(descriptionInput, 1);
        Grid.SetRow(mapLabel, 1);
        Grid.SetRow(mapInput, 1);
        Grid.SetColumn(mapInput, 1);
        Grid.SetRow(cellLabel, 2);
        Grid.SetRow(cellInput, 2);
        Grid.SetColumn(cellInput, 1);
        Grid.SetRow(padLabel, 3);
        Grid.SetRow(padInput, 3);
        Grid.SetColumn(padInput, 1);

        fields.Children.Add(descriptionLabel);
        fields.Children.Add(descriptionInput);
        fields.Children.Add(mapLabel);
        fields.Children.Add(mapInput);
        fields.Children.Add(cellLabel);
        fields.Children.Add(cellInput);
        fields.Children.Add(padLabel);
        fields.Children.Add(padInput);

        StackPanel actionButtons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancel, confirm }
        };

        StackPanel content = new()
        {
            Margin = new Thickness(16),
            Spacing = 12,
            Children =
            {
                fields,
                getCurrent,
                validationMessage,
                actionButtons
            }
        };

        dialog.Content = WrapWithShell(string.IsNullOrWhiteSpace(title) ? "Edit Fast Travel" : title, content);
        Show(dialog);
        return result;
    }

    private static bool? ShowSkillRuleEditorDialog(SkillRuleEditorDialogViewModel vm, string title)
    {
        Window dialog = CreateBaseDialog(string.IsNullOrWhiteSpace(title) ? "Edit Rules" : title);
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Width = 640;
        dialog.Height = 600;

        SkillRulesViewModel scratch = new(vm.UseRules);

        PropertyGridControl propertyGrid = new()
        {
            SelectedObject = scratch,
            MinHeight = 350
        };

        TextBlock help = new()
        {
            Text = "Edit rule values then click Confirm. Multi-aura entries can be managed in the main Skills panel.",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        };
        help[!TextBlock.ForegroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaMutedTextBrush");

        Button confirm = new() { Content = "Confirm", MinWidth = 84, Classes = { "skua-button", "skua-primary" } };
        Button cancel = new() { Content = "Cancel", MinWidth = 84, Classes = { "skua-button" }, IsCancel = true };
        confirm.IsDefault = true;

        bool? result = null;
        confirm.Click += (_, _) =>
        {
            CopySkillRules(scratch, vm.UseRules);
            result = true;
            dialog.Close();
        };

        cancel.Click += (_, _) =>
        {
            result = false;
            dialog.Close();
        };

        Grid content = new();
        content.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        content.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        content.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        content.Margin = new Thickness(16);

        Grid.SetRow(help, 0);
        Grid.SetRow(propertyGrid, 1);

        StackPanel buttonPanel = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { cancel, confirm }
        };
        Grid.SetRow(buttonPanel, 2);

        content.Children.Add(help);
        content.Children.Add(propertyGrid);
        content.Children.Add(buttonPanel);

        dialog.Content = WrapWithShell(string.IsNullOrWhiteSpace(title) ? "Edit Rules" : title, content);
        Show(dialog);
        return result;
    }

    private static string GetCurrentAccountName()
    {
        try
        {
            var bot = Ioc.Default.GetService<IScriptInterface>();
            if (bot?.Player != null && !string.IsNullOrWhiteSpace(bot.Player.Username) && bot.Player.Username != "loginInfo.strUsername")
                return bot.Player.Username.Trim();

            if (bot?.Servers != null && !string.IsNullOrWhiteSpace(bot.Servers.CachedUsername))
                return bot.Servers.CachedUsername.Trim();

            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if ((args[i] == "-u" || args[i] == "--user") && !string.IsNullOrWhiteSpace(args[i + 1]))
                    return args[i + 1].Trim();
            }
        }
        catch { }
        return string.Empty;
    }

    private static string ResolveOptionContainerTitle(string? title)
    {
        string accountName = GetCurrentAccountName();
        if (string.IsNullOrWhiteSpace(title) || title == "Script Options" || title == nameof(OptionContainerViewModel))
        {
            return string.IsNullOrWhiteSpace(accountName) ? "Script Options" : $"Script Options - {accountName}";
        }

        if (!string.IsNullOrWhiteSpace(accountName) && !title.Contains(accountName, StringComparison.OrdinalIgnoreCase))
        {
            return $"{title} - {accountName}";
        }

        return title;
    }

    private static bool? ShowOptionContainerDialog(OptionContainerViewModel vm, string title)
    {
        string resolvedTitle = ResolveOptionContainerTitle(title);
        Window dialog = CreateBaseDialog(resolvedTitle);
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Width = 560;
        dialog.MinWidth = 460;
        dialog.MinHeight = 420;
        dialog.CanResize = true;

        double maxScreenHeight = 720;
        try
        {
            var screen = Skua.Shared.Avalonia.Services.WindowPlacementHelper.GetTargetScreen(dialog);
            if (screen != null)
                maxScreenHeight = Math.Max(420, screen.WorkingArea.Height * 0.85);
        }
        catch { }
        dialog.MaxHeight = maxScreenHeight;
        dialog.Height = Math.Min(560, maxScreenHeight);

        OptionContainerUserControl content = new()
        {
            DataContext = vm
        };

        Button saveBtn = new()
        {
            Content = "Save",
            MinWidth = 84,
            Classes = { "skua-button", "skua-primary" },
            IsDefault = true
        };

        Button cancelBtn = new()
        {
            Content = "Cancel",
            MinWidth = 84,
            Classes = { "skua-button" },
            IsCancel = true
        };

        Button resetBtn = new()
        {
            Content = "Reset Defaults",
            Classes = { "skua-button" }
        };

        bool? result = null;

        saveBtn.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };

        cancelBtn.Click += (_, _) =>
        {
            result = false;
            dialog.Close();
        };

        resetBtn.Click += (_, _) =>
        {
            vm.ResetAllToDefaults();
        };

        Grid rootLayout = new();
        rootLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        rootLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        Grid.SetRow(content, 0);
        rootLayout.Children.Add(content);

        Grid buttonPanel = new()
        {
            Margin = new Thickness(12, 0, 12, 12),
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };

        buttonPanel.Children.Add(resetBtn);
        Grid.SetColumn(resetBtn, 0);

        StackPanel actionButtons = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Children = { cancelBtn, saveBtn }
        };
        buttonPanel.Children.Add(actionButtons);
        Grid.SetColumn(actionButtons, 1);

        Grid.SetRow(buttonPanel, 1);
        rootLayout.Children.Add(buttonPanel);

        dialog.Content = WrapWithShell(resolvedTitle, rootLayout);
        Show(dialog);
        return result;
    }

    private static bool? ShowCustomDialog(CustomDialogViewModel vm, string title)
    {
        string clicked = ShowButtonsDialog(title, vm.Message, vm.Buttons.ToArray());
        int index = vm.Buttons.IndexOf(clicked);
        vm.Result = index < 0 ? DialogResult.Cancelled : new DialogResult(clicked, index);
        return index >= 0;
    }

    private static bool? ShowFallbackDialog(string typeName, string title)
    {
        _ = ShowButtonsDialog(title, $"Dialog view: {typeName}", ["OK"]);
        return true;
    }

    private static bool? ShowMessageBoxInternal(string message, string caption, bool yesAndNo)
    {
        string[] buttons = yesAndNo ? ["No", "Yes"] : ["OK"];
        string clicked = ShowButtonsDialog(caption, message, buttons);
        return clicked == "Yes" || clicked == "OK";
    }

    private static string ShowButtonsDialog(string title, string message, string[] buttons)
    {
        Window dialog = CreateBaseDialog(title);
        dialog.Width = (message.Length > 200 || message.Contains('\n')) ? 560 : 460;
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.CanResize = true;

        double maxScreenHeight = 720;
        var screen = Skua.Shared.Avalonia.Services.WindowPlacementHelper.GetTargetScreen(dialog);
        if (screen != null)
            maxScreenHeight = Math.Max(160, screen.WorkingArea.Height / screen.Scaling * 0.85);
        dialog.MinHeight = 160;
        dialog.MaxHeight = maxScreenHeight;

        SelectableTextBlock label = new()
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20
        };
        label.Measure(new Size(dialog.Width - 34, double.PositiveInfinity));
        dialog.Height = Math.Min(maxScreenHeight, Math.Max(dialog.MinHeight, label.DesiredSize.Height + 112));

        ScrollViewer scrollViewer = new()
        {
            Content = label,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        Button copyButton = new()
        {
            Content = "Copy",
            Classes = { "skua-button", "w84" }
        };
        copyButton.Click += async (_, _) =>
        {
            if (dialog.Clipboard != null)
            {
                await dialog.Clipboard.SetTextAsync(message);
                copyButton.Content = "Copied!";
            }
        };

        StackPanel buttonPanel = new()
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };

        string clicked = string.Empty;
        for (int i = 0; i < buttons.Length; i++)
        {
            string text = buttons[i];
            Button button = new()
            {
                Content = text,
                MinWidth = 84,
                Classes = { "skua-button" }
            };

            if (text == "OK" || text == "Yes")
            {
                button.Classes.Add("skua-primary");
                button.IsDefault = true;
            }
            else if (text == "Cancel" || text == "No")
            {
                button.IsCancel = true;
            }

            button.Click += (_, _) =>
            {
                clicked = text;
                dialog.Close();
            };
            buttonPanel.Children.Add(button);
        }

        Grid footerPanel = new()
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*")
        };
        Grid.SetColumn(copyButton, 0);
        Grid.SetColumn(buttonPanel, 1);
        footerPanel.Children.Add(copyButton);
        footerPanel.Children.Add(buttonPanel);

        Grid content = new()
        {
            Margin = new Thickness(16),
            RowDefinitions = new RowDefinitions("*,Auto"),
            RowSpacing = 16
        };
        Grid.SetRow(scrollViewer, 0);
        Grid.SetRow(footerPanel, 1);
        content.Children.Add(scrollViewer);
        content.Children.Add(footerPanel);

        dialog.Content = WrapWithShell(title, content);
        Show(dialog);
        return clicked;
    }

    private static Window CreateBaseDialog(string title)
    {
        Window dialog = new()
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.Manual,
            SystemDecorations = SystemDecorations.BorderOnly,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaChromeHints = global::Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
            ExtendClientAreaTitleBarHeightHint = 32,
            CanResize = false,
            ShowActivated = true,
            Classes = { "skua" }
        };
        dialog[!Window.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaPageBrush");
        Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(dialog);
        return dialog;
    }

    private static Control WrapWithShell(string title, Control content)
    {
        Grid layout = new();
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        layout.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        WindowTitleBar titleBar = new()
        {
            TitleText = title
        };

        Border contentHost = new()
        {
            Child = content
        };
        contentHost.Classes.Add("shell");
        contentHost[!Border.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaPageBrush");

        Grid.SetRow(titleBar, 0);
        Grid.SetRow(contentHost, 1);
        layout.Children.Add(titleBar);
        layout.Children.Add(contentHost);

        Border rootBorder = new()
        {
            BorderThickness = new Thickness(1),
            Child = layout,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 12,
                Spread = 2,
                Color = Color.FromArgb(140, 0, 0, 0)
            })
        };
        rootBorder[!Border.BorderBrushProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaBorderBrush");
        rootBorder[!Border.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaPageBrush");

        return rootBorder;
    }

    // initialFocus: the control that takes focus when the dialog opens (e.g. an input box); otherwise the default button.
    private static void Show(Window dialog, Control? initialFocus = null)
    {
        Skua.Shared.Avalonia.Services.PointerCaptureReset.Release();
        Skua.Shared.Avalonia.Services.WindowPlacementHelper.CenterOnActiveScreen(dialog);

        Window? owner = GetOwner();
        if (owner != null)
        {
            ShowModalAndBlock(dialog, owner, initialFocus);
            return;
        }

        ShowAndBlock(dialog, initialFocus);
    }

    private static Window? GetOwner()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        Window? activeVisible = desktop.Windows.FirstOrDefault(w => w.IsVisible && w.IsActive && w.Position.X > -10000);
        if (activeVisible != null)
            return activeVisible;

        Window? visible = desktop.Windows.LastOrDefault(w => w.IsVisible && w.Position.X > -10000);
        if (visible != null)
            return visible;

        return (desktop.MainWindow?.IsVisible == true && desktop.MainWindow.Position.X > -10000) ? desktop.MainWindow : null;
    }

    private static void ShowModalAndBlock(Window dialog, Window owner, Control? initialFocus)
    {
        using CancellationTokenSource closedCts = new();
        void OnClosed(object? _, EventArgs __) => closedCts.Cancel();
        void OnOpened(object? _, EventArgs __) => FocusDefaultDialogAction(dialog, initialFocus);

        dialog.Closed += OnClosed;
        dialog.Opened += OnOpened;
        _ = dialog.ShowDialog(owner);

        try
        {
            Dispatcher.UIThread.MainLoop(closedCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when the dialog closes and cancels the nested loop.
        }
        finally
        {
            dialog.Closed -= OnClosed;
            dialog.Opened -= OnOpened;
        }
    }

    private static void ShowAndBlock(Window dialog, Control? initialFocus)
    {
        using CancellationTokenSource closedCts = new();
        void OnClosed(object? _, EventArgs __) => closedCts.Cancel();
        void OnOpened(object? _, EventArgs __) => FocusDefaultDialogAction(dialog, initialFocus);

        dialog.Closed += OnClosed;
        dialog.Opened += OnOpened;
        dialog.Show();

        try
        {
            Dispatcher.UIThread.MainLoop(closedCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected when the dialog closes and cancels the nested loop.
        }
        finally
        {
            dialog.Closed -= OnClosed;
            dialog.Opened -= OnOpened;
        }
    }

    private static void FocusDefaultDialogAction(Window dialog, Control? initialFocus)
    {
        dialog.Activate();
        // In the tab host, keep the tabs' game windows below the dialog so it is never hidden behind the game.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: Views.TabbedHostWindow host })
            host.KeepGameWindowsBelowHostWindows();
        dialog.Focus();
        if (initialFocus?.Focus() == true)
        {
            // Select any prefilled text (e.g. the last quest ID) so typing replaces it.
            (initialFocus as TextBox)?.SelectAll();
            return;
        }

        dialog.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.IsDefault)?
            .Focus();
    }

    private static void CopySkillRules(SkillRulesViewModel source, SkillRulesViewModel target)
    {
        target.UseRuleBool = source.UseRuleBool;
        target.MultiAuraBool = source.MultiAuraBool;
        target.HealthGreaterThanBool = source.HealthGreaterThanBool;
        target.HealthUseValue = source.HealthUseValue;
        target.HealthIsPercentage = source.HealthIsPercentage;
        target.ManaGreaterThanBool = source.ManaGreaterThanBool;
        target.ManaUseValue = source.ManaUseValue;
        target.ManaIsPercentage = source.ManaIsPercentage;
        target.WaitUseValue = source.WaitUseValue;
        target.SkipUseBool = source.SkipUseBool;
        target.AuraGreaterThanBool = source.AuraGreaterThanBool;
        target.AuraUseValue = source.AuraUseValue;
        target.AuraTargetIndex = source.AuraTargetIndex;
        target.AuraName = source.AuraName;
        target.PartyMemberHealthGreaterThanBool = source.PartyMemberHealthGreaterThanBool;
        target.PartyMemberHealthUseValue = source.PartyMemberHealthUseValue;
        target.PartyMemberHealthIsPercentage = source.PartyMemberHealthIsPercentage;
        target.MultiAuraOperatorIndex = source.MultiAuraOperatorIndex;

        target.MultiAuraChecks.Clear();
        foreach (AuraCheckViewModel check in source.MultiAuraChecks)
            target.MultiAuraChecks.Add(new AuraCheckViewModel(check));
    }
}
