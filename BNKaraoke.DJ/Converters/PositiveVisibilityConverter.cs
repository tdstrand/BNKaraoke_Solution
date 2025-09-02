using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace BNKaraoke.DJ.Converters
{
    public class PositiveVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return Visibility.Collapsed;
            if (value is double d) return d > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (value is float f) return f > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (double.TryParse(value.ToString(), out var num)) return num > 0 ? Visibility.Visible : Visibility.Collapsed;
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
