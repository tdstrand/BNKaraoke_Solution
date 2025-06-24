using Microsoft.AspNetCore.SignalR;

namespace BNKaraoke.Api.Hubs
{
    public class QueueHub : Hub
    {
        public async Task SendQueueUpdate(int eventId, List<object> queue)
        {
            await Clients.All.SendAsync("QueueUpdated", eventId, queue);
        }
    }
}