using System;
using System.Globalization;
using System.Windows.Data;

namespace BNKaraoke.DJ.Converters
{
    public class PlayPauseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPlaying)
            {
                return isPlaying ? "⏸" : "▶"; // Pause icon if playing, Play icon if paused
            }
            return "▶"; // Default to Play icon
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}