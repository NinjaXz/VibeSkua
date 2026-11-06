using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Skua.Core.Interfaces;
using Skua.Core.Interfaces.ViewModels;
using Skua.Core.Models;
using Skua.Manager.Avalonia.ViewModels;
using Skua.Shared.Avalonia.Controls.Options;
using Skua.Shared.Avalonia.Controls.Shell;
using Skua.Shared.Avalonia.ViewModels.Options;
using Skua.Shared.Avalonia.ViewModels.Dialogs;
using System;
using System.Linq;
using System.Threading;
using CustomDialogViewModel = Skua.Shared.Avalonia.ViewModels.Dialogs.CustomDialogViewModel;

namespace Skua.Manager.Avalonia.Services;

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

    private bool? ShowDialogInternal<TViewModel>(TViewModel viewModel, string title) where TViewModel : class
    {
        return viewModel switch
        {
            OptionContainerViewModel options => ShowOptionContainerDialog(options, string.IsNullOrWhiteSpace(title) || title == nameof(OptionContainerViewModel) ? options.Title : title),
            IOptionContainerViewModel optionsVm => ShowOptionContainerDialog(optionsVm as OptionContainerViewModel ?? new OptionContainerViewModel(optionsVm.Container), title),
            AccountEditorViewModel accountEditor => ShowAccountEditorDialog(accountEditor, title),
            InputDialogViewModel input => ShowInputDialog(input, title),
            SelectGroupDialogViewModel selectGroup => ShowSelectGroupDialog(selectGroup, title),
            SelectAccountDialogViewModel selectAccount => ShowSelectAccountDialog(selectAccount, title),
            MessageBoxDialogViewModel messageBox => ShowMessageBox(messageBox.Message, messageBox.Title, messageBox.YesAndNo),
            CustomDialogViewModel custom => ShowCustomDialog(custom, title),
            _ => ShowFallbackDialog(viewModel, title)
        };
    }

    private static bool? ShowAccountEditorDialog(AccountEditorViewModel vm, string title)
    {
        Window dialog = CreateBaseDialog(title);
        dialog.Width = 440;

        TextBox usernameBox = new()
        {
            Text = vm.Username,
            Watermark = "Username",
            Margin = new Thickness(0, 0, 0, 10)
        };

        TextBox displayNameBox = new()
        {
            Text = vm.DisplayName,
            Watermark = "Display Name (optional)",
            Margin = new Thickness(0, 0, 0, 10)
        };

        TextBox passwordBox = new()
        {
            Text = vm.Password,
            Watermark = "Password",
            PasswordChar = '*',
            Padding = new Thickness(8, 6, 34, 6)
        };

        Material.Icons.Avalonia.MaterialIcon eyeIcon = new()
        {
            Kind = Material.Icons.MaterialIconKind.EyeOutline,
            Width = 16,
            Height = 16
        };

        Button eyeButton = new()
        {
            Content = eyeIcon,
            Classes = { "icon" },
            Width = 28,
            Height = 28,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0)
        };
        ToolTip.SetTip(eyeButton, "Show/Hide Password");

        bool isPswVisible = false;
        eyeButton.Click += (_, _) =>
        {
            isPswVisible = !isPswVisible;
            passwordBox.PasswordChar = isPswVisible ? '\0' : '*';
            eyeIcon.Kind = isPswVisible ? Material.Icons.MaterialIconKind.EyeOffOutline : Material.Icons.MaterialIconKind.EyeOutline;
        };

        Grid passwordContainer = new()
        {
            Margin = new Thickness(0, 0, 0, 10),
            Children = { passwordBox, eyeButton }
        };

        TextBox tagsBox = new()
        {
            Text = vm.Tags,
            Watermark = "Tags (comma-separated, e.g. farm, daily)",
            Margin = new Thickness(0, 0, 0, 16)
        };

        bool? result = null;
        Button ok = new() { Content = "Save", MinWidth = 84, Classes = { "skua-button", "skua-primary" } };
        Button cancel = new() { Content = "Cancel", MinWidth = 84, Classes = { "skua-button" }, IsCancel = true };
        ok.IsDefault = true;

        ok.Click += (_, _) =>
        {
            vm.Username = usernameBox.Text ?? string.Empty;
            vm.DisplayName = displayNameBox.Text ?? string.Empty;
            vm.Password = passwordBox.Text ?? string.Empty;
            vm.Tags = tagsBox.Text ?? string.Empty;
            result = true;
            dialog.Close();
        };

        cancel.Click += (_, _) =>
        {
            result = false;
            dialog.Close();
        };

        StackPanel formPanel = new()
        {
            Margin = new Thickness(16),
            Children =
            {
                new TextBlock { Text = "Username", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4), FontSize = 12 },
                usernameBox,
                new TextBlock { Text = "Display Name", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4), FontSize = 12 },
                displayNameBox,
                new TextBlock { Text = "Password", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4), FontSize = 12 },
                passwordContainer,
                new TextBlock { Text = "Tags", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 4), FontSize = 12 },
                tagsBox,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, ok }
                }
            }
        };

        dialog.Content = WrapWithShell(title, formPanel);
        Show(dialog);
        return result;
    }

    private static bool? ShowInputDialog(InputDialogViewModel vm, string title)
    {
        Window dialog = CreateBaseDialog(title);
        TextBox input = new() { Text = vm.DialogTextInput, Watermark = vm.TextBoxHint };
        if (vm.NumberOnly)
            input.TextChanged += (_, _) => input.Text = new string((input.Text ?? string.Empty).Where(char.IsDigit).ToArray());

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
            Margin = new Thickness(14),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = vm.DialogHint },
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

    private static bool? ShowSelectGroupDialog(SelectGroupDialogViewModel vm, string title)
    {
        Window dialog = CreateBaseDialog(title);
        ComboBox groups = new()
        {
            ItemsSource = vm.Groups,
            SelectedItem = vm.SelectedGroup,
            ItemTemplate = new FuncDataTemplate<GroupItemViewModel>((g, _) =>
                new TextBlock { Text = g?.Name ?? string.Empty }, true)
        };

        bool? result = null;
        Button ok = new() { Content = "OK", MinWidth = 84, Classes = { "skua-button", "skua-primary" } };
        Button cancel = new() { Content = "Cancel", MinWidth = 84, Classes = { "skua-button" }, IsCancel = true };
        ok.IsDefault = true;
        ok.Click += (_, _) =>
        {
            vm.SelectedGroup = groups.SelectedItem as GroupItemViewModel;
            result = vm.SelectedGroup != null;
            dialog.Close();
        };
        cancel.Click += (_, _) =>
        {
            result = false;
            dialog.Close();
        };

        StackPanel content = new()
        {
            Margin = new Thickness(14),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Select group" },
                groups,
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
        Show(dialog);
        return result;
    }

    private static bool? ShowSelectAccountDialog(SelectAccountDialogViewModel vm, string title)
    {
        Window dialog = CreateBaseDialog(title);
        TextBox searchBox = new()
        {
            Watermark = "Search accounts...",
            Text = vm.SearchText
        };
        searchBox.TextChanged += (_, _) =>
        {
            vm.SearchText = searchBox.Text ?? string.Empty;
        };

        ListBox accounts = new()
        {
            MaxHeight = 260,
            ItemsSource = vm.FilteredAccounts,
            SelectedItem = vm.SelectedAccount,
            ItemTemplate = new FuncDataTemplate<IAccountItemViewModel>((a, _) =>
            {
                return new TextBlock { Text = a?.DisplayOrUsername ?? string.Empty };
            }, true)
        };
        accounts.SelectionChanged += (_, _) =>
        {
            vm.SelectedAccount = accounts.SelectedItem as IAccountItemViewModel;
        };

        bool? result = null;
        Button ok = new() { Content = "OK", MinWidth = 84, Classes = { "skua-button", "skua-primary" } };
        Button cancel = new() { Content = "Cancel", MinWidth = 84, Classes = { "skua-button" }, IsCancel = true };
        ok.IsDefault = true;
        ok.Click += (_, _) =>
        {
            result = vm.SelectedAccount != null;
            dialog.Close();
        };
        cancel.Click += (_, _) =>
        {
            result = false;
            dialog.Close();
        };

        StackPanel content = new()
        {
            Margin = new Thickness(14),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Select replacement account" },
                searchBox,
                accounts,
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
        Show(dialog);
        return result;
    }

    private static bool? ShowOptionContainerDialog(OptionContainerViewModel vm, string title)
    {
        Window dialog = CreateBaseDialog(string.IsNullOrWhiteSpace(title) ? "Script Options" : title);
        dialog.SizeToContent = SizeToContent.Manual;
        dialog.Width = 560;
        dialog.MinWidth = 460;
        dialog.MinHeight = 420;
        dialog.CanResize = true;

        double maxScreenHeight = 720;
        try
        {
            var screen = dialog.Screens?.Primary ?? dialog.Screens?.All?.FirstOrDefault();
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

        dialog.Content = WrapWithShell(string.IsNullOrWhiteSpace(title) ? "Script Options" : title, rootLayout);
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

    private static bool? ShowFallbackDialog<TViewModel>(TViewModel vm, string title) where TViewModel : class
    {
        Window dialog = CreateBaseDialog(title);
        bool? result = null;
        Button ok = new() { Content = "OK", MinWidth = 84, Classes = { "skua-button", "skua-primary" } };
        ok.IsDefault = true;
        ok.Click += (_, _) =>
        {
            result = true;
            dialog.Close();
        };
        StackPanel content = new()
        {
            Margin = new Thickness(14),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = $"Dialog view not ported yet: {vm.GetType().Name}" },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { ok }
                }
            }
        };
        dialog.Content = WrapWithShell(title, content);
        Show(dialog);
        return result;
    }

    private static string ShowButtonsDialog(string title, string message, string[] buttons)
    {
        Window dialog = CreateBaseDialog(title);
        dialog.Width = (message.Length > 200 || message.Contains('\n')) ? 560 : 420;

        string clicked = string.Empty;
        int index = 0;
        StackPanel row = new()
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Spacing = 8
        };
        foreach (string buttonText in buttons)
        {
            Button button = new() { Content = buttonText, HorizontalAlignment = HorizontalAlignment.Stretch, Classes = { "skua-button" } };
            // Same rules as the client's message boxes: OK/Yes is the highlighted default (Enter), No/Cancel answers Esc.
            if (buttonText is "OK" or "Yes" || (index == 0 && !buttons.Contains("OK") && !buttons.Contains("Yes")))
            {
                button.Classes.Add("skua-primary");
                button.IsDefault = true;
            }
            else if (buttonText is "No" or "Cancel")
            {
                button.IsCancel = true;
            }
            button.Click += (_, _) =>
            {
                clicked = buttonText;
                dialog.Close();
            };
            row.Children.Add(button);
            index++;
        }

        Button copyButton = new()
        {
            Content = "Copy",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Classes = { "skua-button" }
        };
        copyButton.Click += async (_, _) =>
        {
            if (dialog.Clipboard != null)
            {
                await dialog.Clipboard.SetTextAsync(message);
                copyButton.Content = "Copied!";
            }
        };
        row.Children.Add(copyButton);

        SelectableTextBlock label = new()
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap
        };

        ScrollViewer scrollViewer = new()
        {
            Content = label,
            MaxHeight = 350,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };

        StackPanel content = new()
        {
            Margin = new Thickness(14),
            Spacing = 10,
            Children =
            {
                scrollViewer,
                row
            }
        };
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
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            SystemDecorations = SystemDecorations.BorderOnly,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaChromeHints = global::Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
            ExtendClientAreaTitleBarHeightHint = 32,
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

        Window? activeVisible = desktop.Windows.FirstOrDefault(w => w.IsVisible && w.IsActive);
        if (activeVisible != null)
            return activeVisible;

        Window? visible = desktop.Windows.LastOrDefault(w => w.IsVisible);
        if (visible != null)
            return visible;

        return desktop.MainWindow?.IsVisible == true ? desktop.MainWindow : null;
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
        dialog.Focus();
        if (initialFocus?.Focus() == true)
        {
            // Select any prefilled text so typing replaces it.
            (initialFocus as TextBox)?.SelectAll();
            return;
        }

        dialog.GetVisualDescendants()
            .OfType<Button>()
            .FirstOrDefault(b => b.IsDefault)?
            .Focus();
    }
}
