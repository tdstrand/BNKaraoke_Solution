using BNKaraoke.Api.Data;
using BNKaraoke.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BNKaraoke.Api.Services
{
    public interface ISongCacheService
    {
        Task<bool> CacheSongAsync(int songId, string youTubeUrl, CancellationToken cancellationToken = default);

        Task<FileInfo?> GetCachedSongFileInfoAsync(int songId, CancellationToken cancellationToken = default);

        Task<FileStream?> OpenCachedSongStreamAsync(int songId, CancellationToken cancellationToken = default);
    }

    public class SongCacheService : ISongCacheService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SongCacheService> _logger;
        private readonly IConfiguration _configuration;
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public SongCacheService(
            IServiceScopeFactory scopeFactory,
            ILogger<SongCacheService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task<bool> CacheSongAsync(int songId, string youTubeUrl, CancellationToken cancellationToken = default)
        {
            await _semaphore.WaitAsync(cancellationToken);
            string cachePath = "";
            int delaySeconds = 0;
            int timeoutSeconds = 0;
            try
            {
                  using var scope = _scopeFactory.CreateScope();
                  var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                  var song = await context.Songs.FindAsync(new object[] { songId }, cancellationToken);
                  if (song == null)
                  {
                      _logger.LogWarning("CacheSongAsync: Song {SongId} not found", songId);
                      return false;
                  }
                  if (song.Mature)
                  {
                      _logger.LogInformation("CacheSongAsync: Skipping mature song {SongId}", songId);
                      return false;
                  }

                  cachePath = await context.ApiSettings
                      .Where(s => s.SettingKey == "CacheStoragePath")
                      .Select(s => s.SettingValue)
                      .FirstOrDefaultAsync(cancellationToken) ?? "cache";

                // Normalize the configured path so that Windows style paths work on all platforms
                cachePath = cachePath.Replace('\\', Path.DirectorySeparatorChar);
                cachePath = Path.GetFullPath(cachePath);
                var delayString = await context.ApiSettings
                    .Where(s => s.SettingKey == "CacheDownloadDelaySeconds")
                    .Select(s => s.SettingValue)
                    .FirstOrDefaultAsync(cancellationToken);
                int.TryParse(delayString, out delaySeconds);

                var timeoutString = await context.ApiSettings
                    .Where(s => s.SettingKey == "YtDlpTimeout")
                    .Select(s => s.SettingValue)
                    .FirstOrDefaultAsync(cancellationToken);
                int.TryParse(timeoutString, out timeoutSeconds);

                Directory.CreateDirectory(cachePath);
                var filePath = Path.Combine(cachePath, $"{songId}.mp4");
                filePath = Path.GetFullPath(filePath);
                if (File.Exists(filePath))
                {
                    _logger.LogInformation("Song {SongId} already cached at {Path}", songId, filePath);
                    return true;
                }

                var ytDlpPath = await context.ApiSettings
                    .Where(s => s.SettingKey == "YtDlpPath")
                    .Select(s => s.SettingValue)
                    .FirstOrDefaultAsync(cancellationToken);
                var ytDlpExecutable = !string.IsNullOrWhiteSpace(ytDlpPath)
                    ? ytDlpPath
                    : (OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");
                var apiKey = _configuration["YouTube:ApiKey"];
                // Download best available MP4 video with AAC audio for high-quality playback
                var arguments =
                    $"--output \"{filePath}\" -f \"bestvideo[ext=mp4]+bestaudio[ext=m4a]/b[ext=mp4]\" --merge-output-format mp4 \"{youTubeUrl}\"";
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    arguments += $" --extractor-args \"youtube:api_key={apiKey}\"";
                }

                for (int attempt = 1; attempt <= 2; attempt++)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = ytDlpExecutable,
                        Arguments = arguments,
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

                    var stderrTask = process.StandardError.ReadToEndAsync();
                    var exitTask = process.WaitForExitAsync(cancellationToken);
                    Task completedTask;
                    if (timeoutSeconds > 0)
                    {
                        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cancellationToken);
                        completedTask = await Task.WhenAny(exitTask, timeoutTask);
                    }
                    else
                    {
                        completedTask = await Task.WhenAny(exitTask);
                    }

                    if (completedTask != exitTask)
                    {
                        try
                        {
                            process.Kill(true);
                        }
                        catch { }
                        _logger.LogWarning("yt-dlp timed out after {Timeout}s for song {SongId} (attempt {Attempt})", timeoutSeconds, songId, attempt);
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                        if (attempt == 1)
                        {
                            // retry once
                            continue;
                        }
                        return false;
                    }

                    string stderr = await stderrTask;

                    if (process.ExitCode == 0 && File.Exists(filePath))
                    {
                        _logger.LogInformation("Cached song {SongId} at {Path}", songId, filePath);
                        return true;
                    }

                      _logger.LogError("yt-dlp exited with code {Code} for song {SongId}: {Error}", process.ExitCode, songId, stderr);
                      if (stderr.Contains("confirm your age", StringComparison.OrdinalIgnoreCase) ||
                          stderr.Contains("inappropriate for some users", StringComparison.OrdinalIgnoreCase) ||
                          stderr.Contains("age-restricted", StringComparison.OrdinalIgnoreCase))
                      {
                          _logger.LogWarning("Marking song {SongId} as mature due to age restriction", songId);
                          song.Mature = true;
                          try
                          {
                              await context.SaveChangesAsync(cancellationToken);
                          }
                          catch (Exception ex)
                          {
                              _logger.LogError(ex, "Error updating Mature flag for song {SongId}", songId);
                          }
                      }
                      if (File.Exists(filePath))
                      {
                          File.Delete(filePath);
                      }
                      return false;
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

        private async Task<string> GetCachedFilePathAsync(int songId, CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cachePath = await context.ApiSettings
                .Where(s => s.SettingKey == "CacheStoragePath")
                .Select(s => s.SettingValue)
                .FirstOrDefaultAsync(cancellationToken) ?? "cache";

            cachePath = cachePath.Replace('\\', Path.DirectorySeparatorChar);
            cachePath = Path.GetFullPath(cachePath);
            Directory.CreateDirectory(cachePath);
            var filePath = Path.Combine(cachePath, $"{songId}.mp4");
            return Path.GetFullPath(filePath);
        }

        public async Task<FileInfo?> GetCachedSongFileInfoAsync(int songId, CancellationToken cancellationToken = default)
        {
            var filePath = await GetCachedFilePathAsync(songId, cancellationToken);
            if (File.Exists(filePath))
            {
                return new FileInfo(filePath);
            }
            return null;
        }

        public async Task<FileStream?> OpenCachedSongStreamAsync(int songId, CancellationToken cancellationToken = default)
        {
            var filePath = await GetCachedFilePathAsync(songId, cancellationToken);
            if (!File.Exists(filePath))
            {
                return null;
            }
            return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        }
    }
}
