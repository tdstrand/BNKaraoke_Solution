using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BNKaraoke.DJ.Converters
{
    public class BrushToDarkerBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
            {
                if (brush.Color.A == 0)
                {
                    return Brushes.Transparent;
                }

                var factor = 1.0;
                if (parameter != null && double.TryParse(parameter.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedFactor))
                {
                    factor = parsedFactor;
                }

                factor = Math.Clamp(factor, 0.0, 1.0);

                var color = brush.Color;
                byte r = (byte)Math.Clamp(color.R * factor, 0, 255);
                byte g = (byte)Math.Clamp(color.G * factor, 0, 255);
                byte b = (byte)Math.Clamp(color.B * factor, 0, 255);

                var darker = Color.FromArgb(color.A, r, g, b);
                var result = new SolidColorBrush(darker);
                if (result.CanFreeze)
                {
                    result.Freeze();
                }

                return result;
            }

            return Brushes.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
