using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Skua.Shared.Avalonia.Controls.Shell;

namespace ButlerV5;

internal static class AvaloniaUi
{
    public static readonly IBrush Background = Brush("#1B1B1F");
    public static readonly IBrush Panel = Brush("#28282E");
    public static readonly IBrush PanelRaised = Brush("#303038");
    public static readonly IBrush Text = Brush("#F0F0F3");
    public static readonly IBrush SubText = Brush("#A7A7B2");
    public static readonly IBrush Divider = Brush("#41414B");
    public static readonly IBrush Accent = Brush("#4F8CFF");
    public static readonly IBrush Green = Brush("#3FBF6F");
    public static readonly IBrush Orange = Brush("#FFA23E");
    public static readonly IBrush OrangeButton = Brush("#E8862D");
    public static readonly IBrush Red = Brush("#E05252");
    public static readonly IBrush White = Brushes.White;

    public static SolidColorBrush Brush(string color) => new(Color.Parse(color));

    public static void PrepareWindow(Window window)
    {
        window.Background = Background;
        window.Classes.Add("skua");
        // Use VibeSkua's own title bar instead of the OS one, like the client's other windows.
        window.SystemDecorations = SystemDecorations.BorderOnly;
        window.ExtendClientAreaToDecorationsHint = true;
        window.ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.NoChrome;
        window.ExtendClientAreaTitleBarHeightHint = 32;
    }

    /// <summary>Sets the window content under a VibeSkua title bar showing the window's title.</summary>
    public static void SetContent(Window window, Control content)
    {
        Grid root = new() { RowDefinitions = new RowDefinitions("Auto,*") };
        root.Children.Add(new WindowTitleBar { TitleText = window.Title ?? "ButlerV5" });
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        Border frame = new() { BorderThickness = new Thickness(1), Child = root };
        frame.Bind(Border.BorderBrushProperty, frame.GetResourceObservable("SkuaBorderBrush"));
        window.Content = frame;
    }

    public static TextBlock TextBlock(double size, IBrush? foreground = null) => new()
    {
        FontSize = size,
        Foreground = foreground ?? Text,
    };

    public static Button Button(string text, Action onClick, IBrush? color = null)
    {
        Button button = new()
        {
            Content = text,
            FontSize = 12,
            Padding = new Thickness(12, 5),
            MinHeight = 30,
        };
        button.Classes.Add("skua-button");
        if (color != null)
        {
            button.Background = color;
            button.BorderBrush = color;
            button.Foreground = White;
            // The shared skua-button hover/pressed styles repaint the presenter grey; keep the colour and dim it instead.
            button.Styles.Add(ColouredState(":pointerover", color, 0.85));
            button.Styles.Add(ColouredState(":pressed", color, 0.7));
        }
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Style ColouredState(string pseudoClass, IBrush color, double opacity) => new(x => x.OfType<Button>().Class(pseudoClass).Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
    {
        Setters =
        {
            new Setter(ContentPresenter.BackgroundProperty, color),
            new Setter(ContentPresenter.BorderBrushProperty, color),
            new Setter(ContentPresenter.ForegroundProperty, White),
            new Setter(Visual.OpacityProperty, opacity)
        }
    };

    public static void SetToolTip(Control control, string text) => ToolTip.SetTip(control, text);
}
