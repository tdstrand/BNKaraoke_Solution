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
            Loaded += (s, e) =>
            {
                try { LoginBox.Focus(); } catch { /* ignore */ }
            };
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginWindowViewModel viewModel && sender is PasswordBox passwordBox)
            {
                viewModel.Password = passwordBox.Password;
                Serilog.Log.Information("[LOGIN] PasswordBox changed: PasswordLength={Length}, CanLogin={CanLogin}", passwordBox.Password.Length, viewModel.CanLogin);
            }
        }

        private void PasswordBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Return || e.Key == System.Windows.Input.Key.Enter)
            {
                if (DataContext is LoginWindowViewModel vm && vm.CanLogin)
                {
                    // Invoke command and suppress default key beep behavior
                    if (vm.LoginCommand.CanExecute(null))
                    {
                        vm.LoginCommand.Execute(null);
                        e.Handled = true;
                    }
                }
                else
                {
                    // Suppress system beep when Enter is pressed but cannot login yet
                    e.Handled = true;
                }
            }
        }
    }
}