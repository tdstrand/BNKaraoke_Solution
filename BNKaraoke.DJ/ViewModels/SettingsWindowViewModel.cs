using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services;
using BNKaraoke.DJ.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NAudio.CoreAudioApi;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace BNKaraoke.DJ.ViewModels;

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

public partial class SettingsWindowViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly IUserSessionService _userSessionService;
    private readonly List<string> _availableApiUrls = new List<string>
    {
        "http://localhost:7290",
        "https://bnkaraoke.com:7290"
    };

    public IReadOnlyList<string> AvailableApiUrls => _availableApiUrls;
    public ObservableCollection<MMDevice> AvailableAudioDevices { get; } = new ObservableCollection<MMDevice>();
    public ObservableCollection<MonitorInfo> AvailableVideoDevices { get; } = new ObservableCollection<MonitorInfo>();

    [ObservableProperty] private string _apiUrl;
    [ObservableProperty] private string _defaultDJName;
    [ObservableProperty] private MMDevice? _preferredAudioDevice;
    [ObservableProperty] private MonitorInfo? _karaokeVideoDevice;
    [ObservableProperty] private bool _enableVideoCaching;
    [ObservableProperty] private string _videoCachePath;
    [ObservableProperty] private double _cacheSizeGB;
    [ObservableProperty] private bool _enableSignalRSync;
    [ObservableProperty] private string _signalRHubUrl;
    [ObservableProperty] private int _reconnectIntervalMs;
    [ObservableProperty] private string _theme;
    [ObservableProperty] private bool _showDebugConsole;
    [ObservableProperty] private bool _maximizedOnStart;
    [ObservableProperty] private string _logFilePath;
    [ObservableProperty] private bool _enableVerboseLogging;
    [ObservableProperty] private bool _testMode;

    public SettingsWindowViewModel()
    {
        _settingsService = SettingsService.Instance;
        _userSessionService = UserSessionService.Instance;

        // Initialize audio devices
        try
        {
            using (var enumerator = new MMDeviceEnumerator())
            {
                foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    AvailableAudioDevices.Add(device);
                }
                Log.Information("[SETTINGS VM] Enumerated {Count} audio devices: {Devices}", AvailableAudioDevices.Count, string.Join(", ", AvailableAudioDevices.Select(d => d.FriendlyName)));
            }
        }
        catch (Exception ex)
        {
            Log.Error("[SETTINGS VM] Failed to enumerate audio devices: {Message}", ex.Message);
        }

        // Initialize video devices
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

        // Initialize from current settings
        _apiUrl = _settingsService.Settings.ApiUrl;
        _defaultDJName = _settingsService.Settings.DefaultDJName;
        _preferredAudioDevice = AvailableAudioDevices.FirstOrDefault(d => d.ID == _settingsService.Settings.PreferredAudioDevice);
        _karaokeVideoDevice = AvailableVideoDevices.FirstOrDefault(s => s.Screen.DeviceName == _settingsService.Settings.KaraokeVideoDevice);
        _enableVideoCaching = _settingsService.Settings.EnableVideoCaching;
        _videoCachePath = _settingsService.Settings.VideoCachePath;
        _cacheSizeGB = _settingsService.Settings.CacheSizeGB;
        _enableSignalRSync = _settingsService.Settings.EnableSignalRSync;
        _signalRHubUrl = _settingsService.Settings.SignalRHubUrl;
        _reconnectIntervalMs = _settingsService.Settings.ReconnectIntervalMs;
        _theme = _settingsService.Settings.Theme;
        _showDebugConsole = _settingsService.Settings.ShowDebugConsole;
        _maximizedOnStart = _settingsService.Settings.MaximizedOnStart;
        _logFilePath = _settingsService.Settings.LogFilePath;
        _enableVerboseLogging = _settingsService.Settings.EnableVerboseLogging;
        _testMode = _settingsService.Settings.TestMode;

        Log.Information("[SETTINGS VM] Initialized: ApiUrl={ApiUrl}, DefaultDJName={DefaultDJName}, PreferredAudioDevice={PreferredAudioDevice}, KaraokeVideoDevice={KaraokeVideoDevice}, EnableSignalRSync={EnableSignalRSync}, CacheSizeGB={CacheSizeGB}, TestMode={TestMode}",
            _apiUrl, _defaultDJName, _preferredAudioDevice?.FriendlyName ?? "None", _karaokeVideoDevice?.DisplayName ?? "None", _enableSignalRSync, _cacheSizeGB, _testMode);
    }

    [RelayCommand]
    private async Task SaveSettings()
    {
        try
        {
            // Log collection states before saving
            Log.Information("[SETTINGS VM] Saving settings: AudioDevicesCount={AudioCount}, VideoDevicesCount={VideoCount}", AvailableAudioDevices.Count, AvailableVideoDevices.Count);

            // Validate ApiUrl
            if (!_availableApiUrls.Contains(ApiUrl))
            {
                MessageBox.Show($"API URL {ApiUrl} is not in the allowed list. Please select a valid URL.", "Invalid API URL", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(5);
                var response = await client.GetAsync($"{ApiUrl}/api/Auth/test");
                if (!response.IsSuccessStatusCode)
                {
                    MessageBox.Show($"API URL {ApiUrl} is not reachable. Please try again.", "API Unreachable", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            // Validate ReconnectIntervalMs
            if (ReconnectIntervalMs < 1000)
            {
                MessageBox.Show("Reconnect Interval must be at least 1000 ms.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Validate VideoCachePath
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

            // Validate LogFilePath
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

            // Validate CacheSizeGB
            if (CacheSizeGB < 0 || CacheSizeGB > 100)
            {
                MessageBox.Show("Cache Size must be between 0 and 100 GB.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Validate audio and video devices
            if (AvailableAudioDevices.Count == 0)
            {
                MessageBox.Show("No audio devices available. Please connect an audio device.", "No Audio Devices", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (AvailableVideoDevices.Count == 0)
            {
                MessageBox.Show("No video devices available. Please connect a monitor.", "No Video Devices", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bool apiUrlChanged = _settingsService.Settings.ApiUrl != ApiUrl;
            if (apiUrlChanged)
            {
                var result = MessageBox.Show("Changing the API URL will log you out. Proceed?", "Confirm API URL Change", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            // Create DjSettings object
            var settings = new DjSettings
            {
                ApiUrl = ApiUrl,
                DefaultDJName = DefaultDJName,
                PreferredAudioDevice = PreferredAudioDevice?.ID ?? "",
                KaraokeVideoDevice = KaraokeVideoDevice?.Screen.DeviceName ?? "",
                EnableVideoCaching = EnableVideoCaching,
                VideoCachePath = VideoCachePath,
                CacheSizeGB = CacheSizeGB,
                EnableSignalRSync = EnableSignalRSync,
                SignalRHubUrl = SignalRHubUrl,
                ReconnectIntervalMs = ReconnectIntervalMs,
                Theme = Theme,
                ShowDebugConsole = ShowDebugConsole,
                MaximizedOnStart = MaximizedOnStart,
                LogFilePath = LogFilePath,
                EnableVerboseLogging = EnableVerboseLogging,
                TestMode = TestMode
            };

            // Save to settings.json
            await _settingsService.SaveSettingsAsync(settings);
            Log.Information("[SETTINGS VM] Settings saved successfully");

            // Close windows safely
            var settingsWindow = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
            if (settingsWindow != null)
            {
                Log.Information("[SETTINGS VM] Closing SettingsWindow");
                settingsWindow.Close();
            }
            else
            {
                Log.Warning("[SETTINGS VM] No SettingsWindow found to close");
            }

            if (apiUrlChanged)
            {
                _userSessionService.ClearSession();
                Log.Information("[SETTINGS VM] API URL changed to {ApiUrl}, session cleared", ApiUrl);

                var loginWindow = new LoginWindow { WindowStartupLocation = WindowStartupLocation.CenterScreen };
                loginWindow.Show();

                var djScreen = Application.Current.Windows.OfType<DJScreen>().FirstOrDefault();
                if (djScreen != null)
                {
                    Log.Information("[SETTINGS VM] Closing DJScreen");
                    djScreen.Close();
                }
                else
                {
                    Log.Warning("[SETTINGS VM] No DJScreen found to close");
                }
            }
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
        var settingsWindow = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
        if (settingsWindow != null)
        {
            Log.Information("[SETTINGS VM] Closing SettingsWindow on cancel");
            settingsWindow.Close();
        }
        else
        {
            Log.Warning("[SETTINGS VM] No SettingsWindow found to close on cancel");
        }
    }
}