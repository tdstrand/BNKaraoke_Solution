using BNKaraoke.DJ.ViewModels.Settings;
using System.Windows;
using System.Windows.Controls;

namespace BNKaraoke.DJ.Views.Settings
{
    public partial class OverlaySettingsView : UserControl
    {
        public OverlaySettingsView()
        {
            InitializeComponent();
            Unloaded += OverlaySettingsView_Unloaded;
        }

        private void OverlaySettingsView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is OverlaySettingsViewModel viewModel)
            {
                viewModel.Dispose();
            }
        }
    }
}
