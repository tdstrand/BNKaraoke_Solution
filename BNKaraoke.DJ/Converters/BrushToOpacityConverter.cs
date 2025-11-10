using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BNKaraoke.DJ.Converters
{
    public class BrushToOpacityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var defaultOpacity = 1.0;
            if (parameter != null && double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedOpacity))
            {
                defaultOpacity = parsedOpacity;
            }

            defaultOpacity = Math.Clamp(defaultOpacity, 0.0, 1.0);

            if (value is SolidColorBrush brush && brush.Color.A > 0)
            {
                return defaultOpacity;
            }

            return 1.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
