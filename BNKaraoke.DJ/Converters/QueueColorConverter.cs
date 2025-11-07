using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BNKaraoke.DJ.ViewModels;

namespace BNKaraoke.DJ.Converters
{
    public class QueueColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not QueueEntryViewModel entry)
            {
                return targetType == typeof(Brush) 
                    ? new SolidColorBrush(Colors.Red) 
                    : Colors.Red;
            }

            var isBackground = parameter?.ToString() == "Background";

            if (entry.IsOnHold)
            {
                return isBackground 
                    ? new SolidColorBrush(Color.FromRgb(128, 128, 128)) 
                    : Colors.Gray;
            }

            if (!entry.IsSingerLoggedIn || !entry.IsSingerJoined)
            {
                return isBackground 
                    ? new SolidColorBrush(Colors.Red) 
                    : Colors.Red;
            }

            return isBackground 
                ? new SolidColorBrush(Colors.Green) 
                : Colors.Green;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
