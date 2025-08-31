using Serilog;
using System;
using System.IO;
using System.Threading.Tasks;

namespace BNKaraoke.DJ.Services;

public class VideoCacheService
{
    private readonly SettingsService _settingsService;
    private readonly IApiService _apiService;

    public VideoCacheService(SettingsService settingsService, IApiService apiService)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
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

    public async Task CacheVideoAsync(int songId)
    {
        if (!_settingsService.Settings.EnableVideoCaching)
        {
            Log.Information("[CACHE SERVICE] Video caching disabled for SongId={SongId}", songId);
            return;
        }

        try
        {
            var cachePath = Path.Combine(_settingsService.Settings.VideoCachePath, $"{songId}.mp4");

            if (File.Exists(cachePath))
            {
                Log.Information("[CACHE SERVICE] Video already cached for SongId={SongId}: {CachePath}", songId, cachePath);
                return;
            }

            Directory.CreateDirectory(_settingsService.Settings.VideoCachePath);

            Log.Information("[CACHE SERVICE] Starting caching for SongId={SongId} from API", songId);
            using var stream = await _apiService.DownloadCachedSongAsync(songId);
            await using var fileStream = File.Create(cachePath);
            await stream.CopyToAsync(fileStream);

            if (File.Exists(cachePath))
            {
                Log.Information("[CACHE SERVICE] Cached video: SongId={SongId}", songId);
            }
            else
            {
                Log.Error("[CACHE SERVICE] Failed to cache video for SongId={SongId}", songId);
            }
        }
        catch (Exception ex)
        {
            Log.Error("[CACHE SERVICE] Error caching video: SongId={SongId}, Message: {Message}", songId, ex.Message);
            try
            {
                var cachePath = Path.Combine(_settingsService.Settings.VideoCachePath, $"{songId}.mp4");
                if (File.Exists(cachePath))
                {
                    File.Delete(cachePath);
                }
            }
            catch (Exception cleanupEx)
            {
                Log.Error("[CACHE SERVICE] Error cleaning up cache for SongId={SongId}: {Message}", songId, cleanupEx.Message);
            }
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