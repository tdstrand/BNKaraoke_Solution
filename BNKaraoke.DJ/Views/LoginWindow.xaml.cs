using System.Windows;
using System.Windows.Controls;
using BNKaraoke.DJ.ViewModels;

namespace BNKaraoke.DJ.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            DataContext = new LoginWindowViewModel();
            LoginBox.Focus();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginWindowViewModel viewModel && sender is PasswordBox passwordBox)
            {
                viewModel.Password = passwordBox.Password;
                Serilog.Log.Information("[LOGIN] PasswordBox changed: PasswordLength={Length}, CanLogin={CanLogin}", passwordBox.Password.Length, viewModel.CanLogin);
            }
        }
    }
}