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
            BrushBrightRed = CreateBrush(0xE8, 0x1B, 0x1B);
            BrushOrange = CreateBrush(0xFF, 0x88, 0x00);
            BrushAmber = CreateBrush(0xFF, 0xC1, 0x07);
            BrushGreen = CreateBrush(0x2E, 0xCC, 0x71);
            BrushGray = CreateBrush(0xCC, 0xCC, 0xCC);
        }

        public static SolidColorBrush MapForeground(SingerStatusFlags status)
        {
            if (status.HasFlag(SingerStatusFlags.OnBreak)) return BrushAmber;
            if (status.HasFlag(SingerStatusFlags.Joined)) return BrushGreen;
            if (status.HasFlag(SingerStatusFlags.LoggedIn)) return BrushOrange;
            return BrushBrightRed;
        }

        public static SolidColorBrush DefaultForeground() => BrushGray;

        private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
        {
            var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }
    }
}
