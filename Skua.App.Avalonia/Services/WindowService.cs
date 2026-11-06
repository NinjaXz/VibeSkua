using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.Converters;
using Avalonia.Media;
using Skua.Core.Interfaces;
using Skua.Shared.Avalonia.Controls.Shell;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Skua.App.Avalonia.Services;

public class WindowService : IWindowService
{
    private readonly Dictionary<string, Window> _managedWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<IManagedWindow>> _managedWindowFactories = new(StringComparer.OrdinalIgnoreCase);

    // One window per view model type for the resolve-from-container overloads: asking again brings the
    // open window to the front instead of stacking a duplicate.
    private readonly Dictionary<Type, Window> _typeWindows = new();

    public void ShowWindow<TViewModel>(int width, int height) where TViewModel : class
    {
        ShowSingleWindow<TViewModel>(width, height);
    }

    public void ShowWindow<TViewModel>() where TViewModel : class
    {
        ShowSingleWindow<TViewModel>(900, 600);
    }

    public void ShowWindow<TViewModel>(TViewModel viewModel) where TViewModel : class
    {
        Window window = CreateHostWindow(viewModel, GetWindowTitle(viewModel), 900, 600);
        window.Show();
    }

    private void ShowSingleWindow<TViewModel>(int width, int height) where TViewModel : class
    {
        if (_typeWindows.TryGetValue(typeof(TViewModel), out Window? open))
        {
            if (open.WindowState == WindowState.Minimized)
                open.WindowState = WindowState.Normal;
            open.Activate();
            return;
        }

        TViewModel vm = Ioc.Default.GetRequiredService<TViewModel>();
        Window window = CreateHostWindow(vm, GetWindowTitle(vm), width, height);
        _typeWindows[typeof(TViewModel)] = window;
        window.Closed += (_, _) => _typeWindows.Remove(typeof(TViewModel));
        window.Show();
    }

    private static string GetWindowTitle(object viewModel)
    {
        if (viewModel is IManagedWindow managed && !string.IsNullOrWhiteSpace(managed.Title))
            return managed.Title;
        if (viewModel is Skua.Shared.Avalonia.ViewModels.BotControlViewModelBase control && !string.IsNullOrWhiteSpace(control.Title))
            return control.Title;

        // "BotWindowViewModel" -> "Bot Window"
        string name = viewModel.GetType().Name;
        if (name.EndsWith("ViewModel", StringComparison.Ordinal))
            name = name[..^"ViewModel".Length];
        return System.Text.RegularExpressions.Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " ");
    }

    public void ShowManagedWindow(string key)
    {
        if (!_managedWindows.TryGetValue(key, out Window? window))
        {
            if (_managedWindowFactories.TryGetValue(key, out var factory))
            {
                var vm = factory();
                window = CreateHostWindow(vm, vm.Title, vm.Width, vm.Height);
                window.CanResize = vm.CanResize;
                window.Closing += (_, e) =>
                {
                    e.Cancel = true;
                    window.Hide();
                    if (window.DataContext is ObservableRecipient recipient)
                        recipient.IsActive = false;
                };
                _managedWindows[key] = window;
            }
            else
            {
                return;
            }
        }

        if (!window.IsVisible)
            window.Show();
        window.Activate();

        if (window.DataContext is ObservableRecipient r)
            r.IsActive = true;
    }

    public void RegisterManagedWindow<TViewModel>(string key, TViewModel viewModel) where TViewModel : class, IManagedWindow
    {
        if (_managedWindows.ContainsKey(key))
            return;

        Window window = CreateHostWindow(viewModel, viewModel.Title, viewModel.Width, viewModel.Height);
        window.CanResize = viewModel.CanResize;
        window.Closing += (_, e) =>
        {
            e.Cancel = true;
            window.Hide();
            if (window.DataContext is ObservableRecipient recipient)
                recipient.IsActive = false;
        };
        _managedWindows[key] = window;
    }

    public void RegisterManagedWindow<TViewModel>(string key) where TViewModel : class, IManagedWindow
    {
        _managedWindowFactories[key] = () => Ioc.Default.GetRequiredService<TViewModel>();
    }

    public void RegisterManagedWindow(string key, Func<IManagedWindow> factory)
    {
        _managedWindowFactories[key] = factory;
    }

    private static Window CreateHostWindow(object viewModel, string title, double width, double height)
    {
        bool hasTemplate = Application.Current?.DataTemplates.Any(t => t.Match(viewModel)) == true;
        SizeToContent sizeToContent = SizeToContent.Manual;

        if (width <= 0 && height <= 0)
            sizeToContent = SizeToContent.WidthAndHeight;
        else if (width <= 0)
            sizeToContent = SizeToContent.Width;
        else if (height <= 0)
            sizeToContent = SizeToContent.Height;

        WindowStartupLocation startupLoc = WindowStartupLocation.Manual;
        PixelPoint? initialPos = null;

        if (viewModel is IManagedWindow managed && managed.X.HasValue && managed.Y.HasValue)
        {
            initialPos = new PixelPoint(managed.X.Value, managed.Y.Value);
        }

        Window window = new()
        {
            Title = title,
            SystemDecorations = SystemDecorations.BorderOnly,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaChromeHints = global::Avalonia.Platform.ExtendClientAreaChromeHints.NoChrome,
            ExtendClientAreaTitleBarHeightHint = 32,
            WindowStartupLocation = startupLoc,
            SizeToContent = sizeToContent,
            DataContext = viewModel,
            Content = CreateShellContent(
                title,
                hasTemplate
                    ? new ContentControl
                    {
                        Content = viewModel,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch,
                        VerticalContentAlignment = VerticalAlignment.Stretch
                    }
                    : CreateFallbackContent(title))
        };
        window.Classes.Add("skua");
        window[!Window.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaPageBrush");
        Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(window);

        if (width > 0)
        {
            window.Width = width;
            window.MinWidth = Math.Min(width, 400);
        }
        if (height > 0)
        {
            window.Height = height;
            window.MinHeight = Math.Min(height, 300);
        }

        Skua.Shared.Avalonia.Services.WindowPlacementHelper.FitToWorkingArea(window);

        // Size first, then place: centring a window whose size is still unknown puts it in the wrong spot
        // and it visibly jumps once Opened corrects it.
        if (initialPos.HasValue)
        {
            window.Position = initialPos.Value;
            window.Opened += (s, e) =>
            {
                window.Position = initialPos.Value;
            };
        }
        else
        {
            Skua.Shared.Avalonia.Services.WindowPlacementHelper.CenterOnActiveScreen(window);
        }

        return window;
    }

    private static Control CreateShellContent(string title, Control content)
    {
        Border contentHost = new()
        {
            Child = content,
            ClipToBounds = true
        };
        contentHost.Classes.Add("shell");

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        WindowTitleBar titleBar = new()
        {
            TitleText = title
        };
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(contentHost, 1);
        root.Children.Add(titleBar);
        root.Children.Add(contentHost);

        Border rootBorder = new()
        {
            BorderThickness = new Thickness(1),
            Child = root,
            BoxShadow = new BoxShadows(new BoxShadow
            {
                Blur = 24,
                Spread = 4,
                Color = Color.FromArgb(180, 0, 0, 0)
            })
        };
        rootBorder[!Border.BorderBrushProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaBorderBrush");
        rootBorder[!Border.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaPageBrush");

        return rootBorder;
    }

    private static Control CreateFallbackContent(string title) =>
        new Border
        {
            Padding = new Thickness(16),
            Child = new TextBlock
            {
                Text = title,
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left
            }
        };
}
