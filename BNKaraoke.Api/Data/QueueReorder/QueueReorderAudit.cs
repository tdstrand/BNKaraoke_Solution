using System;

namespace BNKaraoke.Api.Data.QueueReorder
{
    public class QueueReorderAudit
    {
        public long AuditId { get; set; }

        public int EventId { get; set; }

        public Guid? PlanId { get; set; }

        public required string Action { get; set; }

        public string? UserName { get; set; }

        public string? MaturePolicy { get; set; }

        public string? PayloadJson { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
