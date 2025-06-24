using System;
using System.Globalization;
using System.Windows.Data;

namespace BNKaraoke.DJ.Converters
{
    public class TestModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool testMode && testMode)
                return "Mode: Development (Test Mode)";
            return "Mode: Production";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}