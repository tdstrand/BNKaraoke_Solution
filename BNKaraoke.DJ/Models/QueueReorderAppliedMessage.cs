using System.Collections.Generic;

namespace BNKaraoke.DJ.Models
{
    public class QueueReorderAppliedMessage
    {
        public int EventId { get; set; }
        public string Version { get; set; } = string.Empty;
        public string Mode { get; set; } = string.Empty;
        public QueueReorderMetrics Metrics { get; set; } = new QueueReorderMetrics();
        public List<QueueReorderOrderItem> Order { get; set; } = new List<QueueReorderOrderItem>();
        public List<int> MovedQueueIds { get; set; } = new List<int>();
    }

    public class QueueReorderMetrics
    {
        public int MoveCount { get; set; }
        public double FairnessBefore { get; set; }
        public double FairnessAfter { get; set; }
        public bool NoAdjacentRepeat { get; set; }
        public bool RequiresConfirmation { get; set; }
    }

    public class QueueReorderOrderItem
    {
        public int QueueId { get; set; }
        public int Position { get; set; }
    }
}
