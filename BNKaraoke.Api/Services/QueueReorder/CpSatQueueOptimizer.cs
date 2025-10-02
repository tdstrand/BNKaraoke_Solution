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
            var relativeSpacingTargets = new int[count];
            var relativeSpacingRequired = new bool[count];
            var absoluteSpacingTargets = new int[count];
            var absoluteSpacingRequired = new bool[count];
            var roundFairnessThresholds = new int[count];
            var roundFairnessRequired = new bool[count];
            var lastSeenBySinger = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var singerIndices = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            var absoluteMaxIndex = request.LockedHeadCount + maxIndex;
            var distinctSingerCount = request.Items
                .Select(i => i.RequestorUserName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();

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

                var historicalCount = Math.Max(request.Items[i].HistoricalCount, 0);
                var weight = 1 + historicalCount;
                moveTerms.Add(absVar * (weight * 100));

                if (distinctSingerCount > 0 && historicalCount > 0)
                {
                    var round = historicalCount;
                    var roundThresholdLong = (long)round * distinctSingerCount;
                    var roundThreshold = roundThresholdLong > int.MaxValue
                        ? int.MaxValue
                        : (int)roundThresholdLong;
                    var roundSlack = model.NewIntVar(0, roundThreshold, $"round_slack_{i}");
                    model.Add(positionVars[i] + roundSlack >= roundThreshold);

                    var fairnessWeightLong = (long)(round + 1) * distinctSingerCount * 1000;
                    var fairnessWeight = fairnessWeightLong > int.MaxValue
                        ? int.MaxValue
                        : (int)fairnessWeightLong;
                    fairnessTerms.Add(roundSlack * fairnessWeight);

                    roundFairnessRequired[i] = true;
                    roundFairnessThresholds[i] = roundThreshold;
                }

                var singer = request.Items[i].RequestorUserName ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(singer))
                {
                    if (!singerIndices.TryGetValue(singer, out var indices))
                    {
                        indices = new List<int>();
                        singerIndices[singer] = indices;
                    }

                    indices.Add(i);
                }
                if (historicalCount > 0 && !string.IsNullOrWhiteSpace(singer))
                {
                    if (lastSeenBySinger.TryGetValue(singer, out var previousIndex))
                    {
                        var minimumSpacing = Math.Min(Math.Max(1, historicalCount), maxIndex);
                        var gap = request.Items[i].OriginalIndex - previousIndex - 1;
                        if (gap < minimumSpacing)
                        {
                            var targetIndex = Math.Min(maxIndex, previousIndex + minimumSpacing + 1);
                            if (targetIndex > request.Items[i].OriginalIndex)
                            {
                                var spacingShortfall = model.NewIntVar(0, maxIndex, $"spacing_{i}");
                                model.Add(positionVars[i] + spacingShortfall >= targetIndex);

                                var fairnessWeight = (historicalCount + 1) * 250;
                                fairnessTerms.Add(spacingShortfall * fairnessWeight);

                                relativeSpacingRequired[i] = true;
                                relativeSpacingTargets[i] = targetIndex;
                            }
                        }
                    }
                    else if (request.Items[i].PreviousAbsoluteIndex.HasValue)
                    {
                        var previousAbsolute = request.Items[i].PreviousAbsoluteIndex!.Value;
                        var minimumSpacing = Math.Min(Math.Max(1, historicalCount), absoluteMaxIndex);
                        var targetAbsolute = Math.Min(absoluteMaxIndex, previousAbsolute + minimumSpacing + 1);
                        if (targetAbsolute > request.Items[i].AbsoluteOriginalIndex)
                        {
                            var spacingShortfall = model.NewIntVar(0, absoluteMaxIndex, $"abs_spacing_{i}");
                            model.Add(positionVars[i] + request.LockedHeadCount + spacingShortfall >= targetAbsolute);

                            var fairnessWeight = (historicalCount + 1) * 250;
                            fairnessTerms.Add(spacingShortfall * fairnessWeight);

                            absoluteSpacingRequired[i] = true;
                            absoluteSpacingTargets[i] = targetAbsolute;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(singer))
                {
                    lastSeenBySinger[singer] = request.Items[i].OriginalIndex;
                }
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

            foreach (var (singer, indices) in singerIndices)
            {
                if (indices.Count <= 1)
                {
                    continue;
                }

                indices.Sort((a, b) => request.Items[a].OriginalIndex.CompareTo(request.Items[b].OriginalIndex));
                var otherCount = count - indices.Count;
                var enforceablePairs = Math.Min(otherCount, indices.Count - 1);
                for (var pairIndex = 0; pairIndex < enforceablePairs; pairIndex++)
                {
                    var currentIndex = indices[pairIndex];
                    var nextIndex = indices[pairIndex + 1];
                    model.Add(positionVars[nextIndex] >= positionVars[currentIndex] + 2);
                }
            }

            var objectiveTerms = new List<LinearExpr>(moveTerms.Count + fairnessTerms.Count);
            objectiveTerms.AddRange(moveTerms);
            objectiveTerms.AddRange(fairnessTerms);
            model.Minimize(LinearExpr.Sum(objectiveTerms));

            var maxTimeSeconds = Math.Max(0.001, request.SolverMaxTimeMilliseconds / 1000.0);
            var parameterParts = new List<string>
            {
                $"max_time_in_seconds:{maxTimeSeconds:0.###}"
            };

            if (request.NumSearchWorkers > 0)
            {
                parameterParts.Add($"num_search_workers:{request.NumSearchWorkers}");
            }

            if (request.RandomSeed.HasValue)
            {
                parameterParts.Add($"random_seed:{request.RandomSeed.Value}");
            }

            var solver = new CpSolver
            {
                StringParameters = string.Join(",", parameterParts)
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

            for (var i = 0; i < request.Items.Count; i++)
            {
                var item = request.Items[i];
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
                    if (relativeSpacingRequired[i] || absoluteSpacingRequired[i])
                    {
                        reasons.Add("Moved later to avoid back-to-back turns for the same singer.");
                    }
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

                const string spacingReason = "Unable to fully separate this singer due to current queue constraints.";
                if (relativeSpacingRequired[i] && assignment.ProposedIndex < relativeSpacingTargets[i])
                {
                    reasons.Add(spacingReason);
                }

                if (absoluteSpacingRequired[i]
                    && request.LockedHeadCount + assignment.ProposedIndex < absoluteSpacingTargets[i]
                    && !reasons.Contains(spacingReason))
                {
                    reasons.Add(spacingReason);
                }

                if (roundFairnessRequired[i] && assignment.ProposedIndex < roundFairnessThresholds[i])
                {
                    reasons.Add("Moved later to allow singers with fewer turns to go first.");
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
