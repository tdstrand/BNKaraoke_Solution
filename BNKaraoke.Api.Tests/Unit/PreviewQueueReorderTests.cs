using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BNKaraoke.Api.Contracts.QueueReorder;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Models;
using BNKaraoke.Api.Options;
using BNKaraoke.Api.Services.QueueReorder;
using BNKaraoke.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BNKaraoke.Api.Tests.Unit;

public class PreviewQueueReorderTests
{
    [Fact]
    public async Task Preview_AdjustsDisplayIndexUsingRelativeAssignments()
    {
        using var fixture = new DjControllerTestContext();
        await SeedQueueAsync(fixture.DbContext, includeMature: false);

        var queue = await fixture.DbContext.EventQueues
            .Include(q => q.Song)
            .OrderBy(q => q.QueueId)
            .ToListAsync();

        var optimizer = new DelegatingQueueOptimizer(_ =>
        {
            var assignments = new List<QueueReorderAssignment>
            {
                new(queue[2].QueueId, 1),
                new(queue[3].QueueId, 0)
            };

            var planItems = new List<QueueReorderPlanItem>
            {
                new(queue[2].QueueId, 0, 1, queue[2].RequestorUserName, queue[2].Song!.Mature, false, 1, Array.Empty<string>()),
                new(queue[3].QueueId, 1, 0, queue[3].RequestorUserName, queue[3].Song!.Mature, false, -1, Array.Empty<string>())
            };

            return new QueueOptimizerResult(true, false, assignments, planItems, Array.Empty<QueueReorderWarning>());
        });

        var options = new QueueReorderOptions
        {
            FrozenHeadCount = 2,
            DefaultMovementCap = 4
        };

        var controller = fixture.CreateController(optimizer, options);
        var request = new ReorderPreviewRequest
        {
            EventId = 1
        };

        var result = await controller.PreviewQueueReorder(request, default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ReorderPreviewResponse>(ok.Value);

        response.Items.Should().HaveCount(queue.Count);

        var locked = response.Items.Where(i => i.IsLocked).OrderBy(i => i.DisplayIndex).ToList();
        locked.Should().HaveCount(2);
        locked.Select(i => i.DisplayIndex).Should().Equal(0, 1);

        var reordered = response.Items.Where(i => !i.IsLocked).OrderBy(i => i.DisplayIndex).ToList();
        reordered.Should().HaveCount(2);
        reordered[0].QueueId.Should().Be(queue[3].QueueId);
        reordered[0].DisplayIndex.Should().Be(2);
        reordered[0].Movement.Should().Be(-1);
        reordered[1].QueueId.Should().Be(queue[2].QueueId);
        reordered[1].DisplayIndex.Should().Be(3);
        reordered[1].Movement.Should().Be(1);
    }

    [Fact]
    public async Task Preview_DefersMatureEntriesWhenPolicyIsDefer()
    {
        using var fixture = new DjControllerTestContext();
        await SeedQueueAsync(fixture.DbContext, includeMature: true);

        var queue = await fixture.DbContext.EventQueues
            .Include(q => q.Song)
            .OrderBy(q => q.QueueId)
            .ToListAsync();

        var optimizer = new DelegatingQueueOptimizer(_ =>
        {
            var assignments = new List<QueueReorderAssignment>
            {
                new(queue[0].QueueId, 0),
                new(queue[1].QueueId, 1)
            };

            var planItems = new List<QueueReorderPlanItem>
            {
                new(queue[0].QueueId, 0, 0, queue[0].RequestorUserName, queue[0].Song!.Mature, false, 0, Array.Empty<string>()),
                new(queue[1].QueueId, 1, 1, queue[1].RequestorUserName, queue[1].Song!.Mature, true, 0, new [] { "Deferred due to mature content policy." })
            };

            return new QueueOptimizerResult(true, false, assignments, planItems, Array.Empty<QueueReorderWarning>());
        });

        var options = new QueueReorderOptions
        {
            FrozenHeadCount = 0
        };

        var controller = fixture.CreateController(optimizer, options);
        var request = new ReorderPreviewRequest
        {
            EventId = 2,
            MaturePolicy = QueueReorderMaturePolicy.Defer.ToString()
        };

        var result = await controller.PreviewQueueReorder(request, default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ReorderPreviewResponse>(ok.Value);

        response.Items.Should().HaveCount(2);
        response.Items.Single(i => i.IsMature).IsDeferred.Should().BeTrue();
        response.Items.Single(i => i.IsMature).Reasons.Should().Contain(r => r.Contains("mature", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Preview_SetsNoAdjacentRepeatFlagWhenRequestorsRepeat()
    {
        using var fixture = new DjControllerTestContext();
        await SeedQueueAsync(fixture.DbContext, includeMature: false);

        var queue = await fixture.DbContext.EventQueues
            .Include(q => q.Song)
            .OrderBy(q => q.QueueId)
            .ToListAsync();

        queue[2].RequestorUserName = queue[0].RequestorUserName;
        fixture.DbContext.EventQueues.Update(queue[2]);
        await fixture.DbContext.SaveChangesAsync();

        var optimizer = new DelegatingQueueOptimizer(_ =>
        {
            var assignments = new List<QueueReorderAssignment>
            {
                new(queue[0].QueueId, 0),
                new(queue[2].QueueId, 1),
                new(queue[3].QueueId, 2)
            };

            var planItems = new List<QueueReorderPlanItem>
            {
                new(queue[0].QueueId, 0, 0, queue[0].RequestorUserName, queue[0].Song!.Mature, false, 0, Array.Empty<string>()),
                new(queue[2].QueueId, 1, 1, queue[2].RequestorUserName, queue[2].Song!.Mature, false, 0, Array.Empty<string>()),
                new(queue[3].QueueId, 2, 2, queue[3].RequestorUserName, queue[3].Song!.Mature, false, 0, Array.Empty<string>())
            };

            return new QueueOptimizerResult(true, false, assignments, planItems, Array.Empty<QueueReorderWarning>());
        });

        var options = new QueueReorderOptions
        {
            FrozenHeadCount = 1
        };

        var controller = fixture.CreateController(optimizer, options);
        var request = new ReorderPreviewRequest
        {
            EventId = 1
        };

        var result = await controller.PreviewQueueReorder(request, default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ReorderPreviewResponse>(ok.Value);

        response.Summary.NoAdjacentRepeat.Should().BeFalse();
    }

    [Fact]
    public async Task Preview_PassesMovementCapToOptimizer()
    {
        using var fixture = new DjControllerTestContext();
        await SeedQueueAsync(fixture.DbContext, includeMature: false);

        var queueEntry = await fixture.DbContext.EventQueues
            .Include(q => q.Song)
            .OrderBy(q => q.QueueId)
            .Skip(2)
            .FirstAsync();

        var optimizer = new DelegatingQueueOptimizer(_ =>
        {
            var assignments = new List<QueueReorderAssignment>
            {
                new(queueEntry.QueueId, 0)
            };

            var planItems = new List<QueueReorderPlanItem>
            {
                new(queueEntry.QueueId, 0, 0, queueEntry.RequestorUserName, queueEntry.Song!.Mature, false, 0, Array.Empty<string>())
            };

            return new QueueOptimizerResult(true, false, assignments, planItems, Array.Empty<QueueReorderWarning>());
        });

        var controller = fixture.CreateController(optimizer, new QueueReorderOptions { FrozenHeadCount = 0 });
        var request = new ReorderPreviewRequest
        {
            EventId = 1,
            MovementCap = 2
        };

        var result = await controller.PreviewQueueReorder(request, default);
        _ = Assert.IsType<OkObjectResult>(result);

        optimizer.LastRequest.Should().NotBeNull();
        optimizer.LastRequest!.MovementCap.Should().Be(2);
    }

    [Fact]
    public async Task Preview_SeparatesSingerFollowingLockedHeadEntry()
    {
        using var fixture = new DjControllerTestContext();
        await SeedQueueAsync(fixture.DbContext, includeMature: false);

        var queue = await fixture.DbContext.EventQueues
            .Include(q => q.Song)
            .OrderBy(q => q.Position)
            .ToListAsync();

        queue[2].RequestorUserName = queue[0].RequestorUserName;
        fixture.DbContext.EventQueues.Update(queue[2]);
        await fixture.DbContext.SaveChangesAsync();

        using var loggerFactory = LoggerFactory.Create(builder => { });
        var optimizer = new CpSatQueueOptimizer(loggerFactory.CreateLogger<CpSatQueueOptimizer>());

        var options = new QueueReorderOptions
        {
            FrozenHeadCount = 1,
            SolverNumSearchWorkers = 1,
            SolverMaxTimeMs = 2000
        };

        var controller = fixture.CreateController(optimizer, options);
        var result = await controller.PreviewQueueReorder(new ReorderPreviewRequest { EventId = 1 }, default);
        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ReorderPreviewResponse>(ok.Value);

        var lockedItem = response.Items.Single(i => i.QueueId == queue[0].QueueId);
        var repeatedItem = response.Items.Single(i => i.QueueId == queue[2].QueueId);

        (repeatedItem.DisplayIndex - lockedItem.DisplayIndex).Should().BeGreaterThanOrEqualTo(2);
        response.Summary.NoAdjacentRepeat.Should().BeTrue();
    }

    [Fact]
    public async Task Preview_UsesFixedRandomSeedForDeterministicPlans()
    {
        using var fixture = new DjControllerTestContext();
        await SeedQueueAsync(fixture.DbContext, includeMature: false);

        var optimizer = new SeededQueueOptimizer();
        var options = new QueueReorderOptions
        {
            FrozenHeadCount = 0,
            SolverRandomSeed = 42
        };

        var controller = fixture.CreateController(optimizer, options);
        var request = new ReorderPreviewRequest
        {
            EventId = 1
        };

        var first = await controller.PreviewQueueReorder(request, default) as OkObjectResult;
        var second = await controller.PreviewQueueReorder(request, default) as OkObjectResult;

        first.Should().NotBeNull();
        second.Should().NotBeNull();

        var firstResponse = Assert.IsType<ReorderPreviewResponse>(first!.Value);
        var secondResponse = Assert.IsType<ReorderPreviewResponse>(second!.Value);

        firstResponse.ProposedVersion.Should().Be(secondResponse.ProposedVersion);
        firstResponse.Items.Select(i => i.QueueId).Should().Equal(secondResponse.Items.Select(i => i.QueueId));
    }

    private static async Task SeedQueueAsync(ApplicationDbContext context, bool includeMature)
    {
        context.Users.RemoveRange(context.Users);
        context.Songs.RemoveRange(context.Songs);
        context.EventQueues.RemoveRange(context.EventQueues);
        context.Events.RemoveRange(context.Events);
        await context.SaveChangesAsync();

        var now = DateTime.UtcNow;
        var evt = DjControllerTestContext.CreateEvent(includeMature ? 2 : 1);
        context.Events.Add(evt);

        var users = new[]
        {
            DjControllerTestContext.CreateUser("user-1", "user1", "Alice", "Singer"),
            DjControllerTestContext.CreateUser("user-2", "user2", "Bob", "Crooner"),
            DjControllerTestContext.CreateUser("user-3", "user3", "Cara", "Vocalist"),
            DjControllerTestContext.CreateUser("user-4", "user4", "Dan", "Performer")
        };
        context.Users.AddRange(users);

        var songs = new[]
        {
            DjControllerTestContext.CreateSong(1, "Song 1", "Artist 1", mature: includeMature && false),
            DjControllerTestContext.CreateSong(2, "Song 2", "Artist 2", mature: includeMature),
            DjControllerTestContext.CreateSong(3, "Song 3", "Artist 3", mature: false),
            DjControllerTestContext.CreateSong(4, "Song 4", "Artist 4", mature: false)
        };
        context.Songs.AddRange(songs);

        var queues = new List<EventQueue>
        {
            DjControllerTestContext.CreateQueueEntry(1, evt.EventId, songs[0].Id, users[0].UserName!, 0, now, songs[0]),
            DjControllerTestContext.CreateQueueEntry(2, evt.EventId, songs[1].Id, users[1].UserName!, 1, now.AddSeconds(1), songs[1]),
            DjControllerTestContext.CreateQueueEntry(3, evt.EventId, songs[2].Id, users[2].UserName!, 2, now.AddSeconds(2), songs[2]),
            DjControllerTestContext.CreateQueueEntry(4, evt.EventId, songs[3].Id, users[3].UserName!, 3, now.AddSeconds(3), songs[3])
        };

        context.EventQueues.AddRange(queues);
        await context.SaveChangesAsync();
    }
}
