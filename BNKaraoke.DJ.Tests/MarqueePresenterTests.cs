using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using BNKaraoke.DJ.Controls;
using Xunit;

namespace BNKaraoke.DJ.Tests
{
    public class MarqueePresenterTests
    {
        [WpfFact]
        public async Task StaticAndMarqueeModesRespondToTextLengthAsync()
        {
            await WpfTestHelper.RunAsync(async () =>
            {
                var presenter = new MarqueePresenter
                {
                    Width = 400,
                    Height = 80,
                    CrossfadeMs = 0,
                    MarqueeSpeedPxPerSec = 120,
                    SpacerWidthPx = 60,
                    Margin = new Thickness(0)
                };

                var window = new Window
                {
                    Width = presenter.Width,
                    Height = presenter.Height,
                    Content = presenter,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent
                };

                window.Show();
                await WpfTestHelper.WaitForLoadedAsync(presenter);

                presenter.Text = "Short text";
                presenter.UpdateLayout();
                await WpfTestHelper.WaitForIdleAsync(presenter.Dispatcher);

                var currentLayer = (Grid)presenter.Template.FindName("PART_CurrentLayer", presenter);
                Assert.NotNull(currentLayer);
                Assert.Equal(1, currentLayer.Children.Count);
                var staticRoot = Assert.IsType<Grid>(currentLayer.Children[0]);
                Assert.NotEmpty(staticRoot.Children);
                Assert.IsNotType<StackPanel>(staticRoot.Children[0]);

                presenter.Text = new string('A', 200);
                presenter.UpdateLayout();
                await WpfTestHelper.WaitForIdleAsync(presenter.Dispatcher);

                currentLayer = (Grid)presenter.Template.FindName("PART_CurrentLayer", presenter);
                Assert.Equal(1, currentLayer.Children.Count);
                var marqueeRoot = Assert.IsType<Grid>(currentLayer.Children[0]);
                Assert.IsType<StackPanel>(marqueeRoot.Children[0]);

                window.Close();
            });
        }

        [WpfFact]
        public async Task LongTextCreatesManualMarqueeCopiesAsync()
        {
            await WpfTestHelper.RunAsync(async () =>
            {
                var presenter = new MarqueePresenter
                {
                    Width = 720,
                    Height = 96,
                    CrossfadeMs = 0,
                    MarqueeSpeedPxPerSec = 140,
                    SpacerWidthPx = 10,
                    Margin = new Thickness(0)
                };

                var window = new Window
                {
                    Width = presenter.Width,
                    Height = presenter.Height,
                    Content = presenter,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent
                };

                window.Show();
                await WpfTestHelper.WaitForLoadedAsync(presenter);

                var longText = new string('B', 240);
                presenter.Text = longText;
                presenter.UpdateLayout();
                await WpfTestHelper.WaitForIdleAsync(presenter.Dispatcher);

                var state = GetCurrentState(presenter);
                var stateType = state.GetType();
                var root = Assert.IsType<Grid>(stateType.GetProperty("Root")!.GetValue(state)!);
                var canvas = Assert.IsType<Canvas>(root.Children[0]);

                Assert.True(canvas.Children.Count >= 3);

                var first = Assert.IsAssignableFrom<FrameworkElement>(canvas.Children[0]);
                var second = Assert.IsAssignableFrom<FrameworkElement>(canvas.Children[1]);

                var spacing = Canvas.GetLeft(second) - Canvas.GetLeft(first) - first.ActualWidth;
                var minimumSpacing = MeasureFiveCharacterSpacing(presenter);

                Assert.True(spacing >= minimumSpacing - 1.0, $"Expected spacing >= {minimumSpacing:F1}px but measured {spacing:F1}px");

                foreach (FrameworkElement child in canvas.Children)
                {
                    var textBlock = FindFirstTextBlock(child);
                    Assert.NotNull(textBlock);
                    Assert.Equal(longText, textBlock!.Text);
                }

                window.Close();
            });
        }

