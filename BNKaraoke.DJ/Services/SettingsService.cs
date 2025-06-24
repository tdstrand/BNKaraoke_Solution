using BNKaraoke.DJ.Models;
using Serilog;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BNKaraoke.DJ.Services
{
    public class SettingsService
    {
        private static readonly Lazy<SettingsService> _instance = new Lazy<SettingsService>(() => new SettingsService());
        public static SettingsService Instance => _instance.Value;

        private readonly string _settingsPath;
        public DjSettings Settings { get; private set; }

        public event EventHandler<string>? AudioDeviceChanged;

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
                ApiUrl = "http://localhost:7290",
                DefaultDJName = "DJ Ted",
                PreferredAudioDevice = "Focusrite USB Audio",
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
                TestMode = false // Default to Production
            };
            Log.Information("[SETTINGS SERVICE] Using default settings");
            return defaultSettings;
        }

        public async Task<DjSettings> LoadSettingsAsync()
        {
            await Task.CompletedTask; // Simulate async for compatibility
            return LoadSettings();
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

                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
                var previousAudioDevice = Settings.PreferredAudioDevice;
                Settings = settings;
                Log.Information("[SETTINGS SERVICE] Saved settings to {Path}", _settingsPath);

                if (previousAudioDevice != settings.PreferredAudioDevice && !string.IsNullOrEmpty(settings.PreferredAudioDevice))
                {
                    AudioDeviceChanged?.Invoke(this, settings.PreferredAudioDevice);
                    Log.Information("[SETTINGS SERVICE] Notified audio device change: {DeviceId}", settings.PreferredAudioDevice);
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
    }
}