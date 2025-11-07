using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;

namespace BNKaraoke.DJ.Converters
{
    public class QueueColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // NULL GUARD #1 – before login / binding not ready
            if (value is null)
            {
                return GetDefaultBrush(targetType);
            }

            // NULL GUARD #2 – wrong type passed in
            if (value is not QueueEntryViewModel entry)
            {
                LogService.LogWarning("COLOR CONVERTER", $"Invalid value type: {value?.GetType().Name ?? "null"}, returning safe default");
                return GetDefaultBrush(targetType);
            }

            bool isBackground = parameter?.ToString() == "Background";

            try
            {
                // RULE 1: On Hold → Gray
                if (entry.IsOnHold == true)
                {
                    return isBackground 
                        ? new SolidColorBrush(Color.FromRgb(128, 128, 128)) 
                        : Colors.Gray;
                }

                // RULE 2: Singer offline or not joined → Red
                if (entry.IsSingerLoggedIn != true || entry.IsSingerJoined != true)
                {
                    return isBackground 
                        ? new SolidColorBrush(Colors.Red) 
                        : Colors.Red;
                }

                // RULE 3: All good → Green
                return isBackground 
                    ? new SolidColorBrush(Colors.Green) 
                    : Colors.Green;
            }
            catch (Exception ex)
            {
                LogService.LogError("COLOR CONVERTER", "Unexpected exception in converter", ex);
                return GetDefaultBrush(targetType);
            }
        }

        private static object GetDefaultBrush(Type targetType)
        {
            // Safe, clean fallback
            return targetType == typeof(Brush) ? Brushes.Transparent : Colors.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
