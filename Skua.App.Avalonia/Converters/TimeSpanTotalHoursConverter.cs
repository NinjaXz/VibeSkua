using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Skua.App.Avalonia.Converters;

/// <summary>
/// Formats a time span as total hours, minutes, and seconds without fractional seconds.
/// </summary>
public sealed class TimeSpanTotalHoursConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TimeSpan timeSpan)
            return "—";

        bool isNegative = timeSpan < TimeSpan.Zero;
        timeSpan = timeSpan.Duration();

        long totalHours = (long)Math.Floor(timeSpan.TotalHours);
        string sign = isNegative ? "-" : string.Empty;

        return string.Concat(
            sign,
            totalHours.ToString(CultureInfo.InvariantCulture),
            ":",
            timeSpan.Minutes.ToString("D2", CultureInfo.InvariantCulture),
            ":",
            timeSpan.Seconds.ToString("D2", CultureInfo.InvariantCulture));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
