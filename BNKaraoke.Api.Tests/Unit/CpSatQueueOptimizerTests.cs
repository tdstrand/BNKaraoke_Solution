using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BNKaraoke.Api.Contracts.QueueReorder;
using BNKaraoke.Api.Services.QueueReorder;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BNKaraoke.Api.Tests.Unit;

public class CpSatQueueOptimizerTests
{
    [Fact]
    public async Task OptimizeAsync_SpacesConsecutiveEntriesFromSameSinger()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var optimizer = new CpSatQueueOptimizer(loggerFactory.CreateLogger<CpSatQueueOptimizer>());

        var items = new List<QueueOptimizerItem>
        {
            new(1, 0, "A", false, 0, 0, null),
            new(2, 1, "A", false, 1, 1, 0),
            new(3, 2, "B", false, 0, 2, null),
            new(4, 3, "C", false, 0, 3, null)
        };

        var request = new QueueOptimizerRequest(
            items,
            QueueReorderMaturePolicy.Allow,
            MovementCap: null,
            SolverMaxTimeMilliseconds: 2000,
            RandomSeed: 1,
            NumSearchWorkers: 1,
            LockedHeadCount: 0);

        var result = await optimizer.OptimizeAsync(request);

        result.IsFeasible.Should().BeTrue();
        result.IsNoOp.Should().BeFalse();

        var assignmentsByQueue = result.Assignments.ToDictionary(a => a.QueueId);
        assignmentsByQueue[2].ProposedIndex.Should().BeGreaterThan(1);

        var itemLookup = items.ToDictionary(i => i.QueueId);
        var orderedSingers = result.Assignments
            .OrderBy(a => a.ProposedIndex)
            .Select(a => itemLookup[a.QueueId].RequestorUserName)
            .ToList();

        for (var i = 1; i < orderedSingers.Count; i++)
        {
            orderedSingers[i].Should().NotBe(orderedSingers[i - 1]);
        }

        var reasons = result.Items.Single(i => i.QueueId == 2).Reasons;
        reasons.Should().Contain(reason => reason.Contains("avoid back-to-back", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OptimizeAsync_SeparatesMultipleOccurrencesWhenAlternativesExist()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var optimizer = new CpSatQueueOptimizer(loggerFactory.CreateLogger<CpSatQueueOptimizer>());

        var items = new List<QueueOptimizerItem>
        {
            new(1, 0, "A", false, 0, 0, null),
            new(2, 1, "A", false, 1, 1, 0),
            new(3, 2, "B", false, 0, 2, null),
            new(4, 3, "A", false, 2, 3, 1),
            new(5, 4, "C", false, 0, 4, null)
        };

        var request = new QueueOptimizerRequest(
            items,
            QueueReorderMaturePolicy.Allow,
            MovementCap: null,
            SolverMaxTimeMilliseconds: 2000,
            RandomSeed: 1,
            NumSearchWorkers: 1,
            LockedHeadCount: 0);

        var result = await optimizer.OptimizeAsync(request);

        result.IsFeasible.Should().BeTrue();
        result.IsNoOp.Should().BeFalse();

        var orderedItems = result.Assignments
            .OrderBy(a => a.ProposedIndex)
            .Select(a => items.First(i => i.QueueId == a.QueueId))
            .ToList();

        for (var i = 1; i < orderedItems.Count; i++)
        {
            var current = orderedItems[i];
            var previous = orderedItems[i - 1];
            if (!string.Equals(current.RequestorUserName, "A", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(previous.RequestorUserName, "A", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Assert.True(false, "Adjacent entries for singer A were not separated.");
        }
    }

    [Fact]
    public async Task OptimizeAsync_AllowsAdjacencyWhenInsufficientAlternatives()
    {
        using var loggerFactory = LoggerFactory.Create(builder => { });
        var optimizer = new CpSatQueueOptimizer(loggerFactory.CreateLogger<CpSatQueueOptimizer>());

        var items = new List<QueueOptimizerItem>
        {
            new(1, 0, "A", false, 0, 0, null),
            new(2, 1, "A", false, 1, 1, 0),
            new(3, 2, "A", false, 2, 2, 1),
            new(4, 3, "B", false, 0, 3, null)
        };

        var request = new QueueOptimizerRequest(
            items,
            QueueReorderMaturePolicy.Allow,
            MovementCap: null,
            SolverMaxTimeMilliseconds: 2000,
            RandomSeed: 1,
            NumSearchWorkers: 1,
            LockedHeadCount: 0);

        var result = await optimizer.OptimizeAsync(request);

        result.IsFeasible.Should().BeTrue();
        result.IsNoOp.Should().BeFalse();
    }
}
