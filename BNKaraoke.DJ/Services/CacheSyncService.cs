using BNKaraoke.DJ.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BNKaraoke.DJ.Services
{
    public class CacheSyncService
    {
        public class CacheStatus
        {
            public int SongId { get; set; }
            public bool LocalCached { get; set; }
        }
        private readonly IApiService _apiService;
        private readonly SettingsService _settingsService;
        private readonly CancellationTokenSource _cts = new();
        private readonly ManualResetEventSlim _pauseEvent = new(true);
        private Task? _syncTask;

        public CacheSyncService(IApiService apiService, SettingsService settingsService)
        {
            _apiService = apiService ?? throw new ArgumentNullException(nameof(apiService));
            _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        }

        public async Task<List<int>> GetDiff()
        {
            var manifest = await _apiService.GetCacheManifestAsync();
            var cachePath = _settingsService.Settings.VideoCachePath;
            Directory.CreateDirectory(cachePath);

            var localIds = Directory.GetFiles(cachePath, "*.mp4")
                .Select(Path.GetFileNameWithoutExtension)
                .Select(f => int.TryParse(f, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .ToHashSet();

            var missing = manifest
                .Where(item => !localIds.Contains(item.SongId))
                .Select(item => item.SongId)
                .ToList();
            Log.Information("[CACHE SYNC] Computed diff: Manifest={ManifestCount}, Local={LocalCount}, Missing={MissingCount}", manifest.Count, localIds.Count, missing.Count);
            return missing;
        }

        public async Task<List<CacheStatus>> GetCacheStatusAsync()
        {
            var manifest = await _apiService.GetCacheManifestAsync();
            var cachePath = _settingsService.Settings.VideoCachePath;
            Directory.CreateDirectory(cachePath);

            var localIds = Directory.GetFiles(cachePath, "*.mp4")
                .Select(Path.GetFileNameWithoutExtension)
                .Select(f => int.TryParse(f, out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .ToHashSet();

            return manifest.Select(item => new CacheStatus
            {
                SongId = item.SongId,
                LocalCached = localIds.Contains(item.SongId)
            }).ToList();
        }

        public Task StartSyncAsync()
        {
            _syncTask ??= Task.Run(async () =>
            {
                var diff = await GetDiff();
                await DownloadMissingAsync(diff, _cts.Token);
            }, _cts.Token);
            return _syncTask;
        }

        public void Pause() => _pauseEvent.Reset();

        public void Resume() => _pauseEvent.Set();

        public void Cancel()
        {
            _cts.Cancel();
            _pauseEvent.Set();
        }

        public async Task DownloadMissingAsync(IEnumerable<int> songIds, CancellationToken cancellationToken = default)
        {
            var cachePath = _settingsService.Settings.VideoCachePath;
            Directory.CreateDirectory(cachePath);

            foreach (var songId in songIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _pauseEvent.Wait(cancellationToken);
                try
                {
                    using var stream = await _apiService.DownloadCachedSongAsync(songId);
                    var filePath = Path.Combine(cachePath, $"{songId}.mp4");
                    await using var fileStream = File.Create(filePath);
                    await stream.CopyToAsync(fileStream, cancellationToken);
                    Log.Information("[CACHE SYNC] Downloaded song {SongId} to {Path}", songId, filePath);
                }
                catch (Exception ex)
                {
                    Log.Error("[CACHE SYNC] Failed to download song {SongId}: {Message}", songId, ex.Message);
                }
            }
        }
    }
}
