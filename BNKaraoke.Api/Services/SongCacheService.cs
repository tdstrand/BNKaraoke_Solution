using BNKaraoke.Api.Data;
using BNKaraoke.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace BNKaraoke.Api.Services
{
    public interface ISongCacheService
    {
        Task<bool> CacheSongAsync(int songId, string youTubeUrl, CancellationToken cancellationToken = default);
    }

    public class SongCacheService : ISongCacheService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SongCacheService> _logger;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public SongCacheService(IServiceScopeFactory scopeFactory, ILogger<SongCacheService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task<bool> CacheSongAsync(int songId, string youTubeUrl, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            string cachePath = "";
            int delaySeconds = 0;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                cachePath = await context.ApiSettings
                    .Where(s => s.SettingKey == "CacheStoragePath")
                    .Select(s => s.SettingValue)
                    .FirstOrDefaultAsync(cancellationToken) ?? "cache";
                var delayString = await context.ApiSettings
                    .Where(s => s.SettingKey == "CacheDownloadDelaySeconds")
                    .Select(s => s.SettingValue)
                    .FirstOrDefaultAsync(cancellationToken);
                int.TryParse(delayString, out delaySeconds);

                Directory.CreateDirectory(cachePath);
                var filePath = Path.Combine(cachePath, $"{songId}.mp4");
                if (File.Exists(filePath))
                {
                    _logger.LogInformation("Song {SongId} already cached at {Path}", songId, filePath);
                    return true;
                }

                var ytDlpExecutable = OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp";
                var psi = new ProcessStartInfo
                {
                    FileName = ytDlpExecutable,
                    Arguments = $"--output \"{filePath}\" -f mp4 \"{youTubeUrl}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    _logger.LogError("Failed to start yt-dlp for song {SongId}", songId);
                    return false;
                }

                string stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode == 0 && File.Exists(filePath))
                {
                    _logger.LogInformation("Cached song {SongId} at {Path}", songId, filePath);
                    return true;
                }

                _logger.LogError("yt-dlp exited with code {Code} for song {SongId}: {Error}", process.ExitCode, songId, stderr);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching song {SongId}", songId);
                return false;
            }
            finally
            {
                if (delaySeconds > 0)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                    }
                    catch (TaskCanceledException) { }
                }
                _semaphore.Release();
            }
        }
    }
}
