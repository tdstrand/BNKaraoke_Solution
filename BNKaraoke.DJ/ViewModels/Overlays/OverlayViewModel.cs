using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.Services.Overlay;
using System;
using System.Windows;
using System.Windows.Media;

namespace BNKaraoke.DJ.ViewModels.Overlays
{
    public class OverlayViewModel : ViewModels.ViewModelBase
    {
        private static readonly Lazy<OverlayViewModel> _instance = new(() => new OverlayViewModel());
        public static OverlayViewModel Instance => _instance.Value;

        private readonly SettingsService _settingsService = SettingsService.Instance;
        private readonly OverlayTemplateEngine _templateEngine = new();
        private readonly OverlayTemplateContext _templateContext = new();
        private Func<DateTimeOffset> _timeProvider = () => DateTimeOffset.Now;
        private OverlayTemplates _templates = new();
        private bool _suppressSave;

        private bool _isTopEnabled;
        private bool _isBottomEnabled;
        private double _topHeightPercent;
        private double _bottomHeightPercent;
        private double _backgroundOpacity;
        private bool _useGradient;
        private string _primaryColor = "#1e3a8a";
        private string _secondaryColor = "#3b82f6";
        private string _brandText = "BNKaraoke.com";
        private string _topTemplatePlayback = string.Empty;
        private string _bottomTemplatePlayback = string.Empty;
        private string _topTemplateBlue = string.Empty;
        private string _bottomTemplateBlue = string.Empty;
        private bool _isBlueState;
        private Brush _topBandBrush = Brushes.Transparent;
        private Brush _bottomBandBrush = Brushes.Transparent;
        private string _topBandText = string.Empty;
        private string _bottomBandText = string.Empty;

        private OverlayViewModel()
        {
            ApplySettings(_settingsService.Settings.Overlay ?? new OverlaySettings());
        }

        public bool IsTopEnabled
        {
            get => _isTopEnabled;
            set
            {
                if (_isTopEnabled != value)
                {
                    _isTopEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TopBandVisibility));
                    OnPropertyChanged(nameof(TopRowHeight));
                    OnPropertyChanged(nameof(CenterRowHeight));
                    Persist();
                }
            }
        }

        public bool IsBottomEnabled
        {
            get => _isBottomEnabled;
            set
            {
                if (_isBottomEnabled != value)
                {
                    _isBottomEnabled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BottomBandVisibility));
                    OnPropertyChanged(nameof(BottomRowHeight));
                    OnPropertyChanged(nameof(CenterRowHeight));
                    Persist();
                }
            }
        }

        public double TopHeightPercent
        {
            get => _topHeightPercent;
            set
            {
                value = ClampPercent(value);
                if (!value.Equals(_topHeightPercent))
                {
                    _topHeightPercent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TopRowHeight));
                    OnPropertyChanged(nameof(CenterRowHeight));
                    Persist();
                }
            }
        }

        public double BottomHeightPercent
        {
            get => _bottomHeightPercent;
            set
            {
                value = ClampPercent(value);
                if (!value.Equals(_bottomHeightPercent))
                {
                    _bottomHeightPercent = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(BottomRowHeight));
                    OnPropertyChanged(nameof(CenterRowHeight));
                    Persist();
                }
            }
        }

        public double BackgroundOpacity
        {
            get => _backgroundOpacity;
            set
            {
                value = Math.Clamp(value, 0.0, 1.0);
                if (!value.Equals(_backgroundOpacity))
                {
                    _backgroundOpacity = value;
                    OnPropertyChanged();
                    UpdateBrushes();
                    Persist();
                }
            }
        }

        public bool UseGradient
        {
            get => _useGradient;
            set
            {
                if (_useGradient != value)
                {
                    _useGradient = value;
                    OnPropertyChanged();
                    UpdateBrushes();
                    Persist();
                }
            }
        }

        public string PrimaryColor
        {
            get => _primaryColor;
            set
            {
                if (!string.Equals(_primaryColor, value, StringComparison.OrdinalIgnoreCase))
                {
                    _primaryColor = value ?? _primaryColor;
                    OnPropertyChanged();
                    UpdateBrushes();
                    Persist();
                }
            }
        }

        public string SecondaryColor
        {
            get => _secondaryColor;
            set
            {
                if (!string.Equals(_secondaryColor, value, StringComparison.OrdinalIgnoreCase))
                {
                    _secondaryColor = value ?? _secondaryColor;
                    OnPropertyChanged();
                    UpdateBrushes();
                    Persist();
                }
            }
        }

        public string BrandText
        {
            get => _brandText;
            set => SetBrandInternal(value, persist: true);
        }

        public string Brand
        {
            get => BrandText;
            set => BrandText = value;
        }

        public string TopBandText
        {
            get => _topBandText;
            private set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_topBandText, newValue, StringComparison.Ordinal))
                {
                    _topBandText = newValue;
                    OnPropertyChanged();
                }
            }
        }

        public string BottomBandText
        {
            get => _bottomBandText;
            private set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_bottomBandText, newValue, StringComparison.Ordinal))
                {
                    _bottomBandText = newValue;
                    OnPropertyChanged();
                }
            }
        }

        public string TopTemplatePlayback
        {
            get => _topTemplatePlayback;
            set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_topTemplatePlayback, newValue, StringComparison.Ordinal))
                {
                    _topTemplatePlayback = newValue;
                    _templates.PlaybackTop = newValue;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ActiveTopTemplate));
                    UpdateBandText();
                    Persist();
                }
            }
        }

        public string BottomTemplatePlayback
        {
            get => _bottomTemplatePlayback;
            set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_bottomTemplatePlayback, newValue, StringComparison.Ordinal))
                {
                    _bottomTemplatePlayback = newValue;
                    _templates.PlaybackBottom = newValue;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ActiveBottomTemplate));
                    UpdateBandText();
                    Persist();
                }
            }
        }

        public string TopTemplateBlue
        {
            get => _topTemplateBlue;
            set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_topTemplateBlue, newValue, StringComparison.Ordinal))
                {
                    _topTemplateBlue = newValue;
                    _templates.BlueTop = newValue;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ActiveTopTemplate));
                    UpdateBandText();
                    Persist();
                }
            }
        }

        public string BottomTemplateBlue
        {
            get => _bottomTemplateBlue;
            set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_bottomTemplateBlue, newValue, StringComparison.Ordinal))
                {
                    _bottomTemplateBlue = newValue;
                    _templates.BlueBottom = newValue;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ActiveBottomTemplate));
                    UpdateBandText();
                    Persist();
                }
            }
        }

        public bool IsBlueState
        {
            get => _isBlueState;
            set
            {
                if (_isBlueState != value)
                {
                    _isBlueState = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ActiveTopTemplate));
                    OnPropertyChanged(nameof(ActiveBottomTemplate));
                    UpdateBandText();
                }
            }
        }

        public string ActiveTopTemplate => IsBlueState ? TopTemplateBlue : TopTemplatePlayback;

        public string ActiveBottomTemplate => IsBlueState ? BottomTemplateBlue : BottomTemplatePlayback;

        public Brush TopBandBrush
        {
            get => _topBandBrush;
            private set
            {
                if (!ReferenceEquals(_topBandBrush, value))
                {
                    _topBandBrush = value;
                    OnPropertyChanged();
                }
            }
        }

        public Brush BottomBandBrush
        {
            get => _bottomBandBrush;
            private set
            {
                if (!ReferenceEquals(_bottomBandBrush, value))
                {
                    _bottomBandBrush = value;
                    OnPropertyChanged();
                }
            }
        }

        public Visibility TopBandVisibility => IsTopEnabled ? Visibility.Visible : Visibility.Collapsed;

        public Visibility BottomBandVisibility => IsBottomEnabled ? Visibility.Visible : Visibility.Collapsed;

        public GridLength TopRowHeight => IsTopEnabled ? new GridLength(TopHeightPercent, GridUnitType.Star) : new GridLength(0);

        public GridLength BottomRowHeight => IsBottomEnabled ? new GridLength(BottomHeightPercent, GridUnitType.Star) : new GridLength(0);

        public GridLength CenterRowHeight
        {
            get
            {
                var top = IsTopEnabled ? TopHeightPercent : 0.0;
                var bottom = IsBottomEnabled ? BottomHeightPercent : 0.0;
                var remaining = Math.Max(1.0 - (top + bottom), 0.0001);
                return new GridLength(remaining, GridUnitType.Star);
            }
        }

        public void RefreshFromSettings()
        {
            ApplySettings(_settingsService.Settings.Overlay ?? new OverlaySettings());
        }

        public void UpdateFromQueue(QueueEntry? nowPlaying, QueueEntry? upNext, EventDto? currentEvent)
        {
            _templateContext.Requestor = ExtractRequestor(nowPlaying);
            _templateContext.Song = nowPlaying?.SongTitle ?? string.Empty;
            _templateContext.Artist = nowPlaying?.SongArtist ?? string.Empty;
            _templateContext.UpNextRequestor = ExtractRequestor(upNext);
            _templateContext.UpNextSong = upNext?.SongTitle ?? string.Empty;
            _templateContext.UpNextArtist = upNext?.SongArtist ?? string.Empty;
            _templateContext.EventName = ExtractEventName(currentEvent);
            _templateContext.Venue = currentEvent?.Location ?? string.Empty;

            UpdateBandText();
        }

        private void ApplySettings(OverlaySettings settings)
        {
            settings.Clamp();
            _suppressSave = true;

            _isTopEnabled = settings.EnabledTop;
            _isBottomEnabled = settings.EnabledBottom;
            _topHeightPercent = settings.TopHeightPercent;
            _bottomHeightPercent = settings.BottomHeightPercent;
            _backgroundOpacity = settings.BackgroundOpacity;
            _useGradient = settings.UseGradient;
            _primaryColor = settings.PrimaryColor ?? _primaryColor;
            _secondaryColor = settings.SecondaryColor ?? _secondaryColor;
            _templates = settings.Templates?.Clone() ?? new OverlayTemplates();
            _templates.EnsureDefaults();
            _topTemplatePlayback = _templates.PlaybackTop;
            _bottomTemplatePlayback = _templates.PlaybackBottom;
            _topTemplateBlue = _templates.BlueTop;
            _bottomTemplateBlue = _templates.BlueBottom;
            SetBrandInternal(settings.Brand, persist: false);

            OnPropertyChanged(nameof(IsTopEnabled));
            OnPropertyChanged(nameof(IsBottomEnabled));
            OnPropertyChanged(nameof(TopHeightPercent));
            OnPropertyChanged(nameof(BottomHeightPercent));
            OnPropertyChanged(nameof(BackgroundOpacity));
            OnPropertyChanged(nameof(UseGradient));
            OnPropertyChanged(nameof(PrimaryColor));
            OnPropertyChanged(nameof(SecondaryColor));
            OnPropertyChanged(nameof(TopTemplatePlayback));
            OnPropertyChanged(nameof(BottomTemplatePlayback));
            OnPropertyChanged(nameof(TopTemplateBlue));
            OnPropertyChanged(nameof(BottomTemplateBlue));
            OnPropertyChanged(nameof(TopBandVisibility));
            OnPropertyChanged(nameof(BottomBandVisibility));
            OnPropertyChanged(nameof(TopRowHeight));
            OnPropertyChanged(nameof(BottomRowHeight));
            OnPropertyChanged(nameof(CenterRowHeight));

            UpdateBrushes();
            UpdateBandText();

            _suppressSave = false;
        }

        private void SetBrandInternal(string? value, bool persist)
        {
            var newBrand = value ?? string.Empty;
            var hasChanged = !string.Equals(_brandText, newBrand, StringComparison.Ordinal);
            _brandText = newBrand;
            _templateContext.Brand = newBrand;
            if (hasChanged)
            {
                OnPropertyChanged(nameof(BrandText));
                OnPropertyChanged(nameof(Brand));
            }

            UpdateBandText();

            if (persist)
            {
                Persist();
            }
        }

        private static string ExtractRequestor(QueueEntry? entry)
        {
            if (entry == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(entry.RequestorDisplayName))
            {
                return entry.RequestorDisplayName!;
            }

            return entry.RequestorUserName ?? string.Empty;
        }

        private static string ExtractEventName(EventDto? evt)
        {
            if (evt == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(evt.Description))
            {
                return evt.Description!;
            }

            return evt.EventCode ?? string.Empty;
        }

        private void UpdateBrushes()
        {
            TopBandBrush = CreateBrush(isTop: true);
            BottomBandBrush = CreateBrush(isTop: false);
        }

        private void UpdateBandText()
        {
            _templateContext.Timestamp = _timeProvider();
            TopBandText = _templateEngine.Render(ActiveTopTemplate, _templateContext);
            BottomBandText = _templateEngine.Render(ActiveBottomTemplate, _templateContext);
        }

        private Brush CreateBrush(bool isTop)
        {
            var primary = ParseColor(PrimaryColor, Color.FromRgb(30, 58, 138));
            var secondary = ParseColor(SecondaryColor, Color.FromRgb(59, 130, 246));
            if (!UseGradient)
            {
                var color = WithOpacity(isTop ? primary : secondary, BackgroundOpacity);
                var solid = new SolidColorBrush(color);
                solid.Freeze();
                return solid;
            }

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, isTop ? 0 : 1),
                EndPoint = new Point(0, isTop ? 1 : 0)
            };
            brush.GradientStops.Add(new GradientStop(WithOpacity(primary, BackgroundOpacity), 0));
            brush.GradientStops.Add(new GradientStop(WithOpacity(secondary, BackgroundOpacity), 1));
            brush.Freeze();
            return brush;
        }

        private void Persist()
        {
            if (_suppressSave)
            {
                return;
            }

            var settings = _settingsService.Settings;
            settings.Overlay ??= new OverlaySettings();
            settings.Overlay.EnabledTop = IsTopEnabled;
            settings.Overlay.EnabledBottom = IsBottomEnabled;
            settings.Overlay.TopHeightPercent = TopHeightPercent;
            settings.Overlay.BottomHeightPercent = BottomHeightPercent;
            settings.Overlay.BackgroundOpacity = BackgroundOpacity;
            settings.Overlay.UseGradient = UseGradient;
            settings.Overlay.PrimaryColor = PrimaryColor;
            settings.Overlay.SecondaryColor = SecondaryColor;
            settings.Overlay.Brand = BrandText;
            settings.Overlay.Templates ??= new OverlayTemplates();
            settings.Overlay.Templates.PlaybackTop = _topTemplatePlayback;
            settings.Overlay.Templates.PlaybackBottom = _bottomTemplatePlayback;
            settings.Overlay.Templates.BlueTop = _topTemplateBlue;
            settings.Overlay.Templates.BlueBottom = _bottomTemplateBlue;

            _settingsService.Save();
        }

        private static double ClampPercent(double value)
        {
            if (!double.IsFinite(value))
            {
                return 0.0;
            }

            return Math.Clamp(value, 0.0, 1.0);
        }

        private static Color ParseColor(string color, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                return fallback;
            }

            try
            {
                var converter = new ColorConverter();
                var converted = converter.ConvertFromString(color) as Color?;
                if (converted.HasValue)
                {
                    return converted.Value;
                }
            }
            catch
            {
                // Ignore and use fallback
            }

            return fallback;
        }

        private static Color WithOpacity(Color color, double opacity)
        {
            opacity = Math.Clamp(opacity, 0.0, 1.0);
            byte alpha = (byte)Math.Round(opacity * 255);
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }
    }
}
