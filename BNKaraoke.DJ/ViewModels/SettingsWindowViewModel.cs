using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.Views;
using BNKaraoke.DJ.ViewModels.Overlays;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace BNKaraoke.DJ.ViewModels
{
    public class MonitorInfo
    {
        public System.Windows.Forms.Screen Screen { get; set; }
        public string DisplayName => Screen.Primary ? $"Primary Display ({Screen.DeviceName})" : $"Display {ScreenIndex + 1} ({Screen.DeviceName})";
        public int ScreenIndex { get; set; }
        public MonitorInfo(System.Windows.Forms.Screen screen, int index)
        {
            Screen = screen;
            ScreenIndex = index;
        }
    }

    public class AudioDeviceOption
    {
        public AudioDeviceOption(string id, string friendlyName, string displayName)
        {
            Id = id;
            FriendlyName = friendlyName;
            DisplayName = displayName;
        }

        public string Id { get; }
        public string FriendlyName { get; }
        public string DisplayName { get; }
        public bool IsWindowsDefault => string.Equals(Id, AudioDeviceConstants.WindowsDefaultAudioDeviceId, StringComparison.OrdinalIgnoreCase);

        public override string ToString() => DisplayName;
    }

    public partial class SettingsWindowViewModel : ObservableObject
    {
        private readonly SettingsService _settingsService;
        private readonly IUserSessionService _userSessionService;

        [ObservableProperty] private ObservableCollection<string> _availableApiUrls;
        [ObservableProperty] private ObservableCollection<AudioDeviceOption> _availableAudioDevices = new ObservableCollection<AudioDeviceOption>();
        [ObservableProperty] private ObservableCollection<MonitorInfo> _availableVideoDevices = new ObservableCollection<MonitorInfo>();
        [ObservableProperty] private string _apiUrl = string.Empty;
        [ObservableProperty] private string _defaultDJName = string.Empty;
        [ObservableProperty] private AudioDeviceOption? _preferredAudioDevice;
        [ObservableProperty] private MonitorInfo? _karaokeVideoDevice;
        [ObservableProperty] private bool _enableVideoCaching;
        [ObservableProperty] private string _videoCachePath = string.Empty;
        [ObservableProperty] private double _cacheSizeGB;
        [ObservableProperty] private bool _enableSignalRSync;
        [ObservableProperty] private string _signalRHubUrl = string.Empty;
        [ObservableProperty] private int _reconnectIntervalMs;
        [ObservableProperty] private string _theme = string.Empty;
        [ObservableProperty] private bool _showDebugConsole;
        [ObservableProperty] private bool _maximizedOnStart;
        [ObservableProperty] private string _logFilePath = string.Empty;
        [ObservableProperty] private bool _enableVerboseLogging;
        [ObservableProperty] private bool _testMode;
        [ObservableProperty] private string _newApiUrl = string.Empty;
        [ObservableProperty] private bool _overlayMarqueeEnabled;
        [ObservableProperty] private double _overlayMarqueeSpeed = 90.0;
        [ObservableProperty] private double _overlayMarqueeSpacerWidth = 140.0;
        [ObservableProperty] private int _overlayMarqueeCrossfadeMs = 200;

        public SettingsWindowViewModel()
        {
            _settingsService = SettingsService.Instance;
            _userSessionService = UserSessionService.Instance;
            AvailableApiUrls = new ObservableCollection<string>(_settingsService.Settings.AvailableApiUrls);
            ApiUrl = _settingsService.Settings.ApiUrl;

            AvailableAudioDevices.Add(new AudioDeviceOption(AudioDeviceConstants.WindowsDefaultAudioDeviceId, AudioDeviceConstants.WindowsDefaultDisplayName, AudioDeviceConstants.WindowsDefaultDisplayName));

            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    try
                    {
                        var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                        if (defaultDevice != null)
                        {
                            var defaultDisplayName = $"{AudioDeviceConstants.WindowsDefaultDisplayName} ({defaultDevice.FriendlyName})";
                            AvailableAudioDevices[0] = new AudioDeviceOption(AudioDeviceConstants.WindowsDefaultAudioDeviceId, AudioDeviceConstants.WindowsDefaultDisplayName, defaultDisplayName);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("[SETTINGS VM] Unable to determine Windows default audio device: {Message}", ex.Message);
                    }

                    foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                    {
                        AvailableAudioDevices.Add(new AudioDeviceOption(device.ID, device.FriendlyName, device.FriendlyName));
                    }

                    Log.Information("[SETTINGS VM] Enumerated {Count} audio devices: {Devices}", AvailableAudioDevices.Count, string.Join(", ", AvailableAudioDevices.Select(d => d.DisplayName)));
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SETTINGS VM] Failed to enumerate audio devices: {Message}", ex.Message);
            }

            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                for (int i = 0; i < screens.Length; i++)
                {
                    AvailableVideoDevices.Add(new MonitorInfo(screens[i], i));
                }
                Log.Information("[SETTINGS VM] Enumerated {Count} video devices: {Devices}", AvailableVideoDevices.Count, string.Join(", ", AvailableVideoDevices.Select(s => $"{s.DisplayName} ({s.Screen.Bounds.Width}x{s.Screen.Bounds.Height}, Primary={s.Screen.Primary})")));
                if (AvailableVideoDevices.Count == 0)
                {
                    Log.Warning("[SETTINGS VM] No video devices detected");
                    MessageBox.Show("No monitors detected. Please connect at least one display.", "No Displays", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SETTINGS VM] Failed to enumerate video devices: {Message}", ex.Message);
                MessageBox.Show("Failed to detect monitors. Please check display connections.", "Display Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            DefaultDJName = _settingsService.Settings.DefaultDJName;
            var savedAudioDeviceId = _settingsService.Settings.PreferredAudioDevice;
            PreferredAudioDevice =
                AvailableAudioDevices.FirstOrDefault(d => string.Equals(d.Id, savedAudioDeviceId, StringComparison.OrdinalIgnoreCase)) ??
                AvailableAudioDevices.FirstOrDefault(d => !string.IsNullOrWhiteSpace(savedAudioDeviceId) && string.Equals(d.FriendlyName, savedAudioDeviceId, StringComparison.OrdinalIgnoreCase)) ??
                AvailableAudioDevices.FirstOrDefault(d => d.IsWindowsDefault) ??
                AvailableAudioDevices.FirstOrDefault();
            KaraokeVideoDevice = AvailableVideoDevices.FirstOrDefault(s => s.Screen.DeviceName == _settingsService.Settings.KaraokeVideoDevice);
            EnableVideoCaching = _settingsService.Settings.EnableVideoCaching;
            VideoCachePath = _settingsService.Settings.VideoCachePath;
            CacheSizeGB = _settingsService.Settings.CacheSizeGB;
            EnableSignalRSync = _settingsService.Settings.EnableSignalRSync;
            SignalRHubUrl = _settingsService.Settings.SignalRHubUrl ?? "";
            ReconnectIntervalMs = _settingsService.Settings.ReconnectIntervalMs;
            Theme = _settingsService.Settings.Theme;
            ShowDebugConsole = _settingsService.Settings.ShowDebugConsole;
            MaximizedOnStart = _settingsService.Settings.MaximizedOnStart;
            LogFilePath = _settingsService.Settings.LogFilePath;
            EnableVerboseLogging = _settingsService.Settings.EnableVerboseLogging;
            TestMode = _settingsService.Settings.TestMode;

            var overlaySettings = _settingsService.Settings.Overlay ?? new OverlaySettings();
            OverlayMarqueeEnabled = overlaySettings.MarqueeEnabled;
            OverlayMarqueeSpeed = overlaySettings.MarqueeSpeedPxPerSecond;
            OverlayMarqueeSpacerWidth = overlaySettings.MarqueeSpacerWidthPx;
            OverlayMarqueeCrossfadeMs = overlaySettings.MarqueeCrossfadeMs;

            Log.Information("[SETTINGS VM] Initialized: ApiUrl={ApiUrl}, DefaultDJName={DefaultDJName}, PreferredAudioDevice={PreferredAudioDevice}, KaraokeVideoDevice={KaraokeVideoDevice}, EnableSignalRSync={EnableSignalRSync}, CacheSizeGB={CacheSizeGB}, TestMode={TestMode}",
                ApiUrl, DefaultDJName, PreferredAudioDevice?.DisplayName ?? AudioDeviceConstants.WindowsDefaultDisplayName, KaraokeVideoDevice?.DisplayName ?? "None", EnableSignalRSync, CacheSizeGB, TestMode);
        }

        [RelayCommand]
        private async Task AddApiUrl()
        {
            if (string.IsNullOrWhiteSpace(NewApiUrl) || !NewApiUrl.StartsWith("http://") && !NewApiUrl.StartsWith("https://"))
            {
                MessageBox.Show("Please enter a valid URL (e.g., http://localhost:7290 or https://api.bnkaraoke.com)", "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!_settingsService.IsValidUrl(NewApiUrl))
            {
                MessageBox.Show($"Invalid API URL format: {NewApiUrl}", "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (!AvailableApiUrls.Contains(NewApiUrl))
            {
                AvailableApiUrls.Add(NewApiUrl);
                ApiUrl = NewApiUrl;
                var settings = _settingsService.Settings;
                settings.AvailableApiUrls = AvailableApiUrls.ToList();
                settings.ApiUrl = ApiUrl;
                await _settingsService.SaveSettingsAsync(settings);
                Log.Information("[SETTINGS VM] Added and set API URL: {NewApiUrl}", NewApiUrl);
                NewApiUrl = "";
                OnPropertyChanged(nameof(NewApiUrl));
                OnPropertyChanged(nameof(AvailableApiUrls));
                OnPropertyChanged(nameof(ApiUrl));
            }
            else
            {
                MessageBox.Show($"API URL {NewApiUrl} already exists.", "Duplicate URL", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        private async Task RemoveApiUrl()
        {
            if (!string.IsNullOrEmpty(ApiUrl) && AvailableApiUrls.Contains(ApiUrl))
            {
                AvailableApiUrls.Remove(ApiUrl);
                var settings = _settingsService.Settings;
                settings.AvailableApiUrls = AvailableApiUrls.ToList();
                ApiUrl = AvailableApiUrls.FirstOrDefault() ?? "https://api.bnkaraoke.com";
                settings.ApiUrl = ApiUrl;
                await _settingsService.SaveSettingsAsync(settings);
                OnPropertyChanged(nameof(AvailableApiUrls));
                OnPropertyChanged(nameof(ApiUrl));
                Log.Information("[SETTINGS VM] Removed API URL: {Url}, New ApiUrl: {NewApiUrl}", ApiUrl, settings.ApiUrl);
            }
        }

        [RelayCommand]
        private async Task SaveSettings()
        {
            try
            {
                if (!_settingsService.IsValidUrl(ApiUrl))
                {
                    MessageBox.Show($"API URL {ApiUrl} is invalid. Please select a valid URL.", "Invalid API URL", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (ReconnectIntervalMs < 1000)
                {
                    MessageBox.Show("Reconnect Interval must be at least 1000 ms.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (!string.IsNullOrEmpty(VideoCachePath) && !Directory.Exists(VideoCachePath))
                {
                    try
                    {
                        Directory.CreateDirectory(VideoCachePath);
                    }
                    catch
                    {
                        MessageBox.Show($"Video Cache Path {VideoCachePath} is invalid or inaccessible.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                if (!string.IsNullOrEmpty(LogFilePath))
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
                    }
                    catch
                    {
                        MessageBox.Show($"Log File Path {LogFilePath} is invalid or inaccessible.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                if (CacheSizeGB < 0 || CacheSizeGB > 100)
                {
                    MessageBox.Show("Cache Size must be between 0 and 100 GB.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                var settings = _settingsService.Settings;
                bool apiUrlChanged = settings.ApiUrl != ApiUrl;
                settings.AvailableApiUrls = AvailableApiUrls.ToList();
                settings.ApiUrl = ApiUrl;
                settings.DefaultDJName = DefaultDJName;
                settings.PreferredAudioDevice = PreferredAudioDevice?.Id ?? AudioDeviceConstants.WindowsDefaultAudioDeviceId;
                settings.KaraokeVideoDevice = KaraokeVideoDevice?.Screen.DeviceName ?? "";
                settings.EnableVideoCaching = EnableVideoCaching;
                settings.VideoCachePath = VideoCachePath;
                settings.CacheSizeGB = CacheSizeGB;
                settings.EnableSignalRSync = EnableSignalRSync;
                settings.SignalRHubUrl = SignalRHubUrl;
                settings.ReconnectIntervalMs = ReconnectIntervalMs;
                settings.Theme = Theme;
                settings.ShowDebugConsole = ShowDebugConsole;
                settings.MaximizedOnStart = MaximizedOnStart;
                settings.LogFilePath = LogFilePath;
                settings.EnableVerboseLogging = EnableVerboseLogging;
                settings.TestMode = TestMode;

                settings.Overlay ??= new OverlaySettings();
                settings.Overlay.MarqueeEnabled = OverlayMarqueeEnabled;
                settings.Overlay.MarqueeSpeedPxPerSecond = OverlayMarqueeSpeed;
                settings.Overlay.MarqueeSpacerWidthPx = OverlayMarqueeSpacerWidth;
                settings.Overlay.MarqueeCrossfadeMs = OverlayMarqueeCrossfadeMs;

                await _settingsService.SaveSettingsAsync(settings);
                OverlayViewModel.Instance.RefreshFromSettings();
                Log.Information("[SETTINGS VM] Settings saved successfully, ApiUrl={ApiUrl}", ApiUrl);
                if (apiUrlChanged)
                {
                    _userSessionService.ClearSession();
                    Log.Information("[SETTINGS VM] API URL changed to {ApiUrl}, session cleared", ApiUrl);
                    var loginWindow = new LoginWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                    loginWindow.Show();
                    Application.Current.Windows.OfType<DJScreen>().FirstOrDefault()?.Close();
                }
                Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault()?.Close();
            }
            catch (Exception ex)
            {
                Log.Error("[SETTINGS VM] Failed to save settings: {Message}", ex.Message);
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void BrowseVideoCachePath()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select Video Cache Path";
                dialog.SelectedPath = string.IsNullOrEmpty(VideoCachePath) ? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos) : VideoCachePath;
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    VideoCachePath = dialog.SelectedPath;
                    Log.Information("[SETTINGS VM] Selected Video Cache Path: {VideoCachePath}", VideoCachePath);
                }
            }
        }

        [RelayCommand]
        private void BrowseLogFilePath()
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Select Log File Path";
                var initialPath = string.IsNullOrEmpty(LogFilePath) ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetDirectoryName(LogFilePath);
                dialog.SelectedPath = initialPath ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    LogFilePath = Path.Combine(dialog.SelectedPath, "DJ.log");
                    Log.Information("[SETTINGS VM] Selected Log File Path: {LogFilePath}", LogFilePath);
                }
            }
        }

        [RelayCommand]
        private void Cancel()
        {
            Log.Information("[SETTINGS VM] Settings dialog canceled");
            Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault()?.Close();
        }
    }
}