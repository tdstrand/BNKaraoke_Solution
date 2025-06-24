using System.Collections.Generic;

namespace BNKaraoke.DJ.Models
{
    public class ReorderQueueRequest
    {
        public List<QueuePosition> NewOrder { get; set; } = new List<QueuePosition>();
    }
}