using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Skua.App.Avalonia.Converters;

/// <summary>
/// Converts an hourly rate to the selected hourly, per-minute, or per-second display rate.
/// With ConverterParameter "perMinute" the value is a per-minute rate (kills, deaths) and gets one more decimal.
/// </summary>
public sealed class RatePerUnitConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not IConvertible rateValue || values[1] is not string rateUnit)
            return "—";

        bool perMinute = parameter is "perMinute";
        double hourlyRate = System.Convert.ToDouble(rateValue, CultureInfo.InvariantCulture) * (perMinute ? 60d : 1d);
        double divisor = rateUnit switch
        {
            "/hr" => 1d,
            "/min" => 60d,
            "/sec" => 3_600d,
            _ => 1d
        };

        string format = (rateUnit, perMinute) switch
        {
            ("/min", false) => "N1",
            ("/sec", false) => "N2",
            ("/hr", true) => "N1",
            ("/min", true) => "N2",
            ("/sec", true) => "N3",
            _ => "N0"
        };

        return (hourlyRate / divisor).ToString(format, culture);
    }
}
