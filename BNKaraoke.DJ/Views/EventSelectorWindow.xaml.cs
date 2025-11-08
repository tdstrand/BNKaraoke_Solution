using System.Windows;
using System.Windows.Input;
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

            // Swallow Enter/Escape at window level to avoid beeps when no default/cancel button is focused
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Return or Key.Escape)
                {
                    e.Handled = true;
                }
            };
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (EventComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select an event.", "Error", MessageBoxButton.OK, MessageBoxImage.None);
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