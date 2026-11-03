using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Skua.App.Avalonia.Converters;

/// <summary>
/// Converts an hourly rate to the selected hourly, per-minute, or per-second display rate.
/// </summary>
public sealed class RatePerUnitConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not IConvertible rateValue || values[1] is not string rateUnit)
            return "—";

        double hourlyRate = System.Convert.ToDouble(rateValue, CultureInfo.InvariantCulture);
        double divisor = rateUnit switch
        {
            "/hr" => 1d,
            "/min" => 60d,
            "/sec" => 3_600d,
            _ => 1d
        };

        string format = rateUnit switch
        {
            "/hr" => "N0",
            "/min" => "N1",
            "/sec" => "N2",
            _ => "N0"
        };

        return (hourlyRate / divisor).ToString(format, culture);
    }
}
