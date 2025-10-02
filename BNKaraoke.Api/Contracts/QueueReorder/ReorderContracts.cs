using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace BNKaraoke.Api.Contracts.QueueReorder
{
    public enum QueueReorderMaturePolicy
    {
        Defer,
        Allow
    }

    public record ReorderPreviewRequest
    {
        [Required]
        public int EventId { get; init; }

        public string? BasedOnVersion { get; init; }

        public string? MaturePolicy { get; init; }

        public int? Horizon { get; init; }

        public int? MovementCap { get; init; }
    }

    public record ReorderApplyRequest
    {
        [Required]
        public int EventId { get; init; }

        [Required]
        public required string PlanId { get; init; }

        [Required]
        public required string BasedOnVersion { get; init; }

        public string? IdempotencyKey { get; init; }
    }

    public record QueueReorderWarningDto(string Code, string Message);

    public record QueueReorderSummaryDto(
        int MoveCount,
        double FairnessBefore,
        double FairnessAfter,
        bool NoAdjacentRepeat,
        bool RequiresConfirmation);

    public record QueueReorderPreviewItemDto(
        int QueueId,
        int OriginalIndex,
        int DisplayIndex,
        string SongTitle,
        string SongArtist,
        string Requestor,
        bool IsMature,
        bool IsLocked,
        bool IsDeferred,
        int Movement,
        IReadOnlyList<string> Reasons);

    public record ReorderPreviewResponse(
        string PlanId,
        string BasedOnVersion,
        string ProposedVersion,
        DateTime ExpiresAt,
        QueueReorderSummaryDto Summary,
        IReadOnlyList<QueueReorderPreviewItemDto> Items,
        IReadOnlyList<QueueReorderWarningDto> Warnings);

    public record ReorderApplyResponse(
        string AppliedVersion,
        int MoveCount,
        DateTime AppliedAt);

    public record ReorderErrorResponse(
        string Message,
        IReadOnlyList<QueueReorderWarningDto> Warnings);
}
