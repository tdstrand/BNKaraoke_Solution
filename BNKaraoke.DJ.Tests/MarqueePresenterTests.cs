using System;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
    }
}
