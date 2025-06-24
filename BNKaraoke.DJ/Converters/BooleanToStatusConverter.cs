using System;
using System.Globalization;
using System.Windows.Data;

namespace BNKaraoke.DJ.Converters
{
    public class BooleanToStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool wasSkipped)
            {
                return wasSkipped ? "Skipped" : "Sung";
            }
            return "N/A";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}