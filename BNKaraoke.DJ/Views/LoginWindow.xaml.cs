using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BNKaraoke.DJ.ViewModels;

namespace BNKaraoke.DJ.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            DataContext = new LoginWindowViewModel();

            // Defer focus until the window is fully ready to avoid default beep on startup
            Loaded += (s, e) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    try { LoginBox.Focus(); } catch { /* ignore */ }
                }, System.Windows.Threading.DispatcherPriority.ContextIdle);
            };

            // Swallow Enter/Escape at window level unless a text input control is focused
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Return or Key.Escape)
                {
                    var focused = Keyboard.FocusedElement;
                    if (focused is TextBox || focused is PasswordBox)
                    {
                        return; // let text inputs handle their own keys
                    }
                    e.Handled = true;
                }
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