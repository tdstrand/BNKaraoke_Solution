using System;
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
    }
}
