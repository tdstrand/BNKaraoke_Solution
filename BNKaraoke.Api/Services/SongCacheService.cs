using BNKaraoke.Api.Data;
using BNKaraoke.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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

        private sealed record YtDlpAttempt(string Name, string Format, bool MergeOutputToMp4, bool RecodeVideoToMp4);

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
                var ffmpegFolder = await context.ApiSettings
                    .Where(s => s.SettingKey == "FfmpegFolder")
                    .Select(s => s.SettingValue)
                    .FirstOrDefaultAsync(cancellationToken);
                var ffmpegPathSetting = await context.ApiSettings
                    .Where(s => s.SettingKey == "FfmpegPath")
                    .Select(s => s.SettingValue)
                    .FirstOrDefaultAsync(cancellationToken);
                var ffprobePathSetting = await context.ApiSettings
                    .Where(s => s.SettingKey == "FfprobePath")
                    .Select(s => s.SettingValue)
                    .FirstOrDefaultAsync(cancellationToken);

                var ytDlpExecutable = !string.IsNullOrWhiteSpace(ytDlpPath)
                    ? ytDlpPath
                    : (OperatingSystem.IsWindows() ? "yt-dlp.exe" : "yt-dlp");
                var ffmpegLocation = NormalizeFfmpegPath(ffmpegFolder, ffmpegPathSetting);
                var ffprobeLocation = NormalizeFfprobePath(ffmpegFolder, ffprobePathSetting);
                if (!string.IsNullOrWhiteSpace(ffmpegLocation))
                {
                    _logger.LogInformation("Using configured FFmpeg path for yt-dlp downloads: {FfmpegPath}", ffmpegLocation);
                }
                else
                {
                    _logger.LogInformation("No configured FFmpeg path found for yt-dlp downloads; relying on system PATH.");
                }
                if (!string.IsNullOrWhiteSpace(ffprobeLocation))
                {
                    _logger.LogDebug("Configured FFprobe path resolved to {FfprobePath}", ffprobeLocation);
                }

                var apiKey = _configuration["YouTube:ApiKey"];

                var extractorArg = "youtube:player_client=android";
                if (!string.IsNullOrWhiteSpace(apiKey))
                {
                    extractorArg += $",api_key={apiKey}";
                }

                var downloadAttempts = new[]
                {
                    new YtDlpAttempt(
                        Name: "mp4-preferred",
                        Format: "bestvideo[ext=mp4]+bestaudio[ext=m4a]/b[ext=mp4]",
                        MergeOutputToMp4: true,
                        RecodeVideoToMp4: false),
                    new YtDlpAttempt(
                        Name: "sabr-fallback",
                        Format: "bv*+ba/b",
                        MergeOutputToMp4: false,
                        RecodeVideoToMp4: true)
                };

                for (var i = 0; i < downloadAttempts.Length; i++)
                {
                    var attempt = downloadAttempts[i];
                    _logger.LogInformation(
                        "Starting yt-dlp attempt {Attempt} ({AttemptName}) for song {SongId} using format {Format} (mergeMp4={Merge}, recodeMp4={Recode}, extractorArgs={ExtractorArgs})",
                        i + 1,
                        attempt.Name,
                        songId,
                        attempt.Format,
                        attempt.MergeOutputToMp4,
                        attempt.RecodeVideoToMp4,
                        extractorArg);

                    var psi = CreateYtDlpStartInfo(
                        ytDlpExecutable,
                        filePath,
                        attempt,
                        youTubeUrl,
                        extractorArg,
                        ffmpegLocation);

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
                        _logger.LogWarning("yt-dlp timed out after {Timeout}s for song {SongId} on attempt {Attempt} ({AttemptName})", timeoutSeconds, songId, i + 1, attempt.Name);
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }
                        if (i < downloadAttempts.Length - 1)
                        {
                            continue;
                        }
                        return false;
                    }

                    string stderr = await stderrTask;

                    if (process.ExitCode == 0 && File.Exists(filePath))
                    {
                        _logger.LogInformation("Cached song {SongId} at {Path}", songId, filePath);
                        song.Cached = true;
                        try
                        {
                            await context.SaveChangesAsync(cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error updating Cached flag for song {SongId}", songId);
                        }
                        return true;
                    }

                    _logger.LogError("yt-dlp exited with code {Code} for song {SongId} on attempt {Attempt} ({AttemptName}): {Error}", process.ExitCode, songId, i + 1, attempt.Name, stderr);
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
                    if (i < downloadAttempts.Length - 1)
                    {
                        continue;
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

        private static ProcessStartInfo CreateYtDlpStartInfo(
            string ytDlpExecutable,
            string outputPath,
            YtDlpAttempt attempt,
            string youTubeUrl,
            string? extractorArgs,
            string? ffmpegLocation)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ytDlpExecutable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            BuildYtDlpArguments(psi, outputPath, attempt, youTubeUrl, extractorArgs, ffmpegLocation);
            return psi;
        }

        private static void BuildYtDlpArguments(
            ProcessStartInfo processStartInfo,
            string outputPath,
            YtDlpAttempt attempt,
            string youTubeUrl,
            string? extractorArgs,
            string? ffmpegLocation)
        {
            processStartInfo.ArgumentList.Add("--output");
            processStartInfo.ArgumentList.Add(outputPath);
            processStartInfo.ArgumentList.Add("-f");
            processStartInfo.ArgumentList.Add(attempt.Format);

            if (attempt.MergeOutputToMp4)
            {
                processStartInfo.ArgumentList.Add("--merge-output-format");
                processStartInfo.ArgumentList.Add("mp4");
            }

            if (!string.IsNullOrWhiteSpace(extractorArgs))
            {
                processStartInfo.ArgumentList.Add("--extractor-args");
                processStartInfo.ArgumentList.Add(extractorArgs);
            }

            if (attempt.RecodeVideoToMp4)
            {
                processStartInfo.ArgumentList.Add("--recode-video");
                processStartInfo.ArgumentList.Add("mp4");
            }

            if (!string.IsNullOrWhiteSpace(ffmpegLocation))
            {
                processStartInfo.ArgumentList.Add("--ffmpeg-location");
                processStartInfo.ArgumentList.Add(ffmpegLocation);
            }

            processStartInfo.ArgumentList.Add(youTubeUrl);
        }

        private static string? NormalizeFfmpegPath(string? ffmpegFolder, string? ffmpegPath)
        {
            return NormalizeExecutablePath(ffmpegFolder, ffmpegPath, OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg");
        }

        private static string? NormalizeFfprobePath(string? ffmpegFolder, string? ffprobePath)
        {
            return NormalizeExecutablePath(ffmpegFolder, ffprobePath, OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe");
        }

        private static string? NormalizeExecutablePath(string? folder, string? path, string executableName)
        {
            if (!string.IsNullOrWhiteSpace(folder))
            {
                var normalizedFolder = NormalizePath(folder);
                return Path.Combine(normalizedFolder, executableName);
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return NormalizePath(path);
        }

        private static string NormalizePath(string path)
        {
            path = path.Replace('\\', Path.DirectorySeparatorChar);
            return Path.GetFullPath(path);
        }
    }
}
