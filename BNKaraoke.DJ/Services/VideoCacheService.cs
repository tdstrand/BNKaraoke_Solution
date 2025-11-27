using Serilog;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

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

    public async Task<TimeSpan?> TryGetVideoDurationAsync(int songId, CancellationToken cancellationToken = default)
    {
        try
        {
            var path = Path.Combine(_settingsService.Settings.VideoCachePath, $"{songId}.mp4");
            if (!File.Exists(path))
            {
                Log.Information("[CACHE SERVICE] Duration probe skipped; file missing for SongId={SongId}", songId);
                return null;
            }

            // Fast path: use MediaFoundationReader (works off-UI-thread)
            try
            {
                using var reader = new MediaFoundationReader(path);
                var duration = reader.TotalTime;
                if (duration > TimeSpan.Zero)
                {
                    Log.Information("[CACHE SERVICE] Duration from MediaFoundation for SongId={SongId}: {Seconds:F2}s", songId, duration.TotalSeconds);
                    return duration;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[CACHE SERVICE] MediaFoundation duration probe failed for SongId={SongId}: {Message}", songId, ex.Message);
            }

            var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
            var tcs = new TaskCompletionSource<TimeSpan?>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration registration = default;
            var hasRegistration = false;
            if (cancellationToken.CanBeCanceled)
            {
                registration = cancellationToken.Register(() =>
                {
                    tcs.TrySetCanceled(cancellationToken);
                });
                hasRegistration = true;
            }

            await dispatcher.InvokeAsync(() =>
            {
                var mediaPlayer = new MediaPlayer();

                void Cleanup()
                {
                    mediaPlayer.MediaOpened -= HandleOpened;
                    mediaPlayer.MediaFailed -= HandleFailed;
                    mediaPlayer.Close();
                }

                void HandleOpened(object? sender, EventArgs e)
                {
                    try
                    {
                        var duration = mediaPlayer.NaturalDuration.HasTimeSpan
                            ? mediaPlayer.NaturalDuration.TimeSpan
                            : (TimeSpan?)null;
                        tcs.TrySetResult(duration);
                    }
                    finally
                    {
                        Cleanup();
                    }
                }

                void HandleFailed(object? sender, ExceptionEventArgs e)
                {
                    try
                    {
                        Log.Warning("[CACHE SERVICE] Failed to probe video duration for SongId={SongId}: {Message}", songId, e.ErrorException?.Message ?? "Unknown error");
                        tcs.TrySetResult(null);
                    }
                    finally
                    {
                        Cleanup();
                    }
                }

                mediaPlayer.MediaOpened += HandleOpened;
                mediaPlayer.MediaFailed += HandleFailed;

                try
                {
                    Log.Information("[CACHE SERVICE] Probing duration via MediaPlayer for SongId={SongId}, Path={Path}", songId, path);
                    mediaPlayer.Open(new Uri(path));
                }
                catch (Exception ex)
                {
                    Log.Warning("[CACHE SERVICE] Exception starting duration probe for SongId={SongId}: {Message}", songId, ex.Message);
                    Cleanup();
                    tcs.TrySetResult(null);
                }
            }, DispatcherPriority.Background);

            try
            {
                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                if (hasRegistration)
                {
                    registration.Dispose();
                }
            }
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Warning("[CACHE SERVICE] Unexpected error probing duration for SongId={SongId}: {Message}", songId, ex.Message);
            return null;
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
