using System;
using System.Globalization;
using System.Windows.Data;

namespace BNKaraoke.DJ.Converters
{
    public class BooleanToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool booleanValue && parameter is string paramString)
            {
                var values = paramString.Split(';');
                if (values.Length == 2)
                {
                    return booleanValue ? values[1] : values[0];
                }
            }
            return value?.ToString() ?? string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException("ConvertBack is not supported for BooleanToStringConverter.");
        }
    }
}