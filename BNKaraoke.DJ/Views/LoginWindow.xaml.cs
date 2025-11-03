using BNKaraoke.DJ.ViewModels;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BNKaraoke.DJ.Views
{
    public partial class LoginWindow : Window
    {
        public LoginWindow()
        {
            InitializeComponent();
            DataContext = new LoginWindowViewModel();
            PreviewKeyDown += LoginWindow_PreviewKeyDown;
        }

        private void LoginWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is not (Key.Enter or Key.Return) || e.Handled)
            {
                return;
            }

            if (DataContext is not LoginWindowViewModel viewModel)
            {
                return;
            }

            var loginCommand = viewModel.LoginCommand;
            if (loginCommand == null)
            {
                return;
            }

            if (!loginCommand.CanExecute(null))
            {
                e.Handled = true;
            }
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is LoginWindowViewModel viewModel && sender is PasswordBox passwordBox)
            {
                viewModel.Password = passwordBox.Password;
            }
        }

        private void PasswordBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Enter or Key.Return)
            {
                TryExecuteLoginCommand();
                e.Handled = true;
            }
        }

        private void PhoneNumberTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (!e.Text.All(char.IsDigit))
            {
                e.Handled = true;
                return;
            }

            if (sender is not TextBox textBox)
            {
                return;
            }

            if (WouldExceedMaxDigits(textBox, e.Text.Count(char.IsDigit)))
            {
                e.Handled = true;
            }
        }

        private void PhoneNumberTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key is Key.Enter or Key.Return)
            {
                TryExecuteLoginCommand();
                e.Handled = true;
                return;
            }

            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                return;
            }

            if (e.Key is Key.Back or Key.Delete or Key.Tab or Key.Left or Key.Right or Key.Home or Key.End)
            {
                return;
            }

            var isDigitKey = IsDigitKey(e.Key);
            if (!isDigitKey)
            {
                e.Handled = true;
                return;
            }

            if (sender is TextBox textBox && WouldExceedMaxDigits(textBox, 1))
            {
                e.Handled = true;
            }
        }

        private void PhoneNumberTextBox_OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }

            var pastedText = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            var digits = new string(pastedText.Where(char.IsDigit).ToArray());

            if (sender is TextBox textBox)
            {
                var existingDigits = new string(textBox.Text.Where(char.IsDigit).ToArray());
                var selectedDigits = new string(textBox.SelectedText.Where(char.IsDigit).ToArray());
                var availableSpace = Math.Max(0, 10 - (existingDigits.Length - selectedDigits.Length));
                if (digits.Length > availableSpace)
                {
                    digits = digits[..availableSpace];
                }
            }

            if (string.IsNullOrEmpty(digits))
            {
                e.CancelCommand();
            }
            else
            {
                e.DataObject = new DataObject(DataFormats.Text, digits);
            }
        }

        private void TryExecuteLoginCommand()
        {
            if (DataContext is LoginWindowViewModel vm && vm.LoginCommand?.CanExecute(null) == true)
            {
                vm.LoginCommand.Execute(null);
            }
        }

        private static bool IsDigitKey(Key key)
        {
            var hasShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            if (key is >= Key.NumPad0 and <= Key.NumPad9)
            {
                return true;
            }

            if (key is >= Key.D0 and <= Key.D9)
            {
                return !hasShift;
            }

            return false;
        }

        private static bool WouldExceedMaxDigits(TextBox textBox, int digitsToAdd)
        {
            var existingDigits = textBox.Text.Count(char.IsDigit);
            var selectedDigits = textBox.SelectedText.Count(char.IsDigit);
            return existingDigits - selectedDigits + digitsToAdd > 10;
        }
    }
}
