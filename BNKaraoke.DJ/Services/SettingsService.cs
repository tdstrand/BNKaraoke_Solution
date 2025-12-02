using BNKaraoke.DJ.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

namespace BNKaraoke.DJ.Services
{
    public class SettingsService
    {
        private static readonly Lazy<SettingsService> _instance = new Lazy<SettingsService>(() => new SettingsService());
        public static SettingsService Instance => _instance.Value;
        private readonly string _settingsPath;
        public DjSettings Settings { get; private set; }
        public event EventHandler<string>? AudioDeviceChanged;
        public event EventHandler<string>? VideoDeviceChanged;

        private SettingsService()
        {
            _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BNKaraoke", "settings.json");
            Settings = LoadSettings();
        }

        private DjSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<DjSettings>(json);
                    if (settings != null)
                    {
                        Log.Information("[SETTINGS SERVICE] Loaded settings from {Path}", _settingsPath);
                        var normalizedDeviceId = NormalizePreferredAudioDevice(settings.PreferredAudioDevice);
                        if (!string.Equals(settings.PreferredAudioDevice, normalizedDeviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            settings.PreferredAudioDevice = normalizedDeviceId;
                            SaveSettings(settings);
                        }
                        if (settings.AvailableApiUrls == null || settings.AvailableApiUrls.Count == 0)
                        {
                            settings.AvailableApiUrls = new List<string> { "http://localhost:7290", "https://api.bnkaraoke.com", "https://bnkaraoke.com:7290", "http://bn-concept:7290" };
                            settings.ApiUrl = settings.ApiUrl ?? "https://api.bnkaraoke.com";
                            SaveSettings(settings);
                        }
                        if (string.IsNullOrEmpty(settings.ApiUrl) || !IsValidUrl(settings.ApiUrl))
                        {
                            settings.ApiUrl = "https://api.bnkaraoke.com";
                            SaveSettings(settings);
                        }
                        if (settings.Overlay == null)
                        {
                            settings.Overlay = new OverlaySettings();
                        }
                        settings.Overlay.Clamp();
                        settings.Hydration ??= new HydrationSettings();
                        settings.Hydration.Normalize();
                        settings.MasterVolume = Math.Clamp(settings.MasterVolume, 0, 100);
                        if (string.IsNullOrWhiteSpace(settings.AudioOutputModule))
                        {
                            settings.AudioOutputModule = "mmdevice";
                        }
                        if (string.IsNullOrWhiteSpace(settings.DefaultReorderMaturePolicy))
                        {
                            settings.DefaultReorderMaturePolicy = "Defer";
                        }
                        if (settings.QueueReorderConfirmationThreshold <= 0)
                        {
                            settings.QueueReorderConfirmationThreshold = 6;
                        }
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SETTINGS SERVICE] Failed to load settings: {Message}", ex.Message);
            }

            var defaultSettings = new DjSettings
            {
                AvailableApiUrls = new List<string> { "http://localhost:7290", "https://api.bnkaraoke.com", "https://bnkaraoke.com:7290", "http://bn-concept:7290" },
                ApiUrl = "https://api.bnkaraoke.com",
                DefaultDJName = "DJ Ted",
                PreferredAudioDevice = AudioDeviceConstants.WindowsDefaultAudioDeviceId,
                KaraokeVideoDevice = @"\\.\DISPLAY1",
                EnableVideoCaching = true,
                VideoCachePath = @"C:\BNKaraoke\Cache\",
                CacheSizeGB = 10.0,
                EnableSignalRSync = true,
                SignalRHubUrl = "/hubs/queue",
                ReconnectIntervalMs = 5000,
                Theme = "Dark",
                ShowDebugConsole = true,
                MaximizedOnStart = true,
                LogFilePath = @"C:\BNKaraoke_Logs\DJ.log",
                EnableVerboseLogging = true,
                TestMode = false,
                MasterVolume = 100,
                Overlay = new OverlaySettings(),
                Hydration = new HydrationSettings()
            };
            SaveSettings(defaultSettings);
            Log.Information("[SETTINGS SERVICE] Created default settings at {Path}", _settingsPath);
            return defaultSettings;
        }

        public async Task<DjSettings> LoadSettingsAsync()
        {
            return await Task.FromResult(LoadSettings());
        }

        public void SaveSettings(DjSettings settings)
        {
            try
            {
                var directory = Path.GetDirectoryName(_settingsPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory!);
                }
                if (!string.IsNullOrEmpty(settings.ApiUrl) && !settings.AvailableApiUrls.Contains(settings.ApiUrl))
                {
                    settings.AvailableApiUrls.Add(settings.ApiUrl);
                }
                settings.PreferredAudioDevice = NormalizePreferredAudioDevice(settings.PreferredAudioDevice);
                settings.AudioOutputModule = string.IsNullOrWhiteSpace(settings.AudioOutputModule) ? "mmdevice" : settings.AudioOutputModule;
                settings.Overlay ??= new OverlaySettings();
                settings.Overlay.Clamp();
                settings.Hydration ??= new HydrationSettings();
                settings.Hydration.Normalize();
                settings.MasterVolume = Math.Clamp(settings.MasterVolume, 0, 100);
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
                var previousAudioDevice = Settings?.PreferredAudioDevice;
                var previousVideoDevice = Settings?.KaraokeVideoDevice;
                Settings = settings;
                Log.Information("[SETTINGS SERVICE] Saved settings to {Path}, ApiUrl={ApiUrl}", _settingsPath, settings.ApiUrl);
                if (!string.Equals(previousAudioDevice, settings.PreferredAudioDevice, StringComparison.OrdinalIgnoreCase))
                {
                    AudioDeviceChanged?.Invoke(this, settings.PreferredAudioDevice);
                    Log.Information("[SETTINGS SERVICE] Notified audio device change: {DeviceId}", settings.PreferredAudioDevice);
                }
                if (!string.Equals(previousVideoDevice, settings.KaraokeVideoDevice, StringComparison.OrdinalIgnoreCase))
                {
                    VideoDeviceChanged?.Invoke(this, settings.KaraokeVideoDevice);
                    Log.Information("[SETTINGS SERVICE] Notified video device change: {DeviceId}", settings.KaraokeVideoDevice);
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SETTINGS SERVICE] Failed to save settings: {Message}", ex.Message);
                throw;
            }
        }

        public async Task SaveSettingsAsync(DjSettings settings)
        {
            await Task.Run(() => SaveSettings(settings));
        }

        public bool IsValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            return Regex.IsMatch(url, @"^https?://[\w\-\.]+(:\d+)?(/.*)?$");
        }

        private static string NormalizePreferredAudioDevice(string? deviceId)
        {
            return string.IsNullOrWhiteSpace(deviceId) ? AudioDeviceConstants.WindowsDefaultAudioDeviceId : deviceId;
        }

        public void Save()
        {
            SaveSettings(Settings);
        }
    }
}
