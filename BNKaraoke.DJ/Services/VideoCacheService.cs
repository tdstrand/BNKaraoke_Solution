using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace BNKaraoke.DJ.Services;

public class VideoCacheService
{
    private readonly SettingsService _settingsService;

    public VideoCacheService(SettingsService settingsService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    }

    public bool IsVideoCached(int songId)
    {
        try
        {
            string cachePath = Path.Combine(_settingsService.Settings.VideoCachePath, $"{songId}.mp4");
            bool exists = File.Exists(cachePath);
            Log.Information("[CACHE SERVICE] Checked cache for SongId={SongId}: Exists={Exists}", songId, exists);
            return exists;
        }
        catch (Exception ex)
        {
            Log.Error("[CACHE SERVICE] Error checking cache for SongId={SongId}: {Message}", songId, ex.Message);
            return false;
        }
    }

    public async Task CacheVideoAsync(string youTubeUrl, int songId)
    {
        if (!_settingsService.Settings.EnableVideoCaching)
        {
            Log.Information("[CACHE SERVICE] Video caching disabled for SongId={SongId}", songId);
            return;
        }

        try
        {
            string cachePath = Path.Combine(_settingsService.Settings.VideoCachePath, $"{songId}.mp4");

            if (File.Exists(cachePath))
            {
                Log.Information("[CACHE SERVICE] Video already cached for SongId={SongId}: {CachePath}", songId, cachePath);
                return;
            }

            Directory.CreateDirectory(_settingsService.Settings.VideoCachePath);

            string ytDlpPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "yt-dlp.exe");
            string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "ffmpeg.exe");

            // Use higher resolution format (up to 1080p)
            string args = $"--output \"{cachePath}\" --format bestvideo[height<=1080][ext=mp4]+bestaudio[ext=m4a]/best[ext=mp4] --merge-output-format mp4 --ffmpeg-location \"{ffmpegPath}\" \"{youTubeUrl}\"";

            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = ytDlpPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                }
            };

            Log.Information("[CACHE SERVICE] Starting caching for SongId={SongId}: {YouTubeUrl}", songId, youTubeUrl);

            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            if (process.ExitCode == 0 && File.Exists(cachePath))
            {
                Log.Information("[CACHE SERVICE] Cached video: SongId={SongId}, Output: {Output}", songId, output);
            }
            else
            {
                Log.Error("[CACHE SERVICE] Error caching video: SongId={SongId}, Error: {Error}, ExitCode: {ExitCode}", songId, error, process.ExitCode);
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("[CACHE SERVICE] Error caching video: SongId={SongId}, Message: {Message}", songId, ex.Message);
        }
    }

    public long GetCacheSizeBytes()
    {
        try
        {
            if (!Directory.Exists(_settingsService.Settings.VideoCachePath))
            {
                return 0;
            }

            long totalSize = 0;
            foreach (var file in Directory.GetFiles(_settingsService.Settings.VideoCachePath, "*.mp4"))
            {
                totalSize += new FileInfo(file).Length;
            }

            Log.Information("[CACHE SERVICE] Cache size: {SizeBytes} bytes", totalSize);
            return totalSize;
        }
        catch (Exception ex)
        {
            Log.Error("[CACHE SERVICE] Error getting cache size: {Message}", ex.Message);
            return 0;
        }
    }
}