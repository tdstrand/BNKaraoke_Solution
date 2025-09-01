using BNKaraoke.DJ.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;

namespace BNKaraoke.DJ.ViewModels
{
    public partial class CacheManagerViewModel : ObservableObject
    {
        private readonly CacheSyncService _cacheSyncService;

        public ObservableCollection<CacheItem> Songs { get; } = new();

        public IRelayCommand CloseCommand { get; }
        public IAsyncRelayCommand<CacheItem> DownloadCommand { get; }

        public CacheManagerViewModel(CacheSyncService cacheSyncService)
        {
            _cacheSyncService = cacheSyncService;
            CloseCommand = new RelayCommand<Window>(w => w?.Close());
            DownloadCommand = new AsyncRelayCommand<CacheItem>(DownloadAsync);
        }

        public async Task LoadAsync()
        {
            try
            {
                Songs.Clear();
                var statuses = await _cacheSyncService.GetCacheStatusAsync();
                foreach (var status in statuses)
                {
                    Songs.Add(new CacheItem
                    {
                        SongId = status.SongId,
                        ServerCached = true,
                        LocalCached = status.LocalCached
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error("[CACHEMANAGER] Failed to load cache status: {Message}", ex.Message);
            }
        }

        private async Task DownloadAsync(CacheItem? item)
        {
            if (item == null || item.LocalCached) return;
            try
            {
                await _cacheSyncService.DownloadMissingAsync(new[] { item.SongId });
                item.LocalCached = true;
            }
            catch (Exception ex)
            {
                Log.Error("[CACHEMANAGER] Failed to download song {SongId}: {Message}", item?.SongId, ex.Message);
            }
        }

        public partial class CacheItem : ObservableObject
        {
            public int SongId { get; set; }

            [ObservableProperty]
            private bool serverCached;

            [ObservableProperty]
            private bool localCached;
        }
    }
}
