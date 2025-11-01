using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class LoginWindowViewModel : ObservableObject
    {
        private readonly AuthService? _authService; // Nullable to fix CS8618
        private readonly IUserSessionService? _userSessionService; // Nullable to fix CS8618
        private readonly SettingsService _settingsService;
        private bool _hasLoggedPhoneValidityState;
        private bool _lastLoggedPhoneValidity;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowPhoneValidationMessage))]
        private string _phoneValidationMessage = "Enter a 10-digit phone number";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowPhoneValidationMessage))]
        private bool _isPhoneValid;

        [ObservableProperty]
        private string _phoneNumberRaw = string.Empty;

        [ObservableProperty]
        private string _password = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _errorMessage = string.Empty;

        public ObservableCollection<string> AvailableApiUrls { get; } = new();
        [ObservableProperty] private string _selectedApiUrl = string.Empty;

        public bool ShowPhoneValidationMessage => !IsPhoneValid;

        public LoginWindowViewModel()
        {
            _settingsService = SettingsService.Instance;
            try
            {
                foreach (var url in _settingsService.Settings.AvailableApiUrls)
                {
                    AvailableApiUrls.Add(url);
                }
                _selectedApiUrl = _settingsService.Settings.ApiUrl;
                _authService = new AuthService();
                _userSessionService = UserSessionService.Instance;
                Log.Information("[LOGIN VM] ViewModel initialized: {InstanceId}, ApiUrl={ApiUrl}", GetHashCode(), _authService.GetCurrentApiUrl());
            }
            catch (Exception ex)
            {
                Log.Error("[LOGIN VM] Failed to initialize ViewModel: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                ErrorMessage = $"Initialization failed: {ex.Message}";
            }
        }

        partial void OnSelectedApiUrlChanged(string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && _settingsService.IsValidUrl(value))
            {
                _settingsService.Settings.ApiUrl = value;
                _settingsService.SaveSettings(_settingsService.Settings);
                _authService?.SetApiUrl(value);
                Log.Information("[LOGIN VM] Selected API URL changed to {ApiUrl}", value);
            }
        }

        partial void OnPhoneNumberRawChanged(string value)
        {
            var digits = Regex.Replace(value ?? string.Empty, "[^0-9]", string.Empty);
            if (digits.Length > 10)
            {
                digits = digits[..10];
            }

            if (!string.Equals(digits, _phoneNumberRaw, StringComparison.Ordinal))
            {
                _phoneNumberRaw = digits;
                OnPropertyChanged(nameof(PhoneNumberRaw));
            }

            UpdatePhoneValidationState();
        }

        partial void OnIsBusyChanged(bool value)
        {
            LoginCommand.NotifyCanExecuteChanged();
        }

        private void UpdatePhoneValidationState()
        {
            var isValid = PhoneNumberRaw.Length == 10;
            if (IsPhoneValid != isValid)
            {
                IsPhoneValid = isValid;
                LoginCommand.NotifyCanExecuteChanged();

                if (!_hasLoggedPhoneValidityState || _lastLoggedPhoneValidity != isValid)
                {
                    Log.Information("[LOGIN VM] Phone validation state changed: IsValid={IsValid}, DigitsLength={Length}", isValid, PhoneNumberRaw.Length);
                    _hasLoggedPhoneValidityState = true;
                    _lastLoggedPhoneValidity = isValid;
                }
            }

            PhoneValidationMessage = isValid ? string.Empty : "Enter a 10-digit phone number";
        }

        [RelayCommand(CanExecute = nameof(CanExecuteLogin))]
        private async Task Login()
        {
            try
            {
                ErrorMessage = string.Empty;
                IsBusy = true;
                var userNameDigits = PhoneNumberRaw;
                Log.Information("[LOGIN VM] Attempting login with UserName={UserName}, ApiUrl={ApiUrl}", userNameDigits, _authService?.GetCurrentApiUrl() ?? "null");
                if (string.IsNullOrWhiteSpace(userNameDigits))
                {
                    ErrorMessage = "Username is required.";
                    Log.Error("[LOGIN VM] Login failed: Empty username");
                    return;
                }
                if (userNameDigits.Length != 10)
                {
                    ErrorMessage = "Username must be a 10-digit phone number.";
                    Log.Error("[LOGIN VM] Login failed: Invalid username length={Length}", userNameDigits.Length);
                    return;
                }
                if (string.IsNullOrWhiteSpace(Password))
                {
                    ErrorMessage = "Password is required.";
                    Log.Error("[LOGIN VM] Login failed: Empty password");
                    return;
                }
                if (_authService == null)
                {
                    ErrorMessage = "Authentication service not initialized.";
                    Log.Error("[LOGIN VM] Login failed: AuthService is null");
                    return;
                }
                var loginResult = await _authService.LoginAsync(userNameDigits, Password);
                if (!string.IsNullOrEmpty(loginResult.Token))
                {
                    if (_userSessionService == null)
                    {
                        ErrorMessage = "Session service not initialized.";
                        Log.Error("[LOGIN VM] Login failed: UserSessionService is null");
                        return;
                    }
                    _userSessionService.SetSession(loginResult, userNameDigits);
                    Log.Information("[LOGIN VM] Login successful: UserName={UserName}, FirstName={FirstName}", userNameDigits, loginResult.FirstName);
                    if (Application.Current.Windows.OfType<LoginWindow>().FirstOrDefault() is LoginWindow loginWindow)
                    {
                        loginWindow.DialogResult = true;
                        loginWindow.Close();
                        Log.Information("[LOGIN VM] LoginWindow DialogResult set to true and closed");
                    }
                    else
                    {
                        Log.Warning("[LOGIN VM] No LoginWindow found to close");
                    }
                }
                else
                {
                    ErrorMessage = "Invalid username or password.";
                    Log.Error("[LOGIN VM] Login failed: Invalid credentials");
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Login failed: {ex.Message}";
                Log.Error("[LOGIN VM] Login failed: {Message}, StackTrace={StackTrace}", ex.Message, ex.StackTrace);
                MessageBox.Show($"Login failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.None);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanExecuteLogin() => IsPhoneValid && !IsBusy;
    }
}