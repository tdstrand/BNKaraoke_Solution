using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BNKaraoke.Api.Contracts.QueueReorder;
using BNKaraoke.Api.Models;
using BNKaraoke.Api.Options;
using BNKaraoke.Api.Services.QueueReorder;
using BNKaraoke.Api.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace BNKaraoke.Api.Tests.Integration;

public class DjControllerIntegrationTests
{
    [Fact]
    public async Task PreviewApplyRoundTrip_UpdatesQueueStateAndBroadcasts()
    {
        using var fixture = new DjControllerTestContext();
        var options = new QueueReorderOptions
        {
            FrozenHeadCount = 1,
            DefaultMovementCap = 5
        };

        var queues = await SeedQueueAsync(fixture.DbContext, eventId: 100, allMature: false);
        var reorderable = queues.Skip(1).ToList();

        var optimizer = new DelegatingQueueOptimizer(_ =>
        {
            var assignments = new List<QueueReorderAssignment>
            {
                new(reorderable[0].QueueId, 2),
                new(reorderable[1].QueueId, 0),
                new(reorderable[2].QueueId, 1)
            };

            var planItems = new List<QueueReorderPlanItem>
            {
                new(reorderable[0].QueueId, 0, 2, reorderable[0].RequestorUserName, reorderable[0].Song!.Mature, false, 2, new [] { "Moved later to balance wait times." }),
                new(reorderable[1].QueueId, 1, 0, reorderable[1].RequestorUserName, reorderable[1].Song!.Mature, false, -1, new [] { "Moved earlier to improve rotation balance." }),
                new(reorderable[2].QueueId, 2, 1, reorderable[2].RequestorUserName, reorderable[2].Song!.Mature, false, -1, new [] { "Moved earlier to improve rotation balance." })
            };

            return new QueueOptimizerResult(true, false, assignments, planItems, Array.Empty<QueueReorderWarning>());
        });

        var controller = fixture.CreateController(optimizer, options);

        var previewResult = await controller.PreviewQueueReorder(new ReorderPreviewRequest { EventId = 100 }, default);
        var previewResponse = Assert.IsType<ReorderPreviewResponse>(Assert.IsType<OkObjectResult>(previewResult).Value);

        var planId = Guid.Parse(previewResponse.PlanId);
        fixture.PlanCache.Get(planId).Should().NotBeNull();
        (await fixture.DbContext.QueueReorderPlans.FindAsync(planId)).Should().NotBeNull();

        var applyRequest = new ReorderApplyRequest
        {
            EventId = 100,
            PlanId = previewResponse.PlanId,
            BasedOnVersion = previewResponse.BasedOnVersion
        };

        var applyResult = await controller.ApplyQueueReorder(applyRequest, default);
        var applyResponse = Assert.IsType<ReorderApplyResponse>(Assert.IsType<OkObjectResult>(applyResult).Value);
        applyResponse.MoveCount.Should().BeGreaterThan(0);

        (await fixture.DbContext.QueueReorderPlans.AnyAsync()).Should().BeFalse();
        fixture.PlanCache.Get(planId).Should().BeNull();

        var audits = await fixture.DbContext.QueueReorderAudits
            .Where(a => a.EventId == 100)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync();
        audits.Should().HaveCount(2);
        audits.Select(a => a.Action).Should().Equal("PREVIEW", "APPLY");

        var orderedQueue = await fixture.DbContext.EventQueues
            .AsNoTracking()
            .Where(q => q.EventId == 100)
            .OrderBy(q => q.Position)
            .ToListAsync();
        orderedQueue.Select(q => q.QueueId).Should().Equal(
            queues[0].QueueId,
            reorderable[1].QueueId,
            reorderable[2].QueueId,
            reorderable[0].QueueId);

        fixture.GroupClient.Verify(proxy => proxy.SendAsync(
            "queue/reorder_applied",
            It.Is<object>(payload =>
            {
                var type = payload.GetType();
                var eventIdProp = type.GetProperty("EventId");
                var movedProp = type.GetProperty("MovedQueueIds");
                return eventIdProp != null
                    && movedProp != null
                    && (int)eventIdProp.GetValue(payload)! == 100
                    && ((IEnumerable<int>)movedProp.GetValue(payload)!).Any();
            }),
            It.IsAny<System.Threading.CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Apply_ReturnsConflictWhenQueueVersionHasChanged()
    {
        using var fixture = new DjControllerTestContext();
        var queues = await SeedQueueAsync(fixture.DbContext, eventId: 200, allMature: false);
        var optimizer = new DelegatingQueueOptimizer(request => CreatePassthroughResult(request));
        var controller = fixture.CreateController(optimizer, new QueueReorderOptions { FrozenHeadCount = 0 });

        var preview = await controller.PreviewQueueReorder(new ReorderPreviewRequest { EventId = 200 }, default);
        var response = Assert.IsType<ReorderPreviewResponse>(Assert.IsType<OkObjectResult>(preview).Value);

        var entry = queues.First();
        entry.UpdatedAt = entry.UpdatedAt.AddMinutes(1);
        fixture.DbContext.EventQueues.Update(entry);
        await fixture.DbContext.SaveChangesAsync();

        var apply = await controller.ApplyQueueReorder(new ReorderApplyRequest
        {
            EventId = 200,
            PlanId = response.PlanId,
            BasedOnVersion = response.BasedOnVersion
        }, default);

        var conflict = Assert.IsType<ConflictObjectResult>(apply);
        JsonSerializer.Serialize(conflict.Value).Should().Contain("Queue state has changed");
    }

    [Fact]
    public async Task Preview_ReturnsUnprocessableWhenAllMatureDeferred()
    {
        using var fixture = new DjControllerTestContext();
        await SeedQueueAsync(fixture.DbContext, eventId: 300, allMature: true);
        var controller = fixture.CreateController(new DelegatingQueueOptimizer(CreatePassthroughResult), new QueueReorderOptions { FrozenHeadCount = 0 });

        var result = await controller.PreviewQueueReorder(new ReorderPreviewRequest
        {
            EventId = 300,
            MaturePolicy = QueueReorderMaturePolicy.Defer.ToString()
        }, default);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(result);
        var error = Assert.IsType<ReorderErrorResponse>(unprocessable.Value);
        error.Message.Should().Contain("mature", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Apply_ReturnsUnprocessableWhenPlanHasNoAssignments()
    {
        using var fixture = new DjControllerTestContext();
        await SeedQueueAsync(fixture.DbContext, eventId: 400, allMature: false);
        var controller = fixture.CreateController(new DelegatingQueueOptimizer(CreatePassthroughResult), new QueueReorderOptions { FrozenHeadCount = 0 });

        var preview = await controller.PreviewQueueReorder(new ReorderPreviewRequest { EventId = 400 }, default);
        var response = Assert.IsType<ReorderPreviewResponse>(Assert.IsType<OkObjectResult>(preview).Value);

        var planId = Guid.Parse(response.PlanId);
        var plan = await fixture.DbContext.QueueReorderPlans.FindAsync(planId);
        plan!.PlanJson = "[]";
        await fixture.DbContext.SaveChangesAsync();

        var apply = await controller.ApplyQueueReorder(new ReorderApplyRequest
        {
            EventId = 400,
            PlanId = response.PlanId,
            BasedOnVersion = response.BasedOnVersion
        }, default);

        var unprocessable = Assert.IsType<UnprocessableEntityObjectResult>(apply);
        JsonSerializer.Serialize(unprocessable.Value).Should().Contain("does not contain any assignments");
    }

    [Fact]
    public async Task Apply_UsesCachedPlanWhenDatabaseEntryIsMissing()
    {
        using var fixture = new DjControllerTestContext();
        await SeedQueueAsync(fixture.DbContext, eventId: 500, allMature: false);
        var controller = fixture.CreateController(new DelegatingQueueOptimizer(CreatePassthroughResult), new QueueReorderOptions { FrozenHeadCount = 0 });

        var preview = await controller.PreviewQueueReorder(new ReorderPreviewRequest { EventId = 500 }, default);
        var response = Assert.IsType<ReorderPreviewResponse>(Assert.IsType<OkObjectResult>(preview).Value);

        var planId = Guid.Parse(response.PlanId);
        var trackedPlan = await fixture.DbContext.QueueReorderPlans.FindAsync(planId);
        trackedPlan.Should().NotBeNull();
        fixture.PlanCache.Get(planId).Should().NotBeNull();

        fixture.DbContext.QueueReorderPlans.Remove(trackedPlan!);
        await fixture.DbContext.SaveChangesAsync();

        var apply = await controller.ApplyQueueReorder(new ReorderApplyRequest
        {
            EventId = 500,
            PlanId = response.PlanId,
            BasedOnVersion = response.BasedOnVersion
        }, default);

        Assert.IsType<OkObjectResult>(apply);
    }

    private static QueueOptimizerResult CreatePassthroughResult(QueueOptimizerRequest request)
    {
        var assignments = request.Items.Select((item, index) => new QueueReorderAssignment(item.QueueId, index)).ToList();
        var planItems = request.Items.Select(item => new QueueReorderPlanItem(
            item.QueueId,
            item.OriginalIndex,
            item.OriginalIndex,
            item.RequestorUserName,
            item.IsMature,
            false,
            0,
            Array.Empty<string>())).ToList();

        return new QueueOptimizerResult(true, false, assignments, planItems, Array.Empty<QueueReorderWarning>());
    }

    private static async Task<List<EventQueue>> SeedQueueAsync(ApplicationDbContext context, int eventId, bool allMature)
    {
        context.Users.RemoveRange(context.Users);
        context.Songs.RemoveRange(context.Songs);
        context.EventQueues.RemoveRange(context.EventQueues);
        context.Events.RemoveRange(context.Events);
        await context.SaveChangesAsync();

        var evt = DjControllerTestContext.CreateEvent(eventId);
        context.Events.Add(evt);

        var users = new[]
        {
            DjControllerTestContext.CreateUser($"user-{eventId}-1", $"dj{eventId}a", "Alex", "Singer"),
            DjControllerTestContext.CreateUser($"user-{eventId}-2", $"dj{eventId}b", "Bree", "Crooner"),
            DjControllerTestContext.CreateUser($"user-{eventId}-3", $"dj{eventId}c", "Corey", "Vocalist"),
            DjControllerTestContext.CreateUser($"user-{eventId}-4", $"dj{eventId}d", "Drew", "Performer")
        };
        context.Users.AddRange(users);

        var songs = new[]
        {
            DjControllerTestContext.CreateSong(eventId * 10 + 1, "One", "Artist A", mature: allMature),
            DjControllerTestContext.CreateSong(eventId * 10 + 2, "Two", "Artist B", mature: allMature),
            DjControllerTestContext.CreateSong(eventId * 10 + 3, "Three", "Artist C", mature: allMature),
            DjControllerTestContext.CreateSong(eventId * 10 + 4, "Four", "Artist D", mature: allMature)
        };
        context.Songs.AddRange(songs);

        var now = DateTime.UtcNow;
        var queues = new List<EventQueue>
        {
            DjControllerTestContext.CreateQueueEntry(eventId * 100 + 1, evt.EventId, songs[0].Id, users[0].UserName!, 0, now, songs[0]),
            DjControllerTestContext.CreateQueueEntry(eventId * 100 + 2, evt.EventId, songs[1].Id, users[1].UserName!, 1, now.AddSeconds(1), songs[1]),
            DjControllerTestContext.CreateQueueEntry(eventId * 100 + 3, evt.EventId, songs[2].Id, users[2].UserName!, 2, now.AddSeconds(2), songs[2]),
            DjControllerTestContext.CreateQueueEntry(eventId * 100 + 4, evt.EventId, songs[3].Id, users[3].UserName!, 3, now.AddSeconds(3), songs[3])
        };

        context.EventQueues.AddRange(queues);
        await context.SaveChangesAsync();

        return await context.EventQueues
            .Include(q => q.Song)
            .Where(q => q.EventId == evt.EventId)
            .OrderBy(q => q.QueueId)
            .ToListAsync();
    }
}
