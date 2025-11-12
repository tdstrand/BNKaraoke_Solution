using System.Windows.Media;
using BNKaraoke.DJ.Models;

namespace BNKaraoke.DJ.Services.Presentation
{
    public static class SingerStyleMapper
    {
        private static readonly SolidColorBrush BrushGreen;
        private static readonly SolidColorBrush BrushYellow;
        private static readonly SolidColorBrush BrushRed;
        private static readonly SolidColorBrush BrushDefault;

        static SingerStyleMapper()
        {
            BrushGreen = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00)); BrushGreen.Freeze();
            BrushYellow = new SolidColorBrush(Color.FromRgb(0xCC, 0xA3, 0x00)); BrushYellow.Freeze();
            BrushRed = new SolidColorBrush(Color.FromRgb(0xCC, 0x00, 0x00)); BrushRed.Freeze();
            BrushDefault = new SolidColorBrush(Colors.White); BrushDefault.Freeze();
        }

        public static SolidColorBrush MapForeground(SingerStatusFlags status)
        {
            if (status.HasFlag(SingerStatusFlags.Joined)) return BrushGreen;
            if (status.HasFlag(SingerStatusFlags.LoggedIn)) return BrushYellow;
            return BrushRed;
        }

        public static SolidColorBrush DefaultForeground() => BrushDefault;
    }
}