        [WpfFact]
        public async Task ManualMarqueeAdvanceRepositionsItemsAsync()
        {
            await WpfTestHelper.RunAsync(async () =>
            {
                var presenter = new MarqueePresenter
                {
                    Width = 640,
                    Height = 96,
                    CrossfadeMs = 0,
                    MarqueeSpeedPxPerSec = 180,
                    SpacerWidthPx = 15,
                    Margin = new Thickness(0)
                };

                var window = new Window
                {
                    Width = presenter.Width,
                    Height = presenter.Height,
                    Content = presenter,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent
                };

                window.Show();
                await WpfTestHelper.WaitForLoadedAsync(presenter);

                var text = new string('D', 260);
                presenter.Text = text;
                presenter.UpdateLayout();
                await WpfTestHelper.WaitForIdleAsync(presenter.Dispatcher);

                var state = GetCurrentState(presenter);
                var stateType = state.GetType();
                var root = Assert.IsType<Grid>(stateType.GetProperty("Root")!.GetValue(state)!);
                var canvas = Assert.IsType<Canvas>(root.Children[0]);

                var initialOffsets = canvas.Children.Cast<FrameworkElement>().Select(child => Canvas.GetLeft(child)).ToArray();

                var advanceMethod = stateType.GetMethod("Advance", BindingFlags.Instance | BindingFlags.Public);
                Assert.NotNull(advanceMethod);
                var loopDuration = (double)stateType.GetProperty("LoopDurationSeconds")!.GetValue(state)!;

                advanceMethod!.Invoke(state, new object[] { loopDuration + (loopDuration * 0.5) });
                await WpfTestHelper.WaitForIdleAsync(presenter.Dispatcher);

                var updatedOffsets = canvas.Children.Cast<FrameworkElement>().Select(child => Canvas.GetLeft(child)).ToArray();

                Assert.False(initialOffsets.SequenceEqual(updatedOffsets));

                foreach (FrameworkElement child in canvas.Children)
                {
                    var textBlock = FindFirstTextBlock(child);
                    Assert.NotNull(textBlock);
                    Assert.Equal(text, textBlock!.Text);
                }

                window.Close();
            });
        }

        private static object GetCurrentState(MarqueePresenter presenter)
        {
            var field = typeof(MarqueePresenter).GetField("_currentState", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var state = field!.GetValue(presenter);
            Assert.NotNull(state);
            return state!;
        }

        private static TextBlock? FindFirstTextBlock(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock textBlock)
                {
                    return textBlock;
                }

                var result = FindFirstTextBlock(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }

        private static double MeasureFiveCharacterSpacing(MarqueePresenter presenter)
        {
            var sample = new string(' ', 5);
            var typeface = new Typeface(presenter.FontFamily, FontStyles.Normal, presenter.FontWeight, FontStretches.Normal);
            var formatted = new FormattedText(
                sample,
                CultureInfo.CurrentUICulture,
                presenter.FlowDirection,
                typeface,
                presenter.FontSize,
                presenter.Foreground ?? Brushes.White,
                VisualTreeHelper.GetDpi(presenter).PixelsPerDip);

            return formatted.WidthIncludingTrailingWhitespace;
        }

        [WpfFact]
        public async Task TextChangesCrossfadeBetweenStatesAsync()
        {
            await WpfTestHelper.RunAsync(async () =>
            {
                var presenter = new MarqueePresenter
                {
                    Width = 400,
                    Height = 80,
                    CrossfadeMs = 200,
                    MarqueeSpeedPxPerSec = 120,
                    SpacerWidthPx = 60,
                    Margin = new Thickness(0)
                };

                var window = new Window
                {
                    Width = presenter.Width,
                    Height = presenter.Height,
                    Content = presenter,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent
                };

                window.Show();
                await WpfTestHelper.WaitForLoadedAsync(presenter);

                presenter.Text = "Initial marquee text that is quite long to ensure scrolling";
                presenter.UpdateLayout();
                await WpfTestHelper.WaitForIdleAsync(presenter.Dispatcher);

                var currentLayer = (Grid)presenter.Template.FindName("PART_CurrentLayer", presenter);
                var nextLayer = (Grid)presenter.Template.FindName("PART_NextLayer", presenter);
                Assert.NotNull(currentLayer);
                Assert.NotNull(nextLayer);
                var originalVisual = currentLayer.Children[0];
                Assert.Equal(0, nextLayer.Children.Count);

                presenter.Text = "Updated marquee text that should trigger crossfade";
                presenter.UpdateLayout();

                var beganCrossfade = await WpfTestHelper.WaitForConditionAsync(
                    () => nextLayer.Children.Count > 0,
                    presenter.Dispatcher,
                    TimeSpan.FromMilliseconds(300));

                Assert.True(beganCrossfade);
                Assert.NotEqual(0, nextLayer.Children.Count);

                await Task.Delay(presenter.CrossfadeMs + 150);
                await WpfTestHelper.WaitForIdleAsync(presenter.Dispatcher);

                Assert.Equal(0, nextLayer.Children.Count);
                Assert.NotSame(originalVisual, currentLayer.Children[0]);

                window.Close();
            });
        }

