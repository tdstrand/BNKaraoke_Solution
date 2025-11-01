using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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

            // Swallow Enter/Escape/Tab at the window level when no control can process them
            PreviewKeyDown += LoginWindow_PreviewKeyDown;
        }

        private void LoginWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!ShouldSuppressKey(e) || e.Handled)
            {
                return;
            }

            if (Keyboard.FocusedElement is IInputElement focusedElement &&
                FocusedElementHandlesKey(focusedElement, e))
            {
                return;
            }

            e.Handled = true;
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

        private static bool ShouldSuppressKey(KeyEventArgs e)
        {
            return e.Key is Key.Enter or Key.Return or Key.Escape or Key.Tab;
        }

        private bool FocusedElementHandlesKey(IInputElement focusedElement, KeyEventArgs e)
        {
            switch (focusedElement)
            {
                case TextBoxBase:
                case PasswordBox:
                    if (e.Key is Key.Enter or Key.Return)
                    {
                        if (DataContext is LoginWindowViewModel vm)
                        {
                            if (!vm.CanLogin || !CanExecuteLoginCommand(vm))
                            {
                                e.Handled = true;
                            }
                        }
                    }
                    return true;
                case ComboBox:
                    return true;
                case Selector selector:
                    return selector.IsEnabled;
                case ButtonBase buttonBase:
                    if (!buttonBase.IsEnabled || !IsCommandExecutable(buttonBase))
                    {
                        e.Handled = true;
                    }
                    return true;
                case UIElement uiElement when uiElement.IsEnabled && uiElement.Focusable:
                    return e.Key == Key.Tab || e.Key == Key.Escape;
                default:
                    return false;
            }
        }

        private static bool CanExecuteLoginCommand(LoginWindowViewModel vm)
        {
            var command = vm.LoginCommand;
            if (command == null)
            {
                return vm.CanLogin;
            }

            try
            {
                return command.CanExecute(null);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsCommandExecutable(ButtonBase buttonBase)
        {
            if (buttonBase is not ICommandSource commandSource)
            {
                return buttonBase.IsEnabled;
            }

            var command = commandSource.Command;
            if (command == null)
            {
                return buttonBase.IsEnabled;
            }

            var parameter = commandSource.CommandParameter;
            var target = commandSource.CommandTarget ?? buttonBase;

            try
            {
                return command switch
                {
                    RoutedCommand routedCommand => routedCommand.CanExecute(parameter, target),
                    _ => command.CanExecute(parameter)
                };
            }
            catch
            {
                return false;
            }
        }
    }
}
