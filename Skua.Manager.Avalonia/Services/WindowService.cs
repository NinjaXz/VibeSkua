using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using Skua.Core.Interfaces;
using Skua.Shared.Avalonia.Controls.Shell;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Skua.Manager.Avalonia.Services;

public class WindowService : IWindowService
{
    private readonly Dictionary<string, Window> _managedWindows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<IManagedWindow>> _managedWindowFactories = new(StringComparer.OrdinalIgnoreCase);

    public void ShowWindow<TViewModel>(int width, int height) where TViewModel : class
    {
        TViewModel vm = Ioc.Default.GetRequiredService<TViewModel>();
        Window window = CreateHostWindow(vm, typeof(TViewModel).Name, width, height);
        window.Show();
    }

    public void ShowWindow<TViewModel>() where TViewModel : class
    {
        TViewModel vm = Ioc.Default.GetRequiredService<TViewModel>();
        Window window = CreateHostWindow(vm, typeof(TViewModel).Name, 900, 600);
        window.Show();
    }

    public void ShowWindow<TViewModel>(TViewModel viewModel) where TViewModel : class
    {
        Window window = CreateHostWindow(viewModel, typeof(TViewModel).Name, 900, 600);
        window.Show();
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

        Window window = new()
        {
            Title = title,
            WindowDecorations = WindowDecorations.BorderOnly,
            ExtendClientAreaToDecorationsHint = true,
            ExtendClientAreaTitleBarHeightHint = 32,
            Width = width,
            Height = height,
            WindowStartupLocation = WindowStartupLocation.Manual,
            DataContext = viewModel,
            Content = CreateShellContent(
                title,
                hasTemplate
                    ? new ContentControl { Content = viewModel }
                    : CreateFallbackContent(title))
        };
        window.Classes.Add("skua");
        window[!Window.BackgroundProperty] = new global::Avalonia.Markup.Xaml.MarkupExtensions.DynamicResourceExtension("SkuaPageBrush");
        Skua.Shared.Avalonia.Services.ThemeResourceApplicator.ApplyTitleBarTheme(window);
        Skua.Shared.Avalonia.Services.WindowPlacementHelper.CenterOnActiveScreen(window);

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
