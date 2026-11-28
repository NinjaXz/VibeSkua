using Avalonia.Data.Converters;
using Avalonia.Media;
using Skua.Core.Models.Auras;
using System;
using System.Globalization;

namespace Skua.App.Avalonia.Converters;

/// <summary>
/// Colors of an aura effect pill: gold buff, green heal over time, red damage over time, blue-violet
/// debuff, orange scroll, grey otherwise. "fg" as the parameter gives the text color, else the fill.
/// </summary>
public class EffectKindBrushConverter : IValueConverter
{
    public static readonly EffectKindBrushConverter Instance = new();

    public static Color ColorOf(EffectKind kind) => kind switch
    {
        EffectKind.Buff => Color.Parse("#E8B931"),
        EffectKind.HealOverTime => Color.Parse("#4CC26A"),
        EffectKind.DamageOverTime => Color.Parse("#E5484D"),
        EffectKind.Debuff => Color.Parse("#8A6CFF"),
        EffectKind.Scroll => Color.Parse("#F08A24"),
        _ => Color.Parse("#9A9893")
    };

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        Color c = ColorOf(value is EffectKind k ? k : EffectKind.Neutral);
        return parameter as string == "fg"
            ? new SolidColorBrush(c)
            : new SolidColorBrush(Color.FromArgb(0x33, c.R, c.G, c.B));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>True when the bound value's name equals the parameter (for highlighting the chosen filter chip).</summary>
public class NameEqualsConverter : IValueConverter
{
    public static readonly NameEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && string.Equals(value.ToString(), parameter as string, StringComparison.OrdinalIgnoreCase);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
