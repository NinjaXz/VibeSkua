using AnimatedImage.Avalonia;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Material.Icons;
using Material.Icons.Avalonia;
using System;

namespace Skua.Shared.Avalonia.Controls.Shell;

public partial class WindowTitleBar : UserControl
{
    public static readonly StyledProperty<string> TitleTextProperty =
        AvaloniaProperty.Register<WindowTitleBar, string>(nameof(TitleText), "VibeSkua");

    private static string _defaultIconUri = "avares://Skua.Shared.Avalonia/Assets/SkuaShellImage.png";
    private static IImage? _defaultIconSource;
    private static string? _artworkPath;
    private Bitmap? _artworkBitmap;
    private bool _usesInstanceArtwork;

    public static event Action<string?>? ArtworkPathChanged;

    public static void SetArtworkPath(string? path)
    {
        _artworkPath = string.IsNullOrWhiteSpace(path) ? null : path;
        ArtworkPathChanged?.Invoke(_artworkPath);
    }

    /// <summary>
    /// Sets the icon title bars show when none is given, so each app can use its own. Title bars created
    /// before the call keep the old one.
    /// </summary>
    public static void SetDefaultIcon(string avaresUri)
    {
        _defaultIconUri = avaresUri;
        _defaultIconSource = null;
    }

    public void SetInstanceArtworkPath(string? path)
    {
        _usesInstanceArtwork = true;
        LoadArtworkPath(path);
    }

    public static readonly StyledProperty<IImage?> IconSourceProperty =
        AvaloniaProperty.Register<WindowTitleBar, IImage?>(nameof(IconSource));

    public static readonly StyledProperty<bool> UseAccentBackgroundProperty =
        AvaloniaProperty.Register<WindowTitleBar, bool>(nameof(UseAccentBackground), false);

    public bool UseAccentBackground
    {
        get => GetValue(UseAccentBackgroundProperty);
        set => SetValue(UseAccentBackgroundProperty, value);
    }

    public string TitleText
    {
        get => GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public IImage? IconSource
    {
        get => GetValue(IconSourceProperty);
        set => SetValue(IconSourceProperty, value);
    }

    public WindowTitleBar()
    {
        InitializeComponent();
        SetCurrentValue(IconSourceProperty, _defaultIconSource ??= LoadDefaultIconSource());
        PropertyChanged += (_, e) =>
        {
            if (e.Property == UseAccentBackgroundProperty)
                ApplyAccentForeground();
        };
        Loaded += (_, _) =>
        {
            ArtworkPathChanged -= ApplyArtworkPath;
            ArtworkPathChanged += ApplyArtworkPath;
            ApplyArtworkPath(_artworkPath);
            ApplyAccentForeground();
        };
        Unloaded += (_, _) => ArtworkPathChanged -= ApplyArtworkPath;
        PointerPressed += OnTitleBarPointerPressed;
        MinimizeButton.Click += OnMinimizeClicked;
        MaximizeButton.Click += OnMaximizeClicked;
        CloseButton.Click += OnCloseClicked;
        Loaded += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is not Window window)
                return;

            if (!window.CanResize)
                MaximizeButton.IsVisible = false;

            UpdateMaximizeGlyph(window.WindowState);
            window.PropertyChanged += OnWindowPropertyChanged;
            if (window.CanResize)
                MaximizedResizeGuard.Attach(window);
        };
        Unloaded += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is Window window)
            {
                window.PropertyChanged -= OnWindowPropertyChanged;
                MaximizedResizeGuard.Detach(window);
            }
        };
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty && e.NewValue is WindowState state)
            UpdateMaximizeGlyph(state);
    }

    private void UpdateMaximizeGlyph(WindowState state)
    {
        bool maximized = state == WindowState.Maximized;
        MaximizeButton.Content = new MaterialIcon
        {
            Kind = maximized ? MaterialIconKind.WindowRestore : MaterialIconKind.WindowMaximize,
            Width = 12,
            Height = 12
        };
        ToolTip.SetTip(MaximizeButton, maximized ? "Restore" : "Maximize");
    }

    private void ApplyAccentForeground()
    {
        string resourceKey = UseAccentBackground ? "SkuaAccentForegroundBrush" : "SkuaAccentBrush";
        if (!TryGetResource(resourceKey, ActualThemeVariant, out object? resource) || resource is not IBrush brush)
            return;

        TitleTextBlock.Foreground = brush;
        MinimizeButton.Foreground = brush;
        MaximizeButton.Foreground = brush;
        CloseButton.Foreground = brush;
    }

    private void ApplyArtworkPath(string? path)
    {
        if (_usesInstanceArtwork)
            return;

        LoadArtworkPath(path);
    }

    private void LoadArtworkPath(string? path)
    {
        ArtworkImage.ClearValue(ImageBehavior.AnimatedSourceProperty);
        _artworkBitmap?.Dispose();
        _artworkBitmap = null;

        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            ArtworkImage.Source = null;
            return;
        }

        try
        {
            if (string.Equals(System.IO.Path.GetExtension(path), ".gif", StringComparison.OrdinalIgnoreCase))
            {
                ArtworkImage.Source = null;
                ImageBehavior.SetAnimatedSource(ArtworkImage, new Uri(path, UriKind.Absolute));
                return;
            }

            _artworkBitmap = new Bitmap(path);
            ArtworkImage.Source = _artworkBitmap;
        }
        catch (Exception)
        {
            ArtworkImage.ClearValue(ImageBehavior.AnimatedSourceProperty);
            _artworkBitmap?.Dispose();
            _artworkBitmap = null;
            ArtworkImage.Source = null;
            // Keep the normal themed title bar if the selected image cannot be loaded.
        }
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || TopLevel.GetTopLevel(this) is not Window window)
            return;

        if (e.ClickCount == 2)
        {
            if (window.CanResize && MaximizeButton.IsVisible)
                window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            e.Handled = true;
            return;
        }

        window.BeginMoveDrag(e);
    }

    private void OnMinimizeClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
            window.WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window window)
            return;

        window.WindowState = window.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
            window.Close();
    }

    private static IImage LoadDefaultIconSource()
    {
        // Keep shell icon parity with WPF title bar artwork so badge tint behavior matches.
        using var stream = AssetLoader.Open(new Uri(_defaultIconUri));
        // The source is 2000x2000 but drawn at 30 DIP; decoding at full size and letting the renderer
        // shrink it 60x+ aliases badly, so pre-scale (128 px covers up to 400% DPI).
        return Bitmap.DecodeToWidth(stream, 128, BitmapInterpolationMode.HighQuality);
    }
}