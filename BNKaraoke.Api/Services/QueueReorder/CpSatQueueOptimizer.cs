using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BNKaraoke.Api.Contracts.QueueReorder;
using Google.OrTools.Sat;
using Microsoft.Extensions.Logging;

namespace BNKaraoke.Api.Services.QueueReorder
{
    public class CpSatQueueOptimizer : IQueueOptimizer
    {
        private readonly ILogger<CpSatQueueOptimizer> _logger;

        public CpSatQueueOptimizer(ILogger<CpSatQueueOptimizer> logger)
        {
            _logger = logger;
        }

        public Task<QueueOptimizerResult> OptimizeAsync(QueueOptimizerRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Items.Count <= 1)
            {
                var passthroughItems = request.Items.Select(item =>
                    new QueueReorderPlanItem(
                        item.QueueId,
                        item.OriginalIndex,
                        item.OriginalIndex,
                        item.RequestorUserName,
                        item.IsMature,
                        false,
                        0,
                        Array.Empty<string>()))
                    .ToList();

                return Task.FromResult(new QueueOptimizerResult(
                    IsFeasible: true,
                    IsNoOp: true,
                    Assignments: request.Items.Select(i => new QueueReorderAssignment(i.QueueId, i.OriginalIndex)).ToList(),
                    Items: passthroughItems,
                    Warnings: Array.Empty<QueueReorderWarning>()));
            }

            var model = new CpModel();
            var count = request.Items.Count;
            var maxIndex = count - 1;

            var positionVars = new IntVar[count];
            var moveTerms = new List<LinearExpr>(count * 2);
            var fairnessTerms = new List<LinearExpr>(count);

            for (var i = 0; i < count; i++)
            {
                positionVars[i] = model.NewIntVar(0, maxIndex, $"pos_{i}");
            }

            model.AddAllDifferent(positionVars);

            for (var i = 0; i < count; i++)
            {
                var originalIndex = request.Items[i].OriginalIndex;
                var maxTravel = maxIndex;
                var diffVar = model.NewIntVar(-maxTravel, maxTravel, $"diff_{i}");
                model.Add(diffVar == positionVars[i] - originalIndex);

                var absVar = model.NewIntVar(0, maxTravel, $"abs_{i}");
                model.AddAbsEquality(absVar, diffVar);

                if (request.MovementCap.HasValue)
                {
                    model.Add(absVar <= request.MovementCap.Value);
                }

                var weight = 1 + Math.Max(request.Items[i].HistoricalCount, 0);
                moveTerms.Add(absVar * (weight * 100));
                fairnessTerms.Add(positionVars[i] * weight);
            }

            if (request.MaturePolicy == QueueReorderMaturePolicy.Defer)
            {
                var matureIndices = Enumerable.Range(0, count)
                    .Where(i => request.Items[i].IsMature)
                    .ToList();
                var nonMatureIndices = Enumerable.Range(0, count)
                    .Where(i => !request.Items[i].IsMature)
                    .ToList();

                if (matureIndices.Count > 0 && nonMatureIndices.Count > 0)
                {
                    foreach (var matureIndex in matureIndices)
                    {
                        foreach (var nonMatureIndex in nonMatureIndices)
                        {
                            model.Add(positionVars[matureIndex] >= positionVars[nonMatureIndex] + 1);
                        }
                    }
                }
            }

            var objectiveTerms = new List<LinearExpr>(moveTerms.Count + fairnessTerms.Count);
            objectiveTerms.AddRange(moveTerms);
            objectiveTerms.AddRange(fairnessTerms);
            model.Minimize(LinearExpr.Sum(objectiveTerms));

            var solver = new CpSolver
            {
                StringParameters = $"max_time_in_seconds:{Math.Max(0.1, request.MaxSolveSeconds):0.###},num_search_workers:8"
            };

            var status = solver.Solve(model);
            _logger.LogInformation("Queue optimization solver status: {Status}", status);

            if (status != CpSolverStatus.Optimal && status != CpSolverStatus.Feasible)
            {
                _logger.LogWarning("Queue optimization infeasible: status {Status}", status);
                return Task.FromResult(new QueueOptimizerResult(
                    IsFeasible: false,
                    IsNoOp: true,
                    Assignments: Array.Empty<QueueReorderAssignment>(),
                    Items: Array.Empty<QueueReorderPlanItem>(),
                    Warnings: new[] { new QueueReorderWarning("SOLVER_INFEASIBLE", "The queue optimizer could not find a feasible solution with the provided constraints.") }));
            }

            var assignments = new List<QueueReorderAssignment>(count);
            var planItems = new List<QueueReorderPlanItem>(count);

            for (var i = 0; i < count; i++)
            {
                var proposedIndex = (int)solver.Value(positionVars[i]);
                assignments.Add(new QueueReorderAssignment(request.Items[i].QueueId, proposedIndex));
            }

            var isNoOp = true;
            var warnings = new List<QueueReorderWarning>();

            foreach (var item in request.Items)
            {
                var assignment = assignments.First(a => a.QueueId == item.QueueId);
                var movement = assignment.ProposedIndex - item.OriginalIndex;
                if (movement != 0)
                {
                    isNoOp = false;
                }

                var reasons = new List<string>();
                if (movement < 0)
                {
                    reasons.Add("Moved earlier to improve rotation balance.");
                }
                else if (movement > 0)
                {
                    reasons.Add("Moved later to balance wait times.");
                }

                var isDeferred = false;
                if (item.IsMature && request.MaturePolicy == QueueReorderMaturePolicy.Defer)
                {
                    var nonMatureCount = request.Items.Count(i => !i.IsMature);
                    if (nonMatureCount > 0 && assignment.ProposedIndex >= nonMatureCount)
                    {
                        isDeferred = true;
                        reasons.Add("Deferred due to mature content policy.");
                    }
                }

                planItems.Add(new QueueReorderPlanItem(
                    item.QueueId,
                    item.OriginalIndex,
                    assignment.ProposedIndex,
                    item.RequestorUserName,
                    item.IsMature,
                    isDeferred,
                    movement,
                    reasons));
            }

            return Task.FromResult(new QueueOptimizerResult(
                IsFeasible: true,
                IsNoOp: isNoOp,
                Assignments: assignments,
                Items: planItems,
                Warnings: warnings));
        }
    }
}
