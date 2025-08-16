using System.Windows;
using BNKaraoke.DJ.ViewModels;
using Serilog;

namespace BNKaraoke.DJ.Views
{
    public partial class EventSelectorWindow : Window
    {
        public EventSelectorWindow(DJScreenViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            Log.Information("[EVENT SELECTOR] Initialized with {Count} live events", viewModel.LiveEvents.Count);
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (EventComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select an event.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Log.Warning("[EVENT SELECTOR] OK clicked with no event selected");
                return;
            }
            DialogResult = true;
            Log.Information("[EVENT SELECTOR] OK clicked, selected event: {EventCode}", (DataContext as DJScreenViewModel)?.SelectedEvent?.EventCode);
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Log.Information("[EVENT SELECTOR] Cancel clicked");
            Close();
        }
    }
}