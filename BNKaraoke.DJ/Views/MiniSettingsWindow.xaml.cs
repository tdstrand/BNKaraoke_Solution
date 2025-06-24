using System;
using System.Windows;

namespace BNKaraoke.DJ.Views;

public partial class MiniSettingsWindow : Window
{
    public string? BaseUrl { get; private set; }

    public MiniSettingsWindow()
    {
        InitializeComponent();
        BaseUrlTextBox.Text = "http://localhost:7290"; // Default
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
            MessageBox.Show("Invalid URL format. Please enter a valid URL (e.g., http://localhost:7290)", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}