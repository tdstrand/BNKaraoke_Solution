using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.Services.Overlay;
using BNKaraoke.DJ.Services.Playback;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace BNKaraoke.DJ.ViewModels.Overlays
{
    public class OverlayViewModel : ViewModels.ViewModelBase
    {
        private static readonly Lazy<OverlayViewModel> _instance = new(() => new OverlayViewModel());
        public static OverlayViewModel Instance => _instance.Value;

        private const string NoUpcomingPlaceholder = "—";

        private readonly SettingsService _settingsService = SettingsService.Instance;
        private readonly IUserSessionService _userSessionService = UserSessionService.Instance;
        private readonly OverlayTemplateEngine _templateEngine = new();
        private readonly OverlayTemplateContext _templateContext = new();
        private Func<DateTimeOffset> _timeProvider = () => DateTimeOffset.Now;
        private OverlayTemplates _templates = new();
        private bool _suppressSave;

        private List<QueueEntry> _queueSnapshot = new();
        private int? _playheadQueueId;
        private QueueEntry? _playheadFallback;
        private EventDto? _currentEvent;
        private ReorderMode _matureMode;

        private bool _isTopEnabled;
        private bool _isBottomEnabled;
        private double _topHeightPercent;
        private double _bottomHeightPercent;
        private double _backgroundOpacity;
        private bool _useGradient;
        private string _primaryColor = "#1e3a8a";
        private string _secondaryColor = "#3b82f6";
        private string _brandText = "BNKaraoke.com";
        private string _fontFamilyName = "Segoe UI";
        private double _fontSize = 44.0;
        private string _fontWeightName = "Bold";
        private string _fontColor = "#FFFFFFFF";
        private bool _isStrokeEnabled = true;
        private bool _isShadowEnabled = true;
        private bool _marqueeEnabled = true;
        private double _marqueeSpeedPxPerSecond = 90.0;
        private double _marqueeSpacerWidthPx = 140.0;
        private int _marqueeCrossfadeMs = 200;
        private string _topTemplatePlayback = string.Empty;
        private string _bottomTemplatePlayback = string.Empty;
        private string _topTemplateBlue = string.Empty;
        private string _bottomTemplateBlue = string.Empty;
        private bool _isBlueState;
        private Brush _topBandBrush = Brushes.Transparent;
        private Brush _bottomBandBrush = Brushes.Transparent;
        private Brush _sidePanelBrush = Brushes.Transparent;
        private string _topBandText = string.Empty;
        private string _bottomBandText = string.Empty;
        private Brush _fontBrush = Brushes.White;
        private string _sidePanelClock = string.Empty;
        private string _eventNameDisplay = string.Empty;
        private string _eventVenueDisplay = string.Empty;
        private string _nowPlayingPrimary = NoUpcomingPlaceholder;
        private string _nowPlayingSecondary = string.Empty;
        private string _upNextPrimary = NoUpcomingPlaceholder;
        private string _upNextSecondary = string.Empty;
        private readonly FontWeightConverter _fontWeightConverter = new();

        private OverlayViewModel()
        {
            ApplySettings(_settingsService.Settings.Overlay ?? new OverlaySettings());
            _matureMode = _userSessionService.PreferredReorderMode ?? ParseMatureMode(_settingsService.Settings.DefaultReorderMaturePolicy);
            _userSessionService.PreferredReorderModeChanged += HandlePreferredReorderModeChanged;
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

        public string FontFamilyName
        {
            get => _fontFamilyName;
            set
            {
                var newValue = string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value;
                if (!string.Equals(_fontFamilyName, newValue, StringComparison.OrdinalIgnoreCase))
                {
                    _fontFamilyName = newValue;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FontFamily));
                    Persist();
                }
            }
        }

        public FontFamily FontFamily => new(FontFamilyName);

        public double FontSize
        {
            get => _fontSize;
            set
            {
                var clamped = double.IsFinite(value) ? Math.Clamp(value, 16.0, 96.0) : 44.0;
                if (!_fontSize.Equals(clamped))
                {
                    _fontSize = clamped;
                    OnPropertyChanged();
                    Persist();
                }
            }
        }

        public string FontWeightName
        {
            get => _fontWeightName;
            set
            {
                var normalized = NormalizeFontWeight(value);
                if (!string.Equals(_fontWeightName, normalized, StringComparison.Ordinal))
                {
                    _fontWeightName = normalized;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FontWeight));
                    Persist();
                }
            }
        }

        public FontWeight FontWeight
        {
            get
            {
                try
                {
                    var converted = _fontWeightConverter.ConvertFromInvariantString(FontWeightName);
                    if (converted is FontWeight fontWeight)
                    {
                        return fontWeight;
                    }
                }
                catch
                {
                    // Swallow and fall back to default below.
                }

                return FontWeights.Bold;
            }
        }

        public string FontColor
        {
            get => _fontColor;
            set
            {
                var normalized = NormalizeColor(value);
                if (!string.Equals(_fontColor, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    _fontColor = normalized;
                    OnPropertyChanged();
                    UpdateFontBrush();
                    Persist();
                }
            }
        }

        public Brush FontBrush
        {
            get => _fontBrush;
            private set
            {
                if (!ReferenceEquals(_fontBrush, value))
                {
                    _fontBrush = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsStrokeEnabled
        {
            get => _isStrokeEnabled;
            set
            {
                if (_isStrokeEnabled != value)
                {
                    _isStrokeEnabled = value;
                    OnPropertyChanged();
                    Persist();
                }
            }
        }

        public bool IsShadowEnabled
        {
            get => _isShadowEnabled;
            set
            {
                if (_isShadowEnabled != value)
                {
                    _isShadowEnabled = value;
                    OnPropertyChanged();
                    Persist();
                }
            }
        }

        public bool MarqueeEnabled
        {
            get => _marqueeEnabled;
            set
            {
                if (_marqueeEnabled != value)
                {
                    _marqueeEnabled = value;
                    OnPropertyChanged();
                    Persist();
                }
            }
        }

        public double MarqueeSpeedPxPerSecond
        {
            get => _marqueeSpeedPxPerSecond;
            set
            {
                var clamped = double.IsFinite(value) ? Math.Clamp(value, 10.0, 500.0) : 90.0;
                if (!_marqueeSpeedPxPerSecond.Equals(clamped))
                {
                    _marqueeSpeedPxPerSecond = clamped;
                    OnPropertyChanged();
                    Persist();
                }
            }
        }

        public double MarqueeSpacerWidthPx
        {
            get => _marqueeSpacerWidthPx;
            set
            {
                var clamped = double.IsFinite(value) ? Math.Clamp(value, 20.0, 400.0) : 140.0;
                if (!_marqueeSpacerWidthPx.Equals(clamped))
                {
                    _marqueeSpacerWidthPx = clamped;
                    OnPropertyChanged();
                    Persist();
                }
            }
        }

        public int MarqueeCrossfadeMs
        {
            get => _marqueeCrossfadeMs;
            set
            {
                var clamped = Math.Clamp(value, 0, 5000);
                if (_marqueeCrossfadeMs != clamped)
                {
                    _marqueeCrossfadeMs = clamped;
                    OnPropertyChanged();
                    Persist();
                }
            }
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

        public string SidePanelClock
        {
            get => _sidePanelClock;
            private set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_sidePanelClock, newValue, StringComparison.Ordinal))
                {
                    _sidePanelClock = newValue;
                    OnPropertyChanged();
                }
            }
        }

        public string EventNameDisplay
        {
            get => _eventNameDisplay;
            private set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_eventNameDisplay, newValue, StringComparison.Ordinal))
                {
                    _eventNameDisplay = newValue;
                    OnPropertyChanged();
                }
            }
        }

        public string EventVenueDisplay
        {
            get => _eventVenueDisplay;
            private set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_eventVenueDisplay, newValue, StringComparison.Ordinal))
                {
                    _eventVenueDisplay = newValue;
                    OnPropertyChanged();
                }
            }
        }

        public string NowPlayingPrimary
        {
            get => _nowPlayingPrimary;
            private set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_nowPlayingPrimary, newValue, StringComparison.Ordinal))
                {
                    _nowPlayingPrimary = newValue;
                    OnPropertyChanged();
                }
            }
        }

        public string NowPlayingSecondary
        {
            get => _nowPlayingSecondary;
            private set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_nowPlayingSecondary, newValue, StringComparison.Ordinal))
                {
                    _nowPlayingSecondary = newValue;
                    OnPropertyChanged();
                }
            }
        }

        public string UpNextPrimary
        {
            get => _upNextPrimary;
            private set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_upNextPrimary, newValue, StringComparison.Ordinal))
                {
                    _upNextPrimary = newValue;
                    OnPropertyChanged();
                }
            }
        }

        public string UpNextSecondary
        {
            get => _upNextSecondary;
            private set
            {
                var newValue = value ?? string.Empty;
                if (!string.Equals(_upNextSecondary, newValue, StringComparison.Ordinal))
                {
                    _upNextSecondary = newValue;
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
                    OnPropertyChanged(nameof(SidePanelVisibility));
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

        public Brush SidePanelBrush
        {
            get => _sidePanelBrush;
            private set
            {
                if (!ReferenceEquals(_sidePanelBrush, value))
                {
                    _sidePanelBrush = value;
                    OnPropertyChanged();
                }
            }
        }

        public Visibility TopBandVisibility => IsTopEnabled ? Visibility.Visible : Visibility.Collapsed;

        public Visibility BottomBandVisibility => IsBottomEnabled ? Visibility.Visible : Visibility.Collapsed;

        public Visibility SidePanelVisibility => IsBlueState ? Visibility.Collapsed : Visibility.Visible;

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

        public void ResetTemplates()
        {
            var defaults = new OverlayTemplates();
            _suppressSave = true;
            try
            {
                TopTemplatePlayback = defaults.PlaybackTop;
                BottomTemplatePlayback = defaults.PlaybackBottom;
                TopTemplateBlue = defaults.BlueTop;
                BottomTemplateBlue = defaults.BlueBottom;
            }
            finally
            {
                _suppressSave = false;
                Persist();
                UpdateBandText();
            }
        }

        public void UpdatePlaybackState(IReadOnlyList<QueueEntry>? queue, QueueEntry? playhead, EventDto? currentEvent, ReorderMode? matureMode = null)
        {
            _queueSnapshot = queue?.ToList() ?? new List<QueueEntry>();
            _playheadQueueId = playhead?.QueueId;
            _playheadFallback = playhead;
            _currentEvent = currentEvent;

            if (matureMode.HasValue)
            {
                _matureMode = matureMode.Value;
            }

            RecomputeOverlay();
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
            _fontFamilyName = settings.FontFamily ?? _fontFamilyName;
            _fontSize = settings.FontSize;
            _fontWeightName = settings.FontWeight ?? _fontWeightName;
            _fontColor = settings.FontColor ?? _fontColor;
            _isStrokeEnabled = settings.FontStrokeEnabled;
            _isShadowEnabled = settings.FontShadowEnabled;
            _marqueeEnabled = settings.MarqueeEnabled;
            _marqueeSpeedPxPerSecond = settings.MarqueeSpeedPxPerSecond;
            _marqueeSpacerWidthPx = settings.MarqueeSpacerWidthPx;
            _marqueeCrossfadeMs = settings.MarqueeCrossfadeMs;
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
            OnPropertyChanged(nameof(FontFamilyName));
            OnPropertyChanged(nameof(FontFamily));
            OnPropertyChanged(nameof(FontSize));
            OnPropertyChanged(nameof(FontWeightName));
            OnPropertyChanged(nameof(FontWeight));
            OnPropertyChanged(nameof(FontColor));
            OnPropertyChanged(nameof(IsStrokeEnabled));
            OnPropertyChanged(nameof(IsShadowEnabled));
            OnPropertyChanged(nameof(MarqueeEnabled));
            OnPropertyChanged(nameof(MarqueeSpeedPxPerSecond));
            OnPropertyChanged(nameof(MarqueeSpacerWidthPx));
            OnPropertyChanged(nameof(MarqueeCrossfadeMs));
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
            UpdateFontBrush();
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
            SidePanelBrush = CreateSidePanelBrush();
        }

        private void UpdateFontBrush()
        {
            var color = ParseColor(FontColor, Colors.White);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            FontBrush = brush;
        }

        private void UpdateBandText()
        {
            _templateContext.Timestamp = _timeProvider();
            var hasUpNext = !string.IsNullOrWhiteSpace(_templateContext.UpNextRequestor)
                || !string.IsNullOrWhiteSpace(_templateContext.UpNextSong)
                || !string.IsNullOrWhiteSpace(_templateContext.UpNextArtist);

            var topText = _templateEngine.Render(ActiveTopTemplate, _templateContext);
            if (!hasUpNext)
            {
                var prefix = string.IsNullOrWhiteSpace(_templateContext.Brand) ? string.Empty : $"{_templateContext.Brand} • ";
                topText = $"{prefix}UP NEXT: {NoUpcomingPlaceholder}";
            }

            TopBandText = topText;
            BottomBandText = _templateEngine.Render(ActiveBottomTemplate, _templateContext);
            UpdateSidePanelText();
        }

        private void RecomputeOverlay()
        {
            var resolver = new NowNextResolver(_queueSnapshot, GetPlayheadForResolution());
            var now = resolver.ResolveNow();
            var upNext = resolver.ResolveUpNext(_matureMode);

            var shouldUpdate = false;
            shouldUpdate |= ApplyEvent(_currentEvent);
            shouldUpdate |= ApplyNowPlaying(now);
            shouldUpdate |= ApplyUpNext(upNext);

            if (shouldUpdate)
            {
                UpdateBandText();
            }
        }

        private QueueEntry? GetPlayheadForResolution()
        {
            if (_playheadQueueId.HasValue)
            {
                var fromQueue = _queueSnapshot.FirstOrDefault(entry => entry.QueueId == _playheadQueueId.Value);
                if (fromQueue != null)
                {
                    return fromQueue;
                }
            }

            return _playheadFallback;
        }

        private bool ApplyNowPlaying(QueueEntry? entry)
        {
            var requestor = ExtractRequestor(entry);
            var song = entry?.SongTitle ?? string.Empty;
            var artist = entry?.SongArtist ?? string.Empty;

            var changed = !string.Equals(_templateContext.Requestor, requestor, StringComparison.Ordinal)
                || !string.Equals(_templateContext.Song, song, StringComparison.Ordinal)
                || !string.Equals(_templateContext.Artist, artist, StringComparison.Ordinal);

            if (changed)
            {
                _templateContext.Requestor = requestor;
                _templateContext.Song = song;
                _templateContext.Artist = artist;
            }

            return changed;
        }

        private bool ApplyUpNext(QueueEntry? entry)
        {
            string requestor;
            string song;
            string artist;

            if (entry == null)
            {
                requestor = string.Empty;
                song = string.Empty;
                artist = string.Empty;
            }
            else
            {
                requestor = ExtractRequestor(entry);
                song = entry.SongTitle ?? string.Empty;
                artist = entry.SongArtist ?? string.Empty;
            }

            var changed = !string.Equals(_templateContext.UpNextRequestor, requestor, StringComparison.Ordinal)
                || !string.Equals(_templateContext.UpNextSong, song, StringComparison.Ordinal)
                || !string.Equals(_templateContext.UpNextArtist, artist, StringComparison.Ordinal);

            if (changed)
            {
                _templateContext.UpNextRequestor = requestor;
                _templateContext.UpNextSong = song;
                _templateContext.UpNextArtist = artist;
            }

            return changed;
        }

        private bool ApplyEvent(EventDto? evt)
        {
            var eventName = ExtractEventName(evt);
            var venue = evt?.Location ?? string.Empty;

            var changed = !string.Equals(_templateContext.EventName, eventName, StringComparison.Ordinal)
                || !string.Equals(_templateContext.Venue, venue, StringComparison.Ordinal);

            if (changed)
            {
                _templateContext.EventName = eventName;
                _templateContext.Venue = venue;
            }

            return changed;
        }

        private void HandlePreferredReorderModeChanged(object? sender, ReorderMode mode)
        {
            if (_matureMode == mode)
            {
                return;
            }

            _matureMode = mode;
            RecomputeOverlay();
        }

        private static ReorderMode ParseMatureMode(string? value)
        {
            return string.Equals(value, "Allow", StringComparison.OrdinalIgnoreCase)
                ? ReorderMode.AllowMature
                : ReorderMode.DeferMature;
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

        private Brush CreateSidePanelBrush()
        {
            var primary = ParseColor(PrimaryColor, Color.FromRgb(30, 58, 138));
            var secondary = ParseColor(SecondaryColor, Color.FromRgb(59, 130, 246));
            if (!UseGradient)
            {
                var color = WithOpacity(primary, BackgroundOpacity);
                var solid = new SolidColorBrush(color);
                solid.Freeze();
                return solid;
            }

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5)
            };
            brush.GradientStops.Add(new GradientStop(WithOpacity(primary, BackgroundOpacity), 0));
            brush.GradientStops.Add(new GradientStop(WithOpacity(secondary, BackgroundOpacity), 1));
            brush.Freeze();
            return brush;
        }

        private void UpdateSidePanelText()
        {
            SidePanelClock = FormatTime(_templateContext.Timestamp);

            var eventName = string.IsNullOrWhiteSpace(_templateContext.EventName)
                ? BrandText
                : _templateContext.EventName;
            EventNameDisplay = string.IsNullOrWhiteSpace(eventName) ? BrandText : eventName;
            EventVenueDisplay = _templateContext.Venue ?? string.Empty;

            var nowSinger = _templateContext.Requestor;
            var nowSong = _templateContext.Song;
            var nowArtist = _templateContext.Artist;
            if (string.IsNullOrWhiteSpace(nowSinger)
                && string.IsNullOrWhiteSpace(nowSong)
                && string.IsNullOrWhiteSpace(nowArtist))
            {
                NowPlayingPrimary = NoUpcomingPlaceholder;
                NowPlayingSecondary = string.Empty;
            }
            else
            {
                NowPlayingPrimary = string.IsNullOrWhiteSpace(nowSinger) ? NoUpcomingPlaceholder : nowSinger;
                NowPlayingSecondary = ComposeSongLine(nowSong, nowArtist);
            }

            var upNextSinger = _templateContext.UpNextRequestor;
            var upNextSong = _templateContext.UpNextSong;
            var upNextArtist = _templateContext.UpNextArtist;
            if (string.IsNullOrWhiteSpace(upNextSinger)
                && string.IsNullOrWhiteSpace(upNextSong)
                && string.IsNullOrWhiteSpace(upNextArtist))
            {
                UpNextPrimary = NoUpcomingPlaceholder;
                UpNextSecondary = string.Empty;
            }
            else
            {
                UpNextPrimary = string.IsNullOrWhiteSpace(upNextSinger) ? NoUpcomingPlaceholder : upNextSinger;
                UpNextSecondary = ComposeSongLine(upNextSong, upNextArtist);
            }
        }

        private static string ComposeSongLine(string song, string artist)
        {
            if (!string.IsNullOrWhiteSpace(song) && !string.IsNullOrWhiteSpace(artist))
            {
                return $"{song} – {artist}";
            }

            if (!string.IsNullOrWhiteSpace(song))
            {
                return song;
            }

            if (!string.IsNullOrWhiteSpace(artist))
            {
                return artist;
            }

            return string.Empty;
        }

        private static string FormatTime(DateTimeOffset? timestamp)
        {
            var value = timestamp ?? DateTimeOffset.Now;
            return value.ToString("h:mm tt", CultureInfo.InvariantCulture);
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
            settings.Overlay.FontFamily = FontFamilyName;
            settings.Overlay.FontSize = FontSize;
            settings.Overlay.FontWeight = FontWeightName;
            settings.Overlay.FontColor = FontColor;
            settings.Overlay.FontStrokeEnabled = IsStrokeEnabled;
            settings.Overlay.FontShadowEnabled = IsShadowEnabled;
            settings.Overlay.MarqueeEnabled = MarqueeEnabled;
            settings.Overlay.MarqueeSpeedPxPerSecond = MarqueeSpeedPxPerSecond;
            settings.Overlay.MarqueeSpacerWidthPx = MarqueeSpacerWidthPx;
            settings.Overlay.MarqueeCrossfadeMs = MarqueeCrossfadeMs;
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

        private string NormalizeFontWeight(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Bold";
            }

            try
            {
                var converted = _fontWeightConverter.ConvertFromInvariantString(value);
                if (converted is FontWeight fontWeight)
                {
                    return fontWeight.ToString();
                }
            }
            catch
            {
                return "Bold";
            }

            return "Bold";
        }

        private static string NormalizeColor(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "#FFFFFFFF";
            }

            var color = ParseColor(value, Colors.White);
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        private static Color ParseColor(string color, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(color))
            {
                return fallback;
            }

            try
            {
                var converted = ColorConverter.ConvertFromString(color);
                if (converted is Color colorValue)
                {
                    return colorValue;
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
