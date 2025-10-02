using System;

namespace BNKaraoke.Api.Data.QueueReorder
{
    public class QueueReorderPlan
    {
        public Guid PlanId { get; set; }

        public int EventId { get; set; }

        public required string BasedOnVersion { get; set; }

        public required string ProposedVersion { get; set; }

        public required string MaturePolicy { get; set; }

        public int MoveCount { get; set; }

        public required string PlanJson { get; set; }

        public string? MetadataJson { get; set; }

        public string? CreatedBy { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime ExpiresAt { get; set; }
    }
}
