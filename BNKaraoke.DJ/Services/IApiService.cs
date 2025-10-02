using BNKaraoke.DJ.Models;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BNKaraoke.DJ.Services
{
    public interface IApiService
    {
        Task<List<EventDto>> GetLiveEventsAsync(CancellationToken cancellationToken = default);
        Task JoinEventAsync(string eventId, string requestorUserName);
        Task LeaveEventAsync(string eventId, string requestorId);
        Task<string> GetDiagnosticAsync();
        Task<LoginResult> LoginAsync(string userName, string password);
        Task<List<Singer>> GetSingersAsync(string eventId);
        Task<List<EventQueueDto>> GetQueueAsync(string eventId);
        Task<List<QueueEntry>> GetLiveQueueAsync(string eventId);
        Task<List<QueueEntry>> GetSungQueueAsync(string eventId);
        Task<int> GetSungCountAsync(string eventId);
        Task ReorderQueueAsync(string eventId, List<QueuePosition> newOrder);
        Task<ReorderPreviewResponse> PreviewQueueReorderAsync(ReorderPreviewRequest request, CancellationToken cancellationToken = default);
        Task<ReorderApplyResponse> ApplyQueueReorderAsync(ReorderApplyRequest request, CancellationToken cancellationToken = default);
        Task ResetNowPlayingAsync(string eventId);
        Task PlayAsync(string eventId, string queueId);
        Task PauseAsync(string eventId, string queueId);
        Task StopAsync(string eventId, string queueId);
        Task SkipAsync(string eventId, string queueId);
        Task LaunchVideoAsync(string eventId, string queueId);
        Task CompleteSongAsync(string eventId, int queueId);
        Task ToggleBreakAsync(string eventId, int queueId, bool isOnBreak);
        Task UpdateSingerStatusAsync(string eventId, string requestorUserName, bool isLoggedIn, bool isJoined, bool isOnBreak);
        Task AddSongAsync(string eventId, int songId, string requestorUserName, string[] singers);
        Task<List<CacheManifestItem>> GetCacheManifestAsync();
        Task<Stream> DownloadCachedSongAsync(int songId);
    }
}