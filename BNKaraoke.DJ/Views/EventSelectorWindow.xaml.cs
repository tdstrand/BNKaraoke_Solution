using System.Windows;

namespace BNKaraoke.DJ.Views
{
    public partial class EventSelectorWindow : Window
    {
        public EventSelectorWindow()
        {
            InitializeComponent();
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (EventComboBox.SelectedItem == null)
            {
                MessageBox.Show("Please select an event.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}