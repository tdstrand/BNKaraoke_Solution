namespace BNKaraoke.DJ.Models;

public class DjSettings
{
    public string ApiUrl { get; set; } = "http://localhost:7290";
    public string DefaultDJName { get; set; } = "DJ Ted";
    public string PreferredAudioDevice { get; set; } = "Focusrite USB Audio";
    public string KaraokeVideoDevice { get; set; } = "Display 2";
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
    public bool TestMode { get; set; } = false; // Default to Production
}