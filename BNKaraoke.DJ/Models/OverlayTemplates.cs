namespace BNKaraoke.DJ.Models
{
    public class OverlayTemplates
    {
        private const string DefaultPlaybackTop = "{Brand} • UP NEXT: {UpNextRequestor} – {UpNextSong} – {UpNextArtist}";
        private const string DefaultPlaybackBottom = "{Brand} • NOW PLAYING: {Requestor} – {Song} – {Artist}";
        private const string DefaultBlueTop = "{Brand} • UP NEXT: {UpNextRequestor} – {UpNextSong} – {UpNextArtist}";
        private const string DefaultBlueBottom = "{Brand} • REQUEST A SONG AT {Brand}";

        public string PlaybackTop { get; set; } = DefaultPlaybackTop;
        public string PlaybackBottom { get; set; } = DefaultPlaybackBottom;
        public string BlueTop { get; set; } = DefaultBlueTop;
        public string BlueBottom { get; set; } = DefaultBlueBottom;

        public OverlayTemplates Clone()
        {
            return new OverlayTemplates
            {
                PlaybackTop = PlaybackTop,
                PlaybackBottom = PlaybackBottom,
                BlueTop = BlueTop,
                BlueBottom = BlueBottom
            };
        }

        public void EnsureDefaults()
        {
            PlaybackTop = string.IsNullOrWhiteSpace(PlaybackTop) ? DefaultPlaybackTop : PlaybackTop;
            PlaybackBottom = string.IsNullOrWhiteSpace(PlaybackBottom) ? DefaultPlaybackBottom : PlaybackBottom;
            BlueTop = string.IsNullOrWhiteSpace(BlueTop) ? DefaultBlueTop : BlueTop;
            BlueBottom = string.IsNullOrWhiteSpace(BlueBottom) ? DefaultBlueBottom : BlueBottom;
        }
    }
}
