using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using BNKaraoke.DJ.Models;
using Serilog;

namespace BNKaraoke.DJ.Converters
{
    public class SingerStatusToColorConverter : IValueConverter, IMultiValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string colorHex;
            if (value is Singer singer)
            {
                if (singer.IsLoggedIn && singer.IsJoined && !singer.IsOnBreak)
                    colorHex = "#008000"; // Green
                else if (singer.IsLoggedIn && singer.IsJoined && singer.IsOnBreak)
                    colorHex = "#0000FF"; // Blue
                else if (singer.IsLoggedIn && !singer.IsJoined)
                    colorHex = "#FFD700"; // Gold
                else
                    colorHex = "#FF0000"; // Red

                Log.Information("[COLOR CONVERTER] Returning {ColorHex} for UserId={UserId}, DisplayName={DisplayName}, IsLoggedIn={IsLoggedIn}, IsJoined={IsJoined}, IsOnBreak={IsOnBreak}",
                    colorHex, singer.UserId, singer.DisplayName, singer.IsLoggedIn, singer.IsJoined, singer.IsOnBreak);
            }
            else if (value is QueueEntry queueEntry)
            {
                if (queueEntry.IsSingerLoggedIn && queueEntry.IsSingerJoined && !queueEntry.IsSingerOnBreak)
                    colorHex = "#008000"; // Green
                else if (queueEntry.IsSingerLoggedIn && queueEntry.IsSingerJoined && queueEntry.IsSingerOnBreak)
                    colorHex = "#0000FF"; // Blue
                else if (queueEntry.IsSingerLoggedIn && !queueEntry.IsSingerJoined)
                    colorHex = "#FFD700"; // Gold
                else
                    colorHex = "#FF0000"; // Red

                Log.Information("[COLOR CONVERTER QUEUE] Returning {ColorHex} for QueueId={QueueId}, RequestorUserName={RequestorUserName}, IsSingerLoggedIn={IsSingerLoggedIn}, IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak}",
                    colorHex, queueEntry.QueueId, queueEntry.RequestorUserName, queueEntry.IsSingerLoggedIn, queueEntry.IsSingerJoined, queueEntry.IsSingerOnBreak);
            }
            else
            {
                colorHex = "#FF0000"; // Red
                Log.Warning("[COLOR CONVERTER] Invalid value type, returning default Red (#FF0000)");
            }

            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        }

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 1 && values[0] is QueueEntry queueEntry)
            {
                string colorHex;
                if (queueEntry.IsSingerLoggedIn && queueEntry.IsSingerJoined && !queueEntry.IsSingerOnBreak)
                    colorHex = "#008000"; // Green
                else if (queueEntry.IsSingerLoggedIn && queueEntry.IsSingerJoined && queueEntry.IsSingerOnBreak)
                    colorHex = "#0000FF"; // Blue
                else if (queueEntry.IsSingerLoggedIn && !queueEntry.IsSingerJoined)
                    colorHex = "#FFD700"; // Gold
                else
                    colorHex = "#FF0000"; // Red

                Log.Information("[COLOR CONVERTER QUEUE] Returning {ColorHex} for QueueId={QueueId}, RequestorUserName={RequestorUserName}, IsSingerLoggedIn={IsSingerLoggedIn}, IsSingerJoined={IsSingerJoined}, IsSingerOnBreak={IsSingerOnBreak}",
                    colorHex, queueEntry.QueueId, queueEntry.RequestorUserName, queueEntry.IsSingerLoggedIn, queueEntry.IsSingerJoined, queueEntry.IsSingerOnBreak);
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
            }
            Log.Warning("[COLOR CONVERTER QUEUE] Invalid MultiBinding values, returning default Red (#FF0000)");
            return Brushes.Red;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}