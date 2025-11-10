using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BNKaraoke.DJ.Converters
{
    public class CountToBrushConverter : IValueConverter
    {
        public int Threshold { get; set; } = 4;
        public string BelowThreshold { get; set; } = "#FF808080"; // Gray
        public string AtOrAboveThreshold { get; set; } = "#FFFF0000"; // Red

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int count)
            {
                var colorStr = count >= Threshold ? AtOrAboveThreshold : BelowThreshold;
                return (SolidColorBrush)new BrushConverter().ConvertFrom(colorStr)!;
            }
            return Brushes.Gray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
