using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using BNKaraoke.Api.Constants;
using BNKaraoke.Api.Controllers;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Hubs;
using BNKaraoke.Api.Models;
using BNKaraoke.Api.Options;
using BNKaraoke.Api.Services.QueueReorder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace BNKaraoke.Api.Tests.Infrastructure;

internal sealed class DjControllerTestContext : IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly Mock<IHubClients> _hubClientsMock;
    private readonly Mock<IClientProxy> _groupClientMock;

    public DjControllerTestContext()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .EnableSensitiveDataLogging()
            .Options;

        DbContext = new ApplicationDbContext(dbOptions);
        DbContext.Database.EnsureCreated();

        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        PlanCache = new QueueReorderPlanCache(_memoryCache, Mock.Of<ILogger<QueueReorderPlanCache>>());

        _groupClientMock = new Mock<IClientProxy>();
        _hubClientsMock = new Mock<IHubClients>();
        _hubClientsMock
            .Setup(clients => clients.Group(It.IsAny<string>()))
            .Returns(_groupClientMock.Object);

        var hubContextMock = new Mock<IHubContext<KaraokeDJHub>>();
        hubContextMock.SetupGet(h => h.Clients).Returns(_hubClientsMock.Object);
        hubContextMock.SetupGet(h => h.Groups).Returns(Mock.Of<IGroupManager>());
        HubContext = hubContextMock;
    }

    public ApplicationDbContext DbContext { get; }

    public QueueReorderPlanCache PlanCache { get; }

    public Mock<IHubContext<KaraokeDJHub>> HubContext { get; }

    public Mock<IClientProxy> GroupClient => _groupClientMock;

    public DJController CreateController(IQueueOptimizer optimizer, QueueReorderOptions? options = null)
    {
        options ??= new QueueReorderOptions();
        var controller = new DJController(
            DbContext,
            Mock.Of<ILogger<DJController>>(),
            HubContext.Object,
            Mock.Of<IHttpClientFactory>(),
            optimizer,
            PlanCache,
            Microsoft.Extensions.Options.Options.Create(options));

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "test-dj"),
            new(ClaimTypes.Role, RoleConstants.KaraokeDj)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = principal
            }
        };

        return controller;
    }

    public static ApplicationUser CreateUser(string id, string userName, string firstName, string lastName)
    {
        return new ApplicationUser
        {
            Id = id,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName}@example.com".ToUpperInvariant(),
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString("N"),
            PhoneNumber = "555-0100",
            PhoneNumberConfirmed = true,
            FirstName = firstName,
            LastName = lastName,
            LastActivity = DateTime.UtcNow
        };
    }

    public static Song CreateSong(int id, string title, string artist, bool mature = false)
    {
        return new Song
        {
            Id = id,
            Title = title,
            Artist = artist,
            Status = "Available",
            Cached = true,
            Mature = mature
        };
    }

    public static Event CreateEvent(int id)
    {
        var now = DateTime.UtcNow;
        return new Event
        {
            EventId = id,
            EventCode = $"EVT{id}",
            Description = "Test Event",
            Status = "Live",
            Visibility = "Visible",
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    public static EventQueue CreateQueueEntry(
        int queueId,
        int eventId,
        int songId,
        string requestor,
        int position,
        DateTime timestamp,
        Song song)
    {
        return new EventQueue
        {
            QueueId = queueId,
            EventId = eventId,
            SongId = songId,
            Song = song,
            RequestorUserName = requestor,
            Singers = "[]",
            Position = position,
            Status = "Live",
            IsActive = true,
            WasSkipped = false,
            IsCurrentlyPlaying = false,
            IsOnBreak = false,
            CreatedAt = timestamp,
            UpdatedAt = timestamp
        };
    }

    public void Dispose()
    {
        DbContext.Dispose();
        _memoryCache.Dispose();
    }
}
