using System;
using System.Globalization;
using System.Windows.Data;

namespace BNKaraoke.DJ.Converters;

public class TimeRemainingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int timeRemaining && int.TryParse(parameter as string, out int threshold))
        {
            return timeRemaining <= threshold;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}