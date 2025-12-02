using System;
using System.Globalization;
using System.Windows.Data;

namespace BNKaraoke.DJ.Converters
{
    public class SecondsToTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            const string defaultText = "--:--";
            if (value == null)
            {
                return defaultText;
            }

            if (value is TimeSpan ts)
            {
                return ts.ToString(@"m\:ss");
            }

            double seconds;
            if (value is double d)
            {
                seconds = d;
            }
            else if (value is float f)
            {
                seconds = f;
            }
            else if (!double.TryParse(value.ToString(), out seconds))
            {
                return defaultText;
            }

            return TimeSpan.FromSeconds(seconds).ToString(@"m\:ss");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
