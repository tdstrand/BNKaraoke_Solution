using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BNKaraoke.Api.Contracts.QueueReorder;

namespace BNKaraoke.Api.Services.QueueReorder
{
    public interface IQueueOptimizer
    {
        Task<QueueOptimizerResult> OptimizeAsync(QueueOptimizerRequest request, CancellationToken cancellationToken = default);
    }

    public record QueueOptimizerRequest(
        IReadOnlyList<QueueOptimizerItem> Items,
        QueueReorderMaturePolicy MaturePolicy,
        int? MovementCap,
        int SolverMaxTimeMilliseconds,
        int? RandomSeed,
        int NumSearchWorkers,
        int LockedHeadCount);

    public record QueueOptimizerItem(
        int QueueId,
        int OriginalIndex,
        string RequestorUserName,
        bool IsMature,
        int HistoricalCount,
        int AbsoluteOriginalIndex,
        int? PreviousAbsoluteIndex);

    public record QueueReorderAssignment(int QueueId, int ProposedIndex);

    public record QueueReorderPlanItem(
        int QueueId,
        int OriginalIndex,
        int ProposedIndex,
        string RequestorUserName,
        bool IsMature,
        bool IsDeferred,
        int Movement,
        IReadOnlyList<string> Reasons);

    public record QueueReorderWarning(string Code, string Message);

    public record QueueOptimizerResult(
        bool IsFeasible,
        bool IsNoOp,
        IReadOnlyList<QueueReorderAssignment> Assignments,
        IReadOnlyList<QueueReorderPlanItem> Items,
        IReadOnlyList<QueueReorderWarning> Warnings);
}
