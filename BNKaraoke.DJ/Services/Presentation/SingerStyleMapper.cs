using System.Windows.Media;
using BNKaraoke.DJ.Models;

namespace BNKaraoke.DJ.Services.Presentation
{
    public static class SingerStyleMapper
    {
        private static readonly SolidColorBrush BrushBrightRed;
        private static readonly SolidColorBrush BrushOrange;
        private static readonly SolidColorBrush BrushAmber;
        private static readonly SolidColorBrush BrushGreen;
        private static readonly SolidColorBrush BrushGray;

        static SingerStyleMapper()
        {
            BrushBrightRed = new SolidColorBrush(Color.FromRgb(0xE8, 0x1B, 0x1B)); BrushBrightRed.Freeze();
            BrushOrange = new SolidColorBrush(Color.FromRgb(0xFF, 0x88, 0x00)); BrushOrange.Freeze();
            BrushAmber = new SolidColorBrush(Color.FromRgb(0xFF, 0xC1, 0x07)); BrushAmber.Freeze();
            BrushGreen = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)); BrushGreen.Freeze();
            BrushGray = new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)); BrushGray.Freeze();
        }

        public static SolidColorBrush MapForeground(SingerStatusFlags status)
        {
            if (status.HasFlag(SingerStatusFlags.OnBreak)) return BrushAmber;
            if (status.HasFlag(SingerStatusFlags.Joined)) return BrushGreen;
            if (status.HasFlag(SingerStatusFlags.LoggedIn)) return BrushOrange;
            return BrushBrightRed;
        }

        public static SolidColorBrush DefaultForeground() => BrushGray;
    }
}
