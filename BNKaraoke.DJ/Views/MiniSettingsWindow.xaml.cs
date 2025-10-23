using System;
using System.Windows;
using BNKaraoke.DJ.Services;

namespace BNKaraoke.DJ.Views
{
    public partial class MiniSettingsWindow : Window
    {
        private readonly SettingsService _settingsService = SettingsService.Instance;
        public string? BaseUrl { get; private set; }

        public MiniSettingsWindow()
        {
            InitializeComponent();
            BaseUrlTextBox.Text = _settingsService.Settings.ApiUrl; // Use current ApiUrl
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var uri = new Uri(BaseUrlTextBox.Text);
                BaseUrl = BaseUrlTextBox.Text;
                DialogResult = true;
                Close();
            }
            catch (UriFormatException)
            {
                MessageBox.Show("Invalid URL format. Please enter a valid URL (e.g., http://localhost:7290)", "Error", MessageBoxButton.OK, MessageBoxImage.None);
            }
        }
    }
}