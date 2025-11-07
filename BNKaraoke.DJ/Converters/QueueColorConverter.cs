using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace BNKaraoke.DJ.Converters
{
    public class QueueColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // SENIOR DEV NULL GUARD #1 – Kill warning before login
            if (value is null)
            {
                return GetDefaultBrush(targetType);
            }

            // SENIOR DEV NULL GUARD #2 – Entry itself null (race condition)
            if (value is not QueueEntryViewModel entry)
            {
                LogService.LogWarning("COLOR CONVERTER", $"Invalid value type: {value?.GetType().Name ?? "null"}, returning default");
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

                // RULE 2: Singer not ready → Red
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
                // FINAL BULLETPROOF CATCH – should never hit
                LogService.LogError("COLOR CONVERTER", "Unexpected error in converter", ex);
                return GetDefaultBrush(targetType);
            }
        }

        private static object GetDefaultBrush(Type targetType)
        {
            // Safe fallback: Transparent background, White text
            if (targetType == typeof(Brush))
                return Brushes.Transparent;
            return Colors.White;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
