using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using BNKaraoke.DJ.ViewModels.Overlays;
using Xunit;

namespace BNKaraoke.DJ.Tests
{
    public class OverlayLayoutIntegrationTests
    {
        [WpfFact]
        public async Task OverlayBandsRespectConfiguredHeightsAsync()
        {
            await WpfTestHelper.RunAsync(async () =>
            {
                var viewModel = CreateViewModel();
                viewModel.TopHeightPercent = 0.25;
                viewModel.BottomHeightPercent = 0.15;
                viewModel.IsTopEnabled = true;
                viewModel.IsBottomEnabled = true;

                var grid = new Grid
                {
                    Width = 1920,
                    Height = 1080,
                    DataContext = viewModel
                };

                var topRow = new RowDefinition();
                var centerRow = new RowDefinition();
                var bottomRow = new RowDefinition();

                grid.RowDefinitions.Add(topRow);
                grid.RowDefinitions.Add(centerRow);
                grid.RowDefinitions.Add(bottomRow);

                BindRowHeight(topRow, nameof(OverlayViewModel.TopRowHeight));
                BindRowHeight(centerRow, nameof(OverlayViewModel.CenterRowHeight));
                BindRowHeight(bottomRow, nameof(OverlayViewModel.BottomRowHeight));

                var window = new Window
                {
                    Width = grid.Width,
                    Height = grid.Height,
                    Content = grid,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent
                };

                window.Show();
                await WpfTestHelper.WaitForLoadedAsync(grid);

                grid.Measure(new Size(grid.Width, grid.Height));
                grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
                grid.UpdateLayout();

                var expectedTop = grid.Height * viewModel.TopHeightPercent;
                var expectedBottom = grid.Height * viewModel.BottomHeightPercent;

                Assert.InRange(topRow.ActualHeight, expectedTop - 1, expectedTop + 1);
                Assert.InRange(bottomRow.ActualHeight, expectedBottom - 1, expectedBottom + 1);
                Assert.InRange(centerRow.ActualHeight, 0, grid.Height);

                window.Close();
            });
        }

        private static void BindRowHeight(RowDefinition rowDefinition, string property)
        {
            var binding = new System.Windows.Data.Binding(property)
            {
                Mode = System.Windows.Data.BindingMode.OneWay
            };
            System.Windows.Data.BindingOperations.SetBinding(rowDefinition, RowDefinition.HeightProperty, binding);
        }

        private static OverlayViewModel CreateViewModel()
        {
            var type = typeof(OverlayViewModel);
            var instance = (OverlayViewModel)Activator.CreateInstance(type, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, Array.Empty<object?>(), null)!;
            var suppressField = type.GetField("_suppressSave", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            suppressField?.SetValue(instance, true);
            return instance;
        }
    }
}
