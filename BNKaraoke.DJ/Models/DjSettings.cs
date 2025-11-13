using System.Collections.Generic;

namespace BNKaraoke.DJ.Models
{
    public class DjSettings
    {
        public List<string> AvailableApiUrls { get; set; } = new List<string> { "http://localhost:7290", "https://bnkaraoke.com:7290" };
        public string ApiUrl { get; set; } = "http://localhost:7290";
        public string DefaultDJName { get; set; } = "DJ Ted";
        public string PreferredAudioDevice { get; set; } = AudioDeviceConstants.WindowsDefaultAudioDeviceId;
        public string KaraokeVideoDevice { get; set; } = @"\\.\DISPLAY1";
        public bool EnableVideoCaching { get; set; } = true;
        public string VideoCachePath { get; set; } = @"C:\BNKaraoke\Cache\";
        public double CacheSizeGB { get; set; } = 10.0;
        public bool EnableSignalRSync { get; set; } = true;
        public string SignalRHubUrl { get; set; } = "/hubs/queue";
        public int ReconnectIntervalMs { get; set; } = 5000;
        public string Theme { get; set; } = "Dark";
        public bool ShowDebugConsole { get; set; } = true;
        public bool MaximizedOnStart { get; set; } = true;
        public string LogFilePath { get; set; } = @"C:\BNKaraoke_Logs\DJ.log";
        public bool EnableVerboseLogging { get; set; } = true;
        public bool TestMode { get; set; } = false;
        public string AudioOutputModule { get; set; } = "mmdevice";
        public string? AudioOutputDeviceId { get; set; }
        public bool AllowDirectSoundFallback { get; set; } = true;
        public bool EnableAudioEngineRestartButton { get; set; } = true;
        public string DefaultReorderMaturePolicy { get; set; } = "Defer";
        public int QueueReorderConfirmationThreshold { get; set; } = 6;
        public OverlaySettings Overlay { get; set; } = new OverlaySettings();
        public HydrationSettings Hydration { get; set; } = new HydrationSettings();
    }
}
