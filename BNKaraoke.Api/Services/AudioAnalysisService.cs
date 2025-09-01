using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BNKaraoke.Api.Services
{
    public record AudioAnalysisResult(float NormalizationGain, float FadeStartTime, float IntroMuteDuration);

    public interface IAudioAnalysisService
    {
        Task<AudioAnalysisResult?> AnalyzeAsync(string videoPath);
    }

    public class AudioAnalysisService : IAudioAnalysisService
    {
        private const float TargetLoudness = -14f; // LUFS target

        public async Task<AudioAnalysisResult?> AnalyzeAsync(string videoPath)
        {
            if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            {
                return null;
            }

            try
            {
                float duration = await GetDurationAsync(videoPath);
                float introMute = await GetIntroSilenceAsync(videoPath);
                float gain = await GetNormalizationGainAsync(videoPath);
                float fadeStart = Math.Max(0, duration - 5); // default 5s fade
                return new AudioAnalysisResult(gain, fadeStart, introMute);
            }
            catch
            {
                return null;
            }
        }

        private static async Task<float> GetDurationAsync(string path)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffprobe",
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

        private static async Task<float> GetIntroSilenceAsync(string path)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
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
            var match = Regex.Match(stderr, "silence_end: (?<time>\\d+\\.?\\d*)");
            if (match.Success && float.TryParse(match.Groups["time"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var t))
            {
                return t;
            }
            return 0f;
        }

        private static async Task<float> GetNormalizationGainAsync(string path)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-i \"{path}\" -af loudnorm=I={TargetLoudness}:TP=-1.5:LRA=11 -f null -",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null) return 0f;
            string stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            var match = Regex.Match(stderr, "Input Integrated:\s*(?<lufs>-?[0-9.]+)");
            if (match.Success && float.TryParse(match.Groups["lufs"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var input))
            {
                return TargetLoudness - input;
            }
            return 0f;
        }
    }
}
