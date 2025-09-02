using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BNKaraoke.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;

namespace BNKaraoke.Api.Services
{
    public record AudioAnalysisResult(
        float NormalizationGain,
        float FadeStartTime,
        float IntroMuteDuration,
        float InputLoudness,
        float Duration,
        float InputTruePeak,
        float InputLoudnessRange,
        float InputThreshold,
        string Summary);

    public interface IAudioAnalysisService
    {
        Task<AudioAnalysisResult?> AnalyzeAsync(string videoPath);
    }

    public class AudioAnalysisService : IAudioAnalysisService
    {
        private const float TargetLoudness = -16f; // LUFS target
        private readonly IServiceScopeFactory _scopeFactory;

        public AudioAnalysisService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public async Task<AudioAnalysisResult?> AnalyzeAsync(string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            {
                return null;
            }

            try
            {
                string ffmpegExeName = OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";
                string ffprobeExeName = OperatingSystem.IsWindows() ? "ffprobe.exe" : "ffprobe";
                string ffmpegExe = ffmpegExeName;
                string ffprobeExe = ffprobeExeName;

                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var ffmpegFolder = await context.ApiSettings
                    .Where(s => s.SettingKey == "FfmpegFolder")
                    .Select(s => s.SettingValue)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(ffmpegFolder))
                {
                    ffmpegFolder = ffmpegFolder.Replace('\\', Path.DirectorySeparatorChar);
                    ffmpegFolder = Path.GetFullPath(ffmpegFolder);
                    ffmpegExe = Path.Combine(ffmpegFolder, ffmpegExeName);
                    ffprobeExe = Path.Combine(ffmpegFolder, ffprobeExeName);
                }
                else
                {
                    var ffmpegPath = await context.ApiSettings
                        .Where(s => s.SettingKey == "FfmpegPath")
                        .Select(s => s.SettingValue)
                        .FirstOrDefaultAsync();
                    if (!string.IsNullOrWhiteSpace(ffmpegPath))
                    {
                        ffmpegExe = ffmpegPath;
                    }

                    var ffprobePath = await context.ApiSettings
                        .Where(s => s.SettingKey == "FfprobePath")
                        .Select(s => s.SettingValue)
                        .FirstOrDefaultAsync();
                    if (!string.IsNullOrWhiteSpace(ffprobePath))
                    {
                        ffprobeExe = ffprobePath;
                    }
                }

                float duration = await GetDurationAsync(videoPath, ffprobeExe);
                float introMute = await GetIntroSilenceAsync(videoPath, ffmpegExe);
                var (gain, inputLoudness, inputTp, inputLra, inputThresh, summary) = await GetNormalizationGainAsync(videoPath, ffmpegExe);
                float fadeStart = Math.Max(0, duration - 5); // default 5s fade
                return new AudioAnalysisResult(gain, fadeStart, introMute, inputLoudness, duration, inputTp, inputLra, inputThresh, summary);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<float> GetDurationAsync(string path, string ffprobeExe)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffprobeExe,
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{path}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return 0f;
            string output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (float.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dur))
            {
                return dur;
            }
            return 0f;
        }

        private static async Task<float> GetIntroSilenceAsync(string path, string ffmpegExe)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = $"-i \"{path}\" -af silencedetect=n=-50dB:d=0.5 -f null -",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return 0f;
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            // Use a verbatim string to simplify the regular expression
            var match = Regex.Match(stderr, @"silence_end: (?<time>\d+\.?\d*)");
            if (match.Success && float.TryParse(match.Groups["time"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
            {
                return t;
            }
            return 0f;
        }

        private static async Task<(float Gain, float InputLoudness, float InputTp, float InputLra, float InputThresh, string Summary)> GetNormalizationGainAsync(string path, string ffmpegExe)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = $"-i \"{path}\" -af loudnorm=I={TargetLoudness}:TP=-1.5:LRA=11:print_format=summary -f null -",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return (0f, 0f, 0f, 0f, 0f, string.Empty);
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            var start = stderr.IndexOf("Input Integrated", StringComparison.OrdinalIgnoreCase);
            string summary = start >= 0 ? stderr.Substring(start).Trim() : stderr.Trim();

            float input = 0f;
            float tp = 0f;
            float lra = 0f;
            float thresh = 0f;

            var matchI = Regex.Match(summary, @"Input Integrated:\s*(?<val>-?[0-9.]+)");
            if (matchI.Success)
            {
                float.TryParse(matchI.Groups["val"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out input);
            }

            var matchTp = Regex.Match(summary, @"Input True Peak:\s*(?<val>-?[0-9.]+)");
            if (matchTp.Success)
            {
                float.TryParse(matchTp.Groups["val"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out tp);
            }

            var matchLra = Regex.Match(summary, @"Input LRA:\s*(?<val>-?[0-9.]+)");
            if (matchLra.Success)
            {
                float.TryParse(matchLra.Groups["val"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lra);
            }

            var matchThresh = Regex.Match(summary, @"Input Threshold:\s*(?<val>-?[0-9.]+)");
            if (matchThresh.Success)
            {
                float.TryParse(matchThresh.Groups["val"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out thresh);
            }

            float gain = TargetLoudness - input;
            return (gain, input, tp, lra, thresh, summary);
        }
    }
}
