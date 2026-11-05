using Avalonia;
using Avalonia.Controls;

namespace ButlerV5;

/// <summary>
/// Two children: the first sits on the left, the second on the right, on one row when both fit. When they don't,
/// the first goes on its own line above the second, which gets the full width (a WrapPanel then wraps itself).
/// </summary>
internal sealed class FooterBar : Panel
{
    private const double Gap = 10;
    private bool _oneRow = true;

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count < 2)
            return base.MeasureOverride(availableSize);

        Control left = Children[0];
        Control right = Children[1];
        right.Measure(availableSize);
        left.Measure(new Size(double.PositiveInfinity, availableSize.Height));

        _oneRow = left.DesiredSize.Width + Gap + right.DesiredSize.Width <= availableSize.Width;
        if (_oneRow)
        {
            return new Size(left.DesiredSize.Width + Gap + right.DesiredSize.Width,
                Math.Max(left.DesiredSize.Height, right.DesiredSize.Height));
        }

        left.Measure(availableSize);
        return new Size(Math.Max(left.DesiredSize.Width, right.DesiredSize.Width),
            left.DesiredSize.Height + Gap / 2 + right.DesiredSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count < 2)
            return base.ArrangeOverride(finalSize);

        Control left = Children[0];
        Control right = Children[1];
        if (_oneRow)
        {
            Size l = left.DesiredSize, r = right.DesiredSize;
            left.Arrange(new Rect(0, (finalSize.Height - l.Height) / 2, l.Width, l.Height));
            right.Arrange(new Rect(finalSize.Width - r.Width, (finalSize.Height - r.Height) / 2, r.Width, r.Height));
        }
        else
        {
            double top = left.DesiredSize.Height;
            left.Arrange(new Rect(0, 0, finalSize.Width, top));
            right.Arrange(new Rect(0, top + Gap / 2, finalSize.Width, right.DesiredSize.Height));
        }
        return finalSize;
    }
}
