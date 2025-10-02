using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BNKaraoke.Api.Contracts.QueueReorder;
using BNKaraoke.Api.Services.QueueReorder;

namespace BNKaraoke.Api.Tests.Infrastructure;

internal sealed class DelegatingQueueOptimizer : IQueueOptimizer
{
    private readonly Func<QueueOptimizerRequest, QueueOptimizerResult> _handler;

    public DelegatingQueueOptimizer(Func<QueueOptimizerRequest, QueueOptimizerResult> handler)
    {
        _handler = handler;
    }

    public QueueOptimizerRequest? LastRequest { get; private set; }

    public Task<QueueOptimizerResult> OptimizeAsync(QueueOptimizerRequest request, CancellationToken cancellationToken = default)
    {
        LastRequest = request;
        return Task.FromResult(_handler(request));
    }
}

internal sealed class SeededQueueOptimizer : IQueueOptimizer
{
    public Task<QueueOptimizerResult> OptimizeAsync(QueueOptimizerRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Items.Count == 0)
        {
            return Task.FromResult(new QueueOptimizerResult(
                IsFeasible: true,
                IsNoOp: true,
                Assignments: Array.Empty<QueueReorderAssignment>(),
                Items: Array.Empty<QueueReorderPlanItem>(),
                Warnings: Array.Empty<QueueReorderWarning>()));
        }

        var count = request.Items.Count;
        var shiftSource = request.RandomSeed ?? 0;
        var shift = count == 1 ? 0 : (Math.Abs(shiftSource) % (count - 1)) + 1;

        var assignments = new List<QueueReorderAssignment>(count);
        var planItems = new List<QueueReorderPlanItem>(count);

        foreach (var item in request.Items)
        {
            var proposed = count == 1 ? 0 : (item.OriginalIndex + shift) % count;
            assignments.Add(new QueueReorderAssignment(item.QueueId, proposed));
            var movement = proposed - item.OriginalIndex;
            planItems.Add(new QueueReorderPlanItem(
                item.QueueId,
                item.OriginalIndex,
                proposed,
                item.RequestorUserName,
                item.IsMature,
                false,
                movement,
                Array.Empty<string>()));
        }

        var isNoOp = assignments.All(a => a.ProposedIndex == request.Items.First(i => i.QueueId == a.QueueId).OriginalIndex);

        return Task.FromResult(new QueueOptimizerResult(
            IsFeasible: true,
            IsNoOp: isNoOp,
            Assignments: assignments,
            Items: planItems,
            Warnings: Array.Empty<QueueReorderWarning>()));
    }
}
