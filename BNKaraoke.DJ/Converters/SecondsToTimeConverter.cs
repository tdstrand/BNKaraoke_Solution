using System;
using System.Globalization;
using System.Windows.Data;

namespace BNKaraoke.DJ.Converters
{
    public class SecondsToTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
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
                return string.Empty;
            }
            return TimeSpan.FromSeconds(seconds).ToString(@"m\:ss");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
