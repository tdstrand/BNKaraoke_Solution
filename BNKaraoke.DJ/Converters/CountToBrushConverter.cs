using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BNKaraoke.DJ.Converters
{
    public class CountToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count && count >= 3)
            {
                return Brushes.Red;
            }

            return Brushes.LightGray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
