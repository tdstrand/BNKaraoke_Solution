using System;
using System.Globalization;
using System.Windows.Data;

namespace BNKaraoke.DJ.Converters;

public class VideoLengthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2)
        {
            string currentPosition = values[0] as string ?? "--:--";
            string totalLength = values[1] as string ?? "--:--";
            return $"Timer: {currentPosition} / {totalLength}";
        }
        return "Timer: --:-- / --:--";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}