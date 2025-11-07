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
            if (value is null)
                return GetDefaultBrush(targetType);

            if (value is not QueueEntryViewModel entry)
                return GetDefaultBrush(targetType);

            bool isBackground = parameter?.ToString() == "Background";

            try
            {
                if (entry.IsOnHold == true)
                    return isBackground ? new SolidColorBrush(Color.FromRgb(128, 128, 128)) : Colors.Gray;

                if (entry.IsSingerLoggedIn != true || entry.IsSingerJoined != true)
                    return isBackground ? new SolidColorBrush(Colors.Red) : Colors.Red;

                return isBackground ? new SolidColorBrush(Colors.Green) : Colors.Green;
            }
            catch
            {
                return GetDefaultBrush(targetType);
            }
        }

        private static object GetDefaultBrush(Type targetType)
            => targetType == typeof(Brush) ? Brushes.Transparent : Colors.White;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