        [WpfFact]
        public async Task ShortTextStopsCenteredAfterEntryAnimationAsync()
        {
            await WpfTestHelper.RunAsync(async () =>
            {
                var presenter = new MarqueePresenter
                {
                    Width = 640,
                    Height = 96,
                    CrossfadeMs = 0,
                    MarqueeSpeedPxPerSec = 180,
                    SpacerWidthPx = 60,
                    Margin = new Thickness(0)
                };

                var window = new Window
                {
                    Width = presenter.Width,
                    Height = presenter.Height,
                    Content = presenter,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent
                };

                window.Show();
                await WpfTestHelper.WaitForLoadedAsync(presenter);

                presenter.Text = "Center me";
                presenter.UpdateLayout();
                await WpfTestHelper.WaitForIdleAsync(presenter.Dispatcher);

                var currentLayer = (Grid)presenter.Template.FindName("PART_CurrentLayer", presenter);
                var root = Assert.IsType<Grid>(currentLayer.Children[0]);
                var content = Assert.IsAssignableFrom<FrameworkElement>(root.Children[0]);
                var transform = Assert.IsType<TranslateTransform>(content.RenderTransform);

                var availableWidth = presenter.ActualWidth;
                var textWidth = content.ActualWidth;
                var expectedOffset = Math.Max(0.0, (availableWidth - textWidth) / 2.0);
                var travelDistance = Math.Abs(availableWidth - expectedOffset);
                var durationSeconds = travelDistance / presenter.MarqueeSpeedPxPerSec;

                await Task.Delay(TimeSpan.FromSeconds(durationSeconds + 0.3));
                await WpfTestHelper.WaitForIdleAsync(presenter.Dispatcher);

                var tolerance = 1.5;
                Assert.InRange(transform.X, expectedOffset - tolerance, expectedOffset + tolerance);

                window.Close();
            });
        }

        [WpfFact]
        public async Task LongTextBeginsOffScreenBeforeMarqueeLoopAsync()
        {
            await WpfTestHelper.RunAsync(async () =>
            {
                var presenter = new MarqueePresenter
                {
                    Width = 480,
                    Height = 96,
                    CrossfadeMs = 0,
                    MarqueeSpeedPxPerSec = 90,
                    SpacerWidthPx = 60,
                    Margin = new Thickness(0)
                };

                var window = new Window
                {
                    Width = presenter.Width,
                    Height = presenter.Height,
                    Content = presenter,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent
                };

                window.Show();
                await WpfTestHelper.WaitForLoadedAsync(presenter);

                var longText = new string('A', 200);
                presenter.Text = longText;
                presenter.UpdateLayout();
                await WpfTestHelper.WaitForIdleAsync(presenter.Dispatcher);

                var stateField = typeof(MarqueePresenter).GetField("_currentState", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(stateField);
                var state = stateField!.GetValue(presenter);
                Assert.NotNull(state);

                var stateType = state!.GetType();
                var initialOffset = (double?)stateType.GetProperty("InitialOffset")?.GetValue(state);
                var finalOffset = (double?)stateType.GetProperty("FinalOffset")?.GetValue(state);
                var isMarquee = (bool)stateType.GetProperty("IsMarquee")!.GetValue(state)!;

                Assert.True(isMarquee);
                Assert.True(initialOffset.HasValue && initialOffset.Value > 0);
                Assert.Equal(0.0, finalOffset ?? 0.0, 3);

                window.Close();
            });
        }

        [WpfFact]
        public async Task LongTextUpdatesClampInitialOffsetToVisibleRangeAsync()
        {
            await WpfTestHelper.RunAsync(async () =>
            {
                var presenter = new MarqueePresenter
                {
                    Width = 600,
                    Height = 96,
                    CrossfadeMs = 0,
                    MarqueeSpeedPxPerSec = 140,
                    SpacerWidthPx = 40,
                    Margin = new Thickness(0)
                };

                var window = new Window
                {
                    Width = presenter.Width,
                    Height = presenter.Height,
                    Content = presenter,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent
                };

                window.Show();
                await WpfTestHelper.WaitForLoadedAsync(presenter);

                presenter.Text = new string('X', 220);
                presenter.UpdateLayout();
                await WpfTestHelper.WaitForIdleAsync(presenter.Dispatcher);

                var initialState = GetCurrentState(presenter);
                var stateType = initialState.GetType();
                var transform = Assert.IsType<TranslateTransform>(stateType.GetProperty("Transform")!.GetValue(initialState)!);
                Assert.True(transform.X > 0);

                presenter.Text = new string('Y', 210);
                presenter.UpdateLayout();
                await WpfTestHelper.WaitForIdleAsync(presenter.Dispatcher);

                var updatedState = GetCurrentState(presenter);
                var updatedType = updatedState.GetType();
                var initialOffset = (double?)updatedType.GetProperty("InitialOffset")?.GetValue(updatedState);
                Assert.True(initialOffset.HasValue);
                Assert.True(initialOffset!.Value <= 0.0);

                var updatedTransform = Assert.IsType<TranslateTransform>(updatedType.GetProperty("Transform")!.GetValue(updatedState)!);
                Assert.Equal(initialOffset.Value, updatedTransform.X, 3);

                window.Close();
            });
        }
    }
}
