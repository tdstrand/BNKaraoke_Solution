using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BNKaraoke.Api.Data;
using BNKaraoke.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace BNKaraoke.Api.Services
{
    public class ReorderSuggestion
    {
        public int QueueId { get; set; }
        public string SingerName { get; set; } = "";
        public int CurrentPosition { get; set; }
        public int SuggestedPosition { get; set; }
        public string Reason { get; set; } = "";
        public int Score { get; set; }
    }

    public class ReorderSuggestionResponse
    {
        public List<ReorderSuggestion> Suggestions { get; set; } = new();
    }

    public class ApplyReorderRequest
    {
        public int EventId { get; set; }
        public List<ReorderSuggestion> Reorder { get; set; } = new();
    }

    public interface IQueueService
    {
        Task<ReorderSuggestionResponse> GetComplexFairnessSuggestions(int eventId, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<QueuePosition>> ApplyComplexSuggestions(ApplyReorderRequest request, CancellationToken cancellationToken = default);
        Task ReorderGlobalQueueAsync(List<int> newOrderIds);
        Task ReorderPersonalQueueAsync(string userId, List<int> newOrderIds);
    }

    public class QueueService : IQueueService
    {
        private readonly ApplicationDbContext _context;

        public QueueService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ReorderSuggestionResponse> GetComplexFairnessSuggestions(int eventId, CancellationToken cancellationToken = default)
        {
            var queueEntries = await _context.EventQueues
                .Where(q => q.EventId == eventId && q.IsActive && !q.WasSkipped && q.SungAt == null)
                .OrderBy(q => q.Position)
                .ToListAsync(cancellationToken);

            if (!queueEntries.Any())
                return new ReorderSuggestionResponse();

            var now = DateTime.UtcNow;

            // VIP via Identity roles
            var vipUserNames = await _context.Users
                .Where(u => _context.UserRoles.Any(ur => ur.UserId == u.Id &&
                    _context.Roles.Any(r => r.Id == ur.RoleId && r.Name == "VIP")))
                .Select(u => u.UserName!)
                .ToListAsync(cancellationToken);

            var scored = queueEntries.Select(q => new
            {
                Entry = q,
                Score = 100
                        + (vipUserNames.Contains(q.RequestorUserName) ? 50 : 0)
                        - (q.IsOnBreak ? 40 : 0)
                        + (int)Math.Min((now - q.CreatedAt).TotalMinutes * 2, 60)
                        - (_context.EventQueues.Count(x => x.RequestorUserName == q.RequestorUserName && x.CreatedAt > now.AddHours(-2)) * 15)
            })
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Entry.CreatedAt)
            .Select((x, i) => new { x.Entry, x.Score, SuggestedPosition = i + 1 })
            .ToList();

            var suggestions = scored
                .Where(x => x.SuggestedPosition != x.Entry.Position)
                .Select(x => new ReorderSuggestion
                {
                    QueueId = x.Entry.QueueId,
                    SingerName = string.IsNullOrWhiteSpace(x.Entry.Singers) ? x.Entry.RequestorUserName : x.Entry.Singers,
                    CurrentPosition = x.Entry.Position,
                    SuggestedPosition = x.SuggestedPosition,
                    Reason = BuildReason(x.Entry, vipUserNames),
                    Score = x.Score
                })
                .OrderBy(s => s.SuggestedPosition)
                .ToList();

            return new ReorderSuggestionResponse { Suggestions = suggestions };
        }

        public async Task<IReadOnlyList<QueuePosition>> ApplyComplexSuggestions(ApplyReorderRequest request, CancellationToken cancellationToken = default)
        {
            var entries = await _context.EventQueues
                .Where(q => q.EventId == request.EventId && request.Reorder.Any(r => r.QueueId == q.QueueId))
                .ToListAsync(cancellationToken);

            foreach (var item in request.Reorder)
            {
                var entry = entries.FirstOrDefault(e => e.QueueId == item.QueueId);
                if (entry != null)
                {
                    entry.Position = item.SuggestedPosition;
                    entry.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync(cancellationToken);

            var vipUserNames = await _context.Users
                .Where(u => _context.UserRoles.Any(ur => ur.UserId == u.Id &&
                    _context.Roles.Any(r => r.Id == ur.RoleId && r.Name == "VIP")))
                .Select(u => u.UserName!)
                .ToListAsync(cancellationToken);

            return entries.Select(e => new QueuePosition
            {
                QueueId = e.QueueId.ToString(),
                SingerName = string.IsNullOrWhiteSpace(e.Singers) ? e.RequestorUserName : e.Singers,
                Score = 0,
                Reason = "Applied",
                IsVip = vipUserNames.Contains(e.RequestorUserName),
                IsOffline = false,
                OnHold = e.IsOnBreak,
                RequestorUserName = e.RequestorUserName,
                SongId = e.SongId.ToString(),
                Position = e.Position,
                UserId = e.RequestorUserName,
                AddedAt = e.CreatedAt
            }).ToList();
        }

        private string BuildReason(EventQueue entry, List<string> vipUserNames)
        {
            var reasons = new List<string>();
            if (vipUserNames.Contains(entry.RequestorUserName)) reasons.Add("VIP");
            if (entry.IsOnBreak) reasons.Add("On Break");
            var wait = (DateTime.UtcNow - entry.CreatedAt).TotalMinutes;
            if (wait > 30) reasons.Add($"Waited {(int)wait}m");
            return reasons.Any() ? string.Join(", ", reasons) : "Balanced";
        }

        public async Task ReorderGlobalQueueAsync(List<int> newOrderIds)
        {
            var entries = await _context.EventQueues
                .Where(q => newOrderIds.Contains(q.QueueId))
                .ToListAsync();

            for (int i = 0; i < newOrderIds.Count; i++)
            {
                var entry = entries.FirstOrDefault(e => e.QueueId == newOrderIds[i]);
                if (entry != null)
                    entry.Position = i + 1;
            }

            await _context.SaveChangesAsync();
        }

        public async Task ReorderPersonalQueueAsync(string userId, List<int> newOrderIds)
        {
            var entries = await _context.EventQueues
                .Where(q => q.RequestorUserName == userId && newOrderIds.Contains(q.QueueId))
                .ToListAsync();

            for (int i = 0; i < newOrderIds.Count; i++)
            {
                var entry = entries.FirstOrDefault(e => e.QueueId == newOrderIds[i]);
                if (entry != null)
                    entry.Position = i + 1;
            }

            await _context.SaveChangesAsync();
        }
    }
}