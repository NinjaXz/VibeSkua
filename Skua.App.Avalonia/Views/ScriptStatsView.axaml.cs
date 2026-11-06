using Avalonia;
using Avalonia.Controls;
using Skua.App.Avalonia.ViewModels;
using System;
using System.ComponentModel;

namespace Skua.App.Avalonia.Views;

public partial class ScriptStatsView : UserControl
{
    // Advanced mode gets a fixed height so the rate history fills the window (and can grow with it); without it
    // the window sizes to the compact counters. The window service caps both at the screen's working area.
    private const double AdvancedHeight = 760;

    private ScriptStatsViewModel? _vm;

    public ScriptStatsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Watch(DataContext as ScriptStatsViewModel);
        AttachedToVisualTree += (_, _) =>
        {
            Watch(DataContext as ScriptStatsViewModel);
            ApplyWindowSize();
        };
        DetachedFromVisualTree += (_, _) => Watch(null);
    }

    private void Watch(ScriptStatsViewModel? vm)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = vm;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptStatsViewModel.AdvancedMode))
            ApplyWindowSize();
    }

    private void ApplyWindowSize()
    {
        if (_vm is null || TopLevel.GetTopLevel(this) is not Window window)
            return;

        if (_vm.AdvancedMode)
        {
            window.SizeToContent = SizeToContent.Manual;
            window.Height = Math.Min(AdvancedHeight, window.MaxHeight);
        }
        else
        {
            window.SizeToContent = SizeToContent.Height;
        }
    }
}
