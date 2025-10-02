using System;
using System.Collections.Generic;
namespace BNKaraoke.DJ.Models
{
    public enum ReorderMode
    {
        DeferMature,
        AllowMature
    }

    public static class ReorderModeExtensions
    {
        public static string ToMaturePolicy(this ReorderMode mode)
        {
            return mode == ReorderMode.AllowMature ? "Allow" : "Defer";
        }
    }

    public record ReorderWarning(string Code, string Message, bool BlocksApproval = false);

    public class ReorderPreviewRequest
    {
        public int EventId { get; set; }
        public string? BasedOnVersion { get; set; }
        public string? MaturePolicy { get; set; }
        public int? Horizon { get; set; }
        public int? MovementCap { get; set; }
    }

    public record ReorderPreviewSummary(
        int MoveCount,
        double FairnessBefore,
        double FairnessAfter,
        bool NoAdjacentRepeat,
        bool RequiresConfirmation);

    public record ReorderPreviewDiff(string Code, string Message);

    public record ReorderPreviewResponse(
        string PlanId,
        string BasedOnVersion,
        string ProposedVersion,
        DateTime ExpiresAt,
        ReorderPreviewSummary Summary,
        IReadOnlyList<ReorderQueuePreviewItem> Items,
        IReadOnlyList<ReorderWarning> Warnings,
        IReadOnlyList<ReorderPreviewDiff>? Diffs = null);

    public class ReorderApplyRequest
    {
        public int EventId { get; set; }
        public string PlanId { get; set; } = string.Empty;
        public string BasedOnVersion { get; set; } = string.Empty;
        public string? IdempotencyKey { get; set; }
    }

    public record ReorderApplyResponse(
        string AppliedVersion,
        int MoveCount,
        DateTime AppliedAt);

    public record ReorderErrorResponse(
        string Message,
        IReadOnlyList<ReorderWarning> Warnings);
}
