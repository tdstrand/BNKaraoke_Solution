using BNKaraoke.DJ.Services.Overlay;
using BNKaraoke.DJ.ViewModels.Overlays;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;

namespace BNKaraoke.DJ.ViewModels.Settings
{
    public class OverlayTokenInfo
    {
        public OverlayTokenInfo(string token, string description)
        {
            Token = token;
            Description = description;
        }

        public string Token { get; }
        public string Description { get; }
    }

    public partial class OverlaySettingsViewModel : ObservableObject, IDisposable
    {
        private readonly OverlayViewModel _overlay;
        private readonly OverlayTemplateEngine _templateEngine = new();
        private readonly OverlayTemplateContext _context = new();
        private readonly IReadOnlyList<FontWeight> _availableFontWeights;
        private string _playbackTop = string.Empty;
        private string _playbackBottom = string.Empty;
        private string _blueTop = string.Empty;
        private string _blueBottom = string.Empty;
        private bool _isBluePreview;
        private bool _disposed;

        public OverlaySettingsViewModel()
        {
            _overlay = OverlayViewModel.Instance;
            _overlay.PropertyChanged += Overlay_PropertyChanged;

            AvailableFontFamilies = Fonts.SystemFontFamilies
                .OrderBy(f => f.Source)
                .ToList();

            _availableFontWeights = new List<FontWeight>
            {
                FontWeights.Light,
                FontWeights.Normal,
                FontWeights.Medium,
                FontWeights.SemiBold,
                FontWeights.Bold,
                FontWeights.Black
            };

            TokenCheatSheet = new ObservableCollection<OverlayTokenInfo>
            {
                new OverlayTokenInfo("{Brand}", "Current brand text"),
                new OverlayTokenInfo("{Requestor}", "Now playing singer name"),
                new OverlayTokenInfo("{Song}", "Now playing song title"),
                new OverlayTokenInfo("{Artist}", "Now playing song artist"),
                new OverlayTokenInfo("{UpNextRequestor}", "Up next singer name"),
                new OverlayTokenInfo("{UpNextSong}", "Up next song title"),
                new OverlayTokenInfo("{UpNextArtist}", "Up next song artist"),
                new OverlayTokenInfo("{EventName}", "Event description or code"),
                new OverlayTokenInfo("{Venue}", "Event venue/location"),
                new OverlayTokenInfo("{Time}", "Current local time"),
            };

            UpdateContext();
            UpdatePreviewText();
        }

        public IReadOnlyList<FontFamily> AvailableFontFamilies { get; }

        public IReadOnlyList<FontWeight> AvailableFontWeights => _availableFontWeights;

        public ObservableCollection<OverlayTokenInfo> TokenCheatSheet { get; }

        public OverlayViewModel Overlay => _overlay;

        public FontFamily SelectedFontFamily
        {
            get
            {
                var match = AvailableFontFamilies.FirstOrDefault(f =>
                    string.Equals(f.Source, Overlay.FontFamilyName, StringComparison.OrdinalIgnoreCase));
                return match ?? new FontFamily(Overlay.FontFamilyName);
            }
            set
            {
                if (value != null)
                {
                    Overlay.FontFamilyName = value.Source;
                    OnPropertyChanged();
                }
            }
        }

        public FontWeight SelectedFontWeight
        {
            get
            {
                var match = _availableFontWeights.FirstOrDefault(weight =>
                    string.Equals(weight.ToString(), Overlay.FontWeightName, StringComparison.OrdinalIgnoreCase));
                return match.Equals(default(FontWeight)) ? FontWeights.Bold : match;
            }
            set
            {
                Overlay.FontWeightName = value.ToString();
                OnPropertyChanged();
            }
        }

        public bool IsBluePreview
        {
            get => _isBluePreview;
            set
            {
                if (_isBluePreview != value)
                {
                    _isBluePreview = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PreviewModeLabel));
                    OnPropertyChanged(nameof(PreviewTopText));
                    OnPropertyChanged(nameof(PreviewBottomText));
                }
            }
        }

        public string PreviewModeLabel => IsBluePreview ? "Preview: Blue Screen" : "Preview: Playback";

        public string PreviewTopText => IsBluePreview ? _blueTop : _playbackTop;

        public string PreviewBottomText => IsBluePreview ? _blueBottom : _playbackBottom;

        [RelayCommand]
        private void ResetTemplates()
        {
            Overlay.ResetTemplates();
            UpdatePreviewText();
        }

        private void UpdateContext()
        {
            _context.Brand = Overlay.Brand;
            _context.Requestor = "Alex M.";
            _context.Song = "Don't Stop Believin'";
            _context.Artist = "Journey";
            _context.UpNextRequestor = "Casey L.";
            _context.UpNextSong = "Africa";
            _context.UpNextArtist = "Toto";
            _context.EventName = "BN Karaoke Night";
            _context.Venue = "Capitol Lounge";
        }

        private void UpdatePreviewText()
        {
            UpdateContext();
            _context.Timestamp = DateTimeOffset.Now;
            _playbackTop = _templateEngine.Render(Overlay.TopTemplatePlayback, _context);
            _playbackBottom = _templateEngine.Render(Overlay.BottomTemplatePlayback, _context);
            _blueTop = _templateEngine.Render(Overlay.TopTemplateBlue, _context);
            _blueBottom = _templateEngine.Render(Overlay.BottomTemplateBlue, _context);
            OnPropertyChanged(nameof(PreviewTopText));
            OnPropertyChanged(nameof(PreviewBottomText));
        }

        private void Overlay_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(OverlayViewModel.BrandText):
                case nameof(OverlayViewModel.Brand):
                case nameof(OverlayViewModel.TopTemplatePlayback):
                case nameof(OverlayViewModel.BottomTemplatePlayback):
                case nameof(OverlayViewModel.TopTemplateBlue):
                case nameof(OverlayViewModel.BottomTemplateBlue):
                    UpdatePreviewText();
                    break;
                case nameof(OverlayViewModel.FontFamilyName):
                    OnPropertyChanged(nameof(SelectedFontFamily));
                    break;
                case nameof(OverlayViewModel.FontWeightName):
                    OnPropertyChanged(nameof(SelectedFontWeight));
                    break;
                case nameof(OverlayViewModel.FontColor):
                    // Preview swatch bound directly to Overlay.FontBrush
                    break;
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _overlay.PropertyChanged -= Overlay_PropertyChanged;
            }

            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
