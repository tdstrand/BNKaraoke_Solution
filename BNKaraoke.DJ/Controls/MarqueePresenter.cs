using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Serilog;

namespace BNKaraoke.DJ.Controls
{
    public class MarqueePresenter : Control
    {
        private const double MaxLoopDurationSeconds = 20.0;
        private const double QualityDowngradeThresholdMs = 40.0;
        private const double QualityRestoreThresholdMs = 24.0;
        private const double ExtremeSpikeThresholdMs = 250.0;
        private const double ResumeFrameThresholdMs = 75.0;
        private const double DefaultShadowBlurRadius = 6.0;
        private const double ReducedShadowBlurRadius = 3.0;

        private Grid? _root;
        private Grid? _currentLayer;
        private Grid? _nextLayer;
        private readonly List<MarqueeVisualState> _activeStates = new();
        private MarqueeVisualState? _currentState;
        private bool _isRenderingHooked;
        private TimeSpan? _lastRenderTime;
        private bool _deferredUpdatePending;
        private readonly HashSet<DropShadowEffect> _activeDropShadows = new();
        private double _smoothedFrameDurationMs = 16.7;
        private bool _qualityReduced;
        private bool _marqueePaused;

        static MarqueePresenter()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(MarqueePresenter), new FrameworkPropertyMetadata(typeof(MarqueePresenter)));
        }

        public MarqueePresenter()
        {
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        #region Dependency Properties

        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(MarqueePresenter),
            new PropertyMetadata(string.Empty, OnTextChanged));

        public static readonly DependencyProperty StrokeEnabledProperty = DependencyProperty.Register(
            nameof(StrokeEnabled),
            typeof(bool),
            typeof(MarqueePresenter),
            new PropertyMetadata(true, OnVisualPropertyChanged));

        public static readonly DependencyProperty ShadowEnabledProperty = DependencyProperty.Register(
            nameof(ShadowEnabled),
            typeof(bool),
            typeof(MarqueePresenter),
            new PropertyMetadata(true, OnVisualPropertyChanged));

        public static readonly DependencyProperty MarqueeEnabledProperty = DependencyProperty.Register(
            nameof(MarqueeEnabled),
            typeof(bool),
            typeof(MarqueePresenter),
            new PropertyMetadata(true, OnVisualPropertyChanged));

        public static readonly DependencyProperty MarqueeSpeedPxPerSecProperty = DependencyProperty.Register(
            nameof(MarqueeSpeedPxPerSec),
            typeof(double),
            typeof(MarqueePresenter),
            new PropertyMetadata(90d, OnVisualPropertyChanged));

        public static readonly DependencyProperty SpacerWidthPxProperty = DependencyProperty.Register(
            nameof(SpacerWidthPx),
            typeof(double),
            typeof(MarqueePresenter),
            new PropertyMetadata(80d, OnVisualPropertyChanged));

        public static readonly DependencyProperty CrossfadeMsProperty = DependencyProperty.Register(
            nameof(CrossfadeMs),
            typeof(int),
            typeof(MarqueePresenter),
            new PropertyMetadata(200));

        public string Text
        {
            get => (string)GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        public bool StrokeEnabled
        {
            get => (bool)GetValue(StrokeEnabledProperty);
            set => SetValue(StrokeEnabledProperty, value);
        }

        public bool ShadowEnabled
        {
            get => (bool)GetValue(ShadowEnabledProperty);
            set => SetValue(ShadowEnabledProperty, value);
        }

        public bool MarqueeEnabled
        {
            get => (bool)GetValue(MarqueeEnabledProperty);
            set => SetValue(MarqueeEnabledProperty, value);
        }

        public double MarqueeSpeedPxPerSec
        {
            get => (double)GetValue(MarqueeSpeedPxPerSecProperty);
            set => SetValue(MarqueeSpeedPxPerSecProperty, value);
        }

        public double SpacerWidthPx
        {
            get => (double)GetValue(SpacerWidthPxProperty);
            set => SetValue(SpacerWidthPxProperty, value);
        }

        public int CrossfadeMs
        {
            get => (int)GetValue(CrossfadeMsProperty);
            set => SetValue(CrossfadeMsProperty, value);
        }

        #endregion

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            _root = GetTemplateChild("PART_Root") as Grid;
            _currentLayer = GetTemplateChild("PART_CurrentLayer") as Grid;
            _nextLayer = GetTemplateChild("PART_NextLayer") as Grid;

            if (_root != null)
            {
                _root.SizeChanged -= Root_SizeChanged;
                _root.SizeChanged += Root_SizeChanged;
            }

            RefreshVisual(immediate: true);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            if (sizeInfo.WidthChanged)
            {
                RefreshVisual(immediate: true);
            }
        }

        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == FontFamilyProperty ||
                e.Property == FontSizeProperty ||
                e.Property == FontWeightProperty ||
                e.Property == ForegroundProperty ||
                e.Property == FlowDirectionProperty)
            {
                RefreshVisual(immediate: true);
            }
        }

        private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarqueePresenter presenter)
            {
                var oldText = e.OldValue as string ?? string.Empty;
                presenter.RefreshVisual(immediate: false, previousText: oldText);
            }
        }

        private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is MarqueePresenter presenter)
            {
                presenter.RefreshVisual(immediate: true);
            }
        }

        private void Root_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged)
            {
                RefreshVisual(immediate: true);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_deferredUpdatePending)
            {
                _deferredUpdatePending = false;
                RefreshVisual(immediate: true);
            }
            EnsureRenderingHook();
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            DetachRenderingHook();
        }

        private void RefreshVisual(bool immediate, string? previousText = null)
        {
            if (_root == null || _currentLayer == null || _nextLayer == null)
            {
                return;
            }

            var availableWidth = Math.Max(0, _root.ActualWidth);
            if (availableWidth <= 0)
            {
                _deferredUpdatePending = true;
                return;
            }

            var newText = Text ?? string.Empty;
            if (!immediate && previousText != null && string.Equals(previousText, newText, StringComparison.Ordinal))
            {
                return;
            }

            var newState = CreateVisualState(newText, availableWidth);
            var canCrossfade = !immediate &&
                               _currentState != null &&
                               CrossfadeMs > 0 &&
                               IsLoaded &&
                               !string.IsNullOrWhiteSpace(previousText) &&
                               !string.IsNullOrWhiteSpace(newText);

            if (!canCrossfade)
            {
                ApplyStateImmediately(newState);
                return;
            }

            BeginCrossfade(newState);
        }

        private void ApplyStateImmediately(MarqueeVisualState newState)
        {
            if (_currentLayer == null || _nextLayer == null)
            {
                return;
            }

            _currentLayer.BeginAnimation(UIElement.OpacityProperty, null);
            _nextLayer.BeginAnimation(UIElement.OpacityProperty, null);

            if (_currentState != null)
            {
                UnregisterDropShadows(_currentState);
                _currentState.StopAnimation();
                if (_currentState.IsMarquee)
                {
                    _activeStates.Remove(_currentState);
                }
            }

            _currentLayer.Children.Clear();
            _currentLayer.Children.Add(newState.Root);
            _currentLayer.Opacity = 1.0;

            RegisterDropShadows(newState);
            newState.StartAnimation();
            if (_marqueePaused)
            {
                newState.PauseAnimation();
            }

            _nextLayer.Children.Clear();
            _nextLayer.Opacity = 0.0;

            _activeStates.Clear();
            if (newState.IsMarquee)
            {
                _activeStates.Add(newState);
            }

            _currentState = newState;
            EnsureRenderingHook();
        }

        private void BeginCrossfade(MarqueeVisualState newState)
        {
            if (_currentLayer == null || _nextLayer == null)
            {
                return;
            }

            var previousState = _currentState;

            _nextLayer.BeginAnimation(UIElement.OpacityProperty, null);
            _nextLayer.Children.Clear();
            _nextLayer.Children.Add(newState.Root);
            _nextLayer.Opacity = 0.0;

            RegisterDropShadows(newState);
            newState.StartAnimation();
            if (_marqueePaused)
            {
                newState.PauseAnimation();
            }

            if (newState.IsMarquee && !_activeStates.Contains(newState))
            {
                _activeStates.Add(newState);
            }

            EnsureRenderingHook();

            var durationMs = Math.Clamp(CrossfadeMs, 150, 250);
            var duration = TimeSpan.FromMilliseconds(durationMs);

            var fadeOut = new DoubleAnimation(0.0, duration) { FillBehavior = FillBehavior.HoldEnd };
            var fadeIn = new DoubleAnimation(1.0, duration) { FillBehavior = FillBehavior.HoldEnd };

            fadeIn.Completed += (_, _) =>
            {
                _currentLayer.BeginAnimation(UIElement.OpacityProperty, null);
                _nextLayer.BeginAnimation(UIElement.OpacityProperty, null);

                _nextLayer.Children.Clear();

                _currentLayer.Children.Clear();
                _currentLayer.Children.Add(newState.Root);
                _currentLayer.Opacity = 1.0;

                _nextLayer.Opacity = 0.0;

                if (previousState != null)
                {
                    UnregisterDropShadows(previousState);
                    previousState.StopAnimation();
                    if (previousState.IsMarquee)
                    {
                        _activeStates.Remove(previousState);
                    }
                }

                _currentState = newState;
                EnsureRenderingHook();
            };

            _currentLayer.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            _nextLayer.BeginAnimation(UIElement.OpacityProperty, fadeIn);
        }

        private MarqueeVisualState CreateVisualState(string text, double availableWidth)
        {
            var root = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true,
                ClipToBounds = false
            };

            var measuredWidth = MeasureTextWidth(text);
            var spacerWidth = Math.Max(0.0, SpacerWidthPx);
            var dropShadows = new List<DropShadowEffect>();
            var marqueeActive = MarqueeEnabled && !string.IsNullOrWhiteSpace(text);

            if (!marqueeActive)
            {
                var staticContent = CreateTextVisual(text, dropShadows);
                staticContent.HorizontalAlignment = HorizontalAlignment.Center;
                root.Children.Add(staticContent);
                return new MarqueeVisualState(root, null, 0.0, 0.0, 0.0, null, null, dropShadows, null, null, false);
            }

            var baseSpeed = Math.Max(10.0, Math.Abs(MarqueeSpeedPxPerSec));

            if (measuredWidth <= availableWidth)
            {
                var entryVisual = CreateTextVisual(text, dropShadows);
                entryVisual.HorizontalAlignment = HorizontalAlignment.Left;

                var transform = new TranslateTransform();
                entryVisual.RenderTransform = transform;

                root.Children.Add(entryVisual);

                var startOffset = availableWidth;
                var targetOffset = Math.Max(0.0, (availableWidth - measuredWidth) / 2.0);
                var travelDistance = Math.Abs(startOffset - targetOffset);
                var entryDurationSecondsCentered = travelDistance > 0.0
                    ? travelDistance / baseSpeed
                    : 0.0;

                transform.X = startOffset;

                var entryAnimation = new DoubleAnimation
                {
                    From = startOffset,
                    To = targetOffset,
                    Duration = new Duration(TimeSpan.FromSeconds(Math.Max(0.1, entryDurationSecondsCentered))),
                    FillBehavior = FillBehavior.HoldEnd
                };
                Timeline.SetDesiredFrameRate(entryAnimation, 60);

                var entryClockCentered = entryAnimation.CreateClock();

                Log.Information(
                    "[MARQUEE] Created entry animation. TextWidth={TextWidth:F1}px, AvailableWidth={AvailableWidth:F1}px, TargetOffset={TargetOffset:F1}px, Duration={Duration:F2}s",
                    measuredWidth,
                    availableWidth,
                    targetOffset,
                    Math.Max(0.1, entryDurationSecondsCentered));

                return new MarqueeVisualState(
                    root,
                    transform,
                    0.0,
                    baseSpeed,
                    Math.Max(0.1, entryDurationSecondsCentered),
                    entryClockCentered,
                    null,
                    dropShadows,
                    startOffset,
                    targetOffset,
                    false);
            }

            var stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true
            };

            var first = CreateTextVisual(text, dropShadows);
            var spacer = new Border
            {
                Width = spacerWidth,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var second = CreateTextVisual(text, dropShadows);

            stackPanel.Children.Add(first);
            stackPanel.Children.Add(spacer);
            stackPanel.Children.Add(second);

            var marqueeTransform = new TranslateTransform();
            stackPanel.RenderTransform = marqueeTransform;

            root.Children.Add(stackPanel);

            var loopTravelDistance = measuredWidth + spacerWidth;
            var totalTravelDistance = loopTravelDistance + Math.Max(0.0, availableWidth);
            var speed = CalculateSpeed(totalTravelDistance);
            var loopDuration = loopTravelDistance > 0 && speed > 0
                ? TimeSpan.FromSeconds(loopTravelDistance / speed)
                : TimeSpan.Zero;

            var entryDistance = Math.Max(0.0, availableWidth);
            var entryDurationSeconds = entryDistance > 0 && speed > 0
                ? entryDistance / speed
                : 0.0;

            AnimationClock? entryClock = null;
            if (entryDistance > 0.0)
            {
                var entryAnimation = new DoubleAnimation
                {
                    From = availableWidth,
                    To = 0.0,
                    Duration = new Duration(TimeSpan.FromSeconds(Math.Max(0.1, entryDurationSeconds))),
                    FillBehavior = FillBehavior.HoldEnd
                };
                Timeline.SetDesiredFrameRate(entryAnimation, 60);
                entryClock = entryAnimation.CreateClock();
            }

            var loopAnimation = new DoubleAnimation
            {
                From = 0.0,
                To = -loopTravelDistance,
                Duration = new Duration(loopDuration > TimeSpan.Zero ? loopDuration : TimeSpan.FromSeconds(1)),
                RepeatBehavior = RepeatBehavior.Forever,
                FillBehavior = FillBehavior.Stop
            };
            Timeline.SetDesiredFrameRate(loopAnimation, 60);

            marqueeTransform.X = entryDistance > 0.0 ? availableWidth : 0.0;

            var loopClock = loopAnimation.CreateClock();

            var overflow = Math.Max(0.0, measuredWidth - availableWidth);
            Log.Information(
                "[MARQUEE] Created marquee state. TextWidth={TextWidth:F1}px, AvailableWidth={AvailableWidth:F1}px, Overflow={Overflow:F1}px, EntryDistance={EntryDistance:F1}px, TravelDistance={TravelDistance:F1}px, Speed={Speed:F1}px/s, LoopDuration={LoopDuration:F2}s",
                measuredWidth,
                availableWidth,
                overflow,
                entryDistance,
                loopTravelDistance,
                speed,
                loopDuration.TotalSeconds);

            return new MarqueeVisualState(
                root,
                marqueeTransform,
                loopTravelDistance,
                speed,
                loopDuration.TotalSeconds,
                entryClock,
                loopClock,
                dropShadows,
                entryDistance > 0.0 ? availableWidth : 0.0,
                0.0,
                true);
        }

        private FrameworkElement CreateTextVisual(string text, ICollection<DropShadowEffect> dropShadows)
        {
            var container = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true
            };

            if (StrokeEnabled)
            {
                foreach (var offset in GetStrokeOffsets())
                {
                    var stroke = CreateTextBlock(text, Brushes.Black);
                    stroke.RenderTransform = new TranslateTransform(offset.X, offset.Y);
                    container.Children.Add(stroke);
                }
            }

            var foreground = Foreground ?? Brushes.White;
            var textBlock = CreateTextBlock(text, foreground);

            if (ShadowEnabled)
            {
                var shadow = new DropShadowEffect
                {
                    BlurRadius = DefaultShadowBlurRadius,
                    Color = Colors.Black,
                    Direction = 315,
                    ShadowDepth = 2,
                    Opacity = 0.75,
                    RenderingBias = RenderingBias.Performance
                };
                dropShadows.Add(shadow);
                textBlock.Effect = shadow;
            }

            container.Children.Add(textBlock);
            return container;
        }

        private IEnumerable<Point> GetStrokeOffsets()
        {
            yield return new Point(-1, 0);
            yield return new Point(1, 0);
            yield return new Point(0, -1);
            yield return new Point(0, 1);
            yield return new Point(-1, -1);
            yield return new Point(1, -1);
            yield return new Point(-1, 1);
            yield return new Point(1, 1);
        }

        private TextBlock CreateTextBlock(string text, Brush brush)
        {
            var block = new TextBlock
            {
                Text = text,
                FontFamily = FontFamily,
                FontSize = FontSize,
                FontWeight = FontWeight,
                Foreground = brush,
                TextTrimming = TextTrimming.None,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextOptions.SetTextFormattingMode(block, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(block, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(block, TextHintingMode.Fixed);

            return block;
        }

        private double MeasureTextWidth(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0.0;
            }

            var brush = Foreground ?? Brushes.White;
            var culture = CultureInfo.CurrentUICulture;
            var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeight, FontStretches.Normal);
            var formatted = new FormattedText(
                text,
                culture,
                FlowDirection,
                typeface,
                FontSize,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            return Math.Ceiling(formatted.WidthIncludingTrailingWhitespace);
        }

        private double CalculateSpeed(double loopWidth)
        {
            if (loopWidth <= 0)
            {
                return 0;
            }

            var baseSpeed = Math.Max(10.0, Math.Abs(MarqueeSpeedPxPerSec));
            var minSpeed = loopWidth / MaxLoopDurationSeconds;
            return Math.Max(baseSpeed, minSpeed);
        }

        private void EnsureRenderingHook()
        {
            if (_activeStates.Any(state => state.IsMarquee))
            {
                if (!_isRenderingHooked)
                {
                    CompositionTarget.Rendering += OnRendering;
                    _isRenderingHooked = true;
                    _lastRenderTime = null;
                }
            }
            else
            {
                DetachRenderingHook();
            }
        }

        private void DetachRenderingHook()
        {
            if (_isRenderingHooked)
            {
                CompositionTarget.Rendering -= OnRendering;
                _isRenderingHooked = false;
                ResetPerformanceState();
            }
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            if (_activeStates.Count == 0)
            {
                DetachRenderingHook();
                return;
            }

            if (e is RenderingEventArgs renderingArgs)
            {
                var current = renderingArgs.RenderingTime;
                if (_lastRenderTime.HasValue)
                {
                    var delta = current - _lastRenderTime.Value;
                    if (delta > TimeSpan.Zero)
                    {
                        var deltaMs = delta.TotalMilliseconds;
                        _smoothedFrameDurationMs = (_smoothedFrameDurationMs * 0.85) + (deltaMs * 0.15);

                        if (deltaMs > ExtremeSpikeThresholdMs && !_marqueePaused)
                        {
                            PauseAnimations();
                            _marqueePaused = true;
                            Log.Warning("[MARQUEE] Extreme frame spike detected ({FrameTimeMs:F1}ms). Pausing marquee animations.", deltaMs);
                        }
                        else if (_marqueePaused && _smoothedFrameDurationMs < ResumeFrameThresholdMs)
                        {
                            ResumeAnimations();
                            _marqueePaused = false;
                            Log.Information("[MARQUEE] Frame time stabilized ({FrameTimeMs:F1}ms). Resuming marquee animations.", _smoothedFrameDurationMs);
                        }

                        if (!_qualityReduced && _smoothedFrameDurationMs > QualityDowngradeThresholdMs)
                        {
                            ReduceShadowQuality(deltaMs);
                        }
                        else if (_qualityReduced && _smoothedFrameDurationMs < QualityRestoreThresholdMs)
                        {
                            RestoreShadowQuality();
                        }
                    }
                }

                _lastRenderTime = current;
            }
        }

        private void RegisterDropShadows(MarqueeVisualState state)
        {
            foreach (var shadow in state.DropShadows)
            {
                if (shadow == null)
                {
                    continue;
                }

                if (_activeDropShadows.Add(shadow))
                {
                    shadow.BlurRadius = _qualityReduced ? ReducedShadowBlurRadius : DefaultShadowBlurRadius;
                    shadow.RenderingBias = RenderingBias.Performance;
                }
            }
        }

        private void UnregisterDropShadows(MarqueeVisualState state)
        {
            foreach (var shadow in state.DropShadows)
            {
                if (shadow != null)
                {
                    _activeDropShadows.Remove(shadow);
                }
            }
        }

        private void ApplyShadowBlur(double blurRadius)
        {
            foreach (var shadow in _activeDropShadows)
            {
                shadow.BlurRadius = blurRadius;
            }
        }

        private void ReduceShadowQuality(double frameTimeMs)
        {
            _qualityReduced = true;
            ApplyShadowBlur(ReducedShadowBlurRadius);
            Log.Warning(
                "[MARQUEE] Frame time {FrameTimeMs:F1}ms exceeded {Threshold:F1}ms. Reducing shadow blur to {BlurRadius}.",
                frameTimeMs,
                QualityDowngradeThresholdMs,
                ReducedShadowBlurRadius);
        }

        private void RestoreShadowQuality()
        {
            _qualityReduced = false;
            ApplyShadowBlur(DefaultShadowBlurRadius);
            Log.Information(
                "[MARQUEE] Frame times recovered ({FrameTimeMs:F1}ms). Restoring shadow blur to {BlurRadius}.",
                _smoothedFrameDurationMs,
                DefaultShadowBlurRadius);
        }

        private void PauseAnimations()
        {
            foreach (var state in _activeStates)
            {
                state.PauseAnimation();
            }
        }

        private void ResumeAnimations()
        {
            foreach (var state in _activeStates)
            {
                state.ResumeAnimation();
            }
        }

        private void ResetPerformanceState()
        {
            _lastRenderTime = null;
            _smoothedFrameDurationMs = 16.7;
            if (_qualityReduced)
            {
                _qualityReduced = false;
                ApplyShadowBlur(DefaultShadowBlurRadius);
            }

            if (_marqueePaused)
            {
                _marqueePaused = false;
                ResumeAnimations();
            }
        }

        private sealed class MarqueeVisualState
        {
            public MarqueeVisualState(
                FrameworkElement root,
                TranslateTransform? transform,
                double travelDistance,
                double speed,
                double loopDurationSeconds,
                AnimationClock? entryClock,
                AnimationClock? loopClock,
                IReadOnlyList<DropShadowEffect> dropShadows,
                double? initialOffset,
                double? finalOffset,
                bool isLooping)
            {
                Root = root;
                Transform = transform;
                TravelDistance = travelDistance;
                Speed = speed;
                LoopDurationSeconds = loopDurationSeconds;
                _entryClock = entryClock;
                _loopClock = loopClock;
                DropShadows = dropShadows?.ToArray() ?? Array.Empty<DropShadowEffect>();
                InitialOffset = initialOffset;
                FinalOffset = finalOffset;
                IsMarquee = isLooping && Transform != null && (_loopClock != null || _entryClock != null);

                if (_entryClock != null)
                {
                    _entryClock.Completed += OnEntryCompleted;
                }
            }

            public FrameworkElement Root { get; }
            public TranslateTransform? Transform { get; }
            public double TravelDistance { get; }
            public double Speed { get; }
            public double LoopDurationSeconds { get; }
            public IReadOnlyList<DropShadowEffect> DropShadows { get; }
            public double? InitialOffset { get; private set; }
            public double? FinalOffset { get; }
            public bool IsMarquee { get; }

            public void StartAnimation()
            {
                if (Transform == null)
                {
                    return;
                }

                Transform.ApplyAnimationClock(TranslateTransform.XProperty, null);
                if (InitialOffset.HasValue)
                {
                    Transform.X = InitialOffset.Value;
                }

                if (_entryClock != null)
                {
                    Transform.ApplyAnimationClock(TranslateTransform.XProperty, _entryClock);
                    _entryClock.Controller?.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
                    _entryClock.Controller?.Begin();
                }
                else if (_loopClock != null)
                {
                    Transform.ApplyAnimationClock(TranslateTransform.XProperty, _loopClock);
                    _loopClock.Controller?.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
                    _loopClock.Controller?.Begin();
                }
            }

            public void PauseAnimation()
            {
                _entryClock?.Controller?.Pause();
                _loopClock?.Controller?.Pause();
            }

            public void ResumeAnimation()
            {
                _entryClock?.Controller?.Resume();
                _loopClock?.Controller?.Resume();
            }

            public void StopAnimation()
            {
                if (_entryClock != null)
                {
                    _entryClock.Completed -= OnEntryCompleted;
                    _entryClock.Controller?.Stop();
                    _entryClock = null;
                }

                _loopClock?.Controller?.Stop();

                if (Transform != null)
                {
                    Transform.ApplyAnimationClock(TranslateTransform.XProperty, null);
                    Transform.X = InitialOffset ?? 0.0;
                }
            }

            private void OnEntryCompleted(object? sender, EventArgs e)
            {
                if (_entryClock != null)
                {
                    _entryClock.Completed -= OnEntryCompleted;
                    _entryClock = null;
                }

                if (Transform == null)
                {
                    return;
                }

                Transform.ApplyAnimationClock(TranslateTransform.XProperty, null);
                var finalOffset = FinalOffset ?? 0.0;
                Transform.X = finalOffset;
                InitialOffset = finalOffset;

                if (_loopClock != null)
                {
                    Transform.ApplyAnimationClock(TranslateTransform.XProperty, _loopClock);
                    _loopClock.Controller?.SeekAlignedToLastTick(TimeSpan.Zero, TimeSeekOrigin.BeginTime);
                    _loopClock.Controller?.Begin();
                }
            }

            private AnimationClock? _entryClock;
            private readonly AnimationClock? _loopClock;
        }
    }
}
