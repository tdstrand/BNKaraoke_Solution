using BNKaraoke.Api.Data;
using BNKaraoke.Api.DTOs;
using BNKaraoke.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BNKaraoke.Api.Services;

public interface IQueueService
{
    Task<ReorderSuggestionResponse> GetComplexFairnessSuggestions(int eventId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<QueuePosition>> ApplyComplexSuggestions(ApplyReorderRequest request, CancellationToken cancellationToken = default);
}

public class QueueService : IQueueService
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;

    public QueueService(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
    {
        _context = context;
        _userManager = userManager;
    }

    public async Task<ReorderSuggestionResponse> GetComplexFairnessSuggestions(int eventId, CancellationToken cancellationToken = default)
    {
        var queueEntries = await _context.EventQueues
            .Where(q => q.EventId == eventId && q.IsActive && q.Status == "Live" && q.SungAt == null && !q.WasSkipped)
            .OrderBy(q => q.Position)
            .ToListAsync(cancellationToken);

        if (queueEntries.Count == 0)
        {
            return new ReorderSuggestionResponse();
        }

        var distinctUserNames = queueEntries
            .Select(q => q.RequestorUserName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var users = await _userManager.Users
            .Where(u => u.UserName != null && distinctUserNames.Contains(u.UserName))
            .ToListAsync(cancellationToken);

        var userByUserName = users
            .Where(u => !string.IsNullOrEmpty(u.UserName))
            .ToDictionary(u => u.UserName!, StringComparer.OrdinalIgnoreCase);

        var rolesByUserName = new Dictionary<string, IList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            rolesByUserName[user.UserName!] = roles;
        }

        var singerStatuses = await _context.SingerStatus
            .Where(ss => ss.EventId == eventId)
            .ToDictionaryAsync(ss => ss.RequestorId, cancellationToken);

        var now = DateTime.UtcNow;
        var topTwentyEntries = queueEntries.Take(20).ToList();
        var availablePositions = queueEntries.Select(q => q.Position).OrderBy(p => p).ToList();

        var suggestionDetails = new List<SuggestionDetail>(queueEntries.Count);
        foreach (var entry in queueEntries)
        {
            var detail = new SuggestionDetail(entry);
            double score = 0;

            var waitMinutes = Math.Max(0, (now - entry.CreatedAt).TotalMinutes);
            detail.WaitMinutes = waitMinutes;
            detail.WaitScore = waitMinutes * 1.5;
            score += detail.WaitScore;

            detail.SingerSongsInTopTwenty = topTwentyEntries.Count(q => string.Equals(q.RequestorUserName, entry.RequestorUserName, StringComparison.OrdinalIgnoreCase));
            if (detail.SingerSongsInTopTwenty > 3)
            {
                detail.RotationPenalty = 50 * (detail.SingerSongsInTopTwenty - 3);
                score -= detail.RotationPenalty;
            }

            ApplicationUser? user = null;
            if (!string.IsNullOrEmpty(entry.RequestorUserName) && userByUserName.TryGetValue(entry.RequestorUserName, out user))
            {
                detail.UserId = user.Id;

                if (rolesByUserName.TryGetValue(user.UserName!, out var roles))
                {
                    if (roles.Contains("VIP"))
                    {
                        detail.HasVipPriority = true;
                        score += 100;
                    }

                    if (roles.Contains("Event Manager"))
                    {
                        detail.HasManagerPriority = true;
                        score += 200;
                    }
                }
            }

            SingerStatus? status = null;
            if (user != null && singerStatuses.TryGetValue(user.Id, out status))
            {
                detail.IsSingerLoggedIn = status.IsLoggedIn;
                detail.IsSingerJoined = status.IsJoined;
                detail.IsSingerOnBreak = status.IsOnBreak;
            }

            if (status == null || !detail.IsSingerLoggedIn || !detail.IsSingerJoined)
            {
                detail.HasOfflinePenalty = true;
                score -= 300;
            }

            detail.NearbyHoldCount = queueEntries.Count(q => Math.Abs(q.Position - entry.Position) <= 5 && q.IsOnBreak);
            if (detail.NearbyHoldCount > 0)
            {
                detail.HasHoldPenalty = true;
                score -= detail.NearbyHoldCount * 20;
            }

            if (entry.IsOnBreak)
            {
                detail.HasSelfHoldPenalty = true;
                score -= 150;
            }

            detail.Score = score;
            suggestionDetails.Add(detail);
        }

        var orderedByScore = suggestionDetails
            .OrderByDescending(d => d.Score)
            .ThenBy(d => d.Entry.Position)
            .ToList();

        for (var i = 0; i < orderedByScore.Count; i++)
        {
            orderedByScore[i].SuggestedPosition = availablePositions[i];
        }

        var suggestions = suggestionDetails
            .Where(d => d.SuggestedPosition.HasValue && d.SuggestedPosition.Value != d.Entry.Position)
            .Select(d => new ReorderSuggestion
            {
                QueueId = d.Entry.QueueId,
                SingerName = string.IsNullOrWhiteSpace(d.Entry.Singers) ? d.Entry.RequestorUserName : d.Entry.Singers,
                CurrentPosition = d.Entry.Position,
                SuggestedPosition = d.SuggestedPosition!.Value,
                Reason = GenerateReason(d),
                Score = Math.Round(d.Score, 2)
            })
            .OrderBy(s => s.SuggestedPosition)
            .ToList();

        return new ReorderSuggestionResponse { Suggestions = suggestions };
    }

    public async Task<IReadOnlyList<QueuePosition>> ApplyComplexSuggestions(ApplyReorderRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var queueEntries = await _context.EventQueues
            .Where(q => q.EventId == request.EventId && q.IsActive)
            .OrderBy(q => q.Position)
            .ToListAsync(cancellationToken);

        if (queueEntries.Count == 0 || request.Reorder.Count == 0)
        {
            return queueEntries.Select(q => new QueuePosition
            {
                QueueId = q.QueueId.ToString(),
                SingerName = string.IsNullOrWhiteSpace(q.Singers) ? q.RequestorUserName : q.Singers,
                Score = 0,
                Reason = string.Empty,
                IsVip = false,
                IsOffline = false,
                OnHold = q.IsOnBreak,
                RequestorUserName = q.RequestorUserName,
                SongId = q.SongId.ToString(),
                Position = q.Position,
                UserId = string.Empty,
                AddedAt = q.CreatedAt
            }).ToList();
        }

        var suggestionMap = request.Reorder
            .GroupBy(item => item.QueueId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(item => item.SuggestedPosition).First().SuggestedPosition);

        var wrappers = new List<QueueEntryWrapper>(queueEntries.Count);
        foreach (var entry in queueEntries)
        {
            double sortKey = entry.Position;
            if (suggestionMap.TryGetValue(entry.QueueId, out var suggestedPosition))
            {
                sortKey = suggestedPosition - 0.5;
            }

            wrappers.Add(new QueueEntryWrapper(entry, sortKey));
        }

        var ordered = wrappers
            .OrderBy(w => w.SortKey)
            .ThenBy(w => w.Entry.QueueId)
            .ToList();

        var newPositions = new List<QueuePosition>(ordered.Count);
        var nextPosition = 1;
        foreach (var wrapper in ordered)
        {
            if (wrapper.Entry.Position != nextPosition)
            {
                wrapper.Entry.Position = nextPosition;
                wrapper.Entry.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                wrapper.Entry.UpdatedAt = DateTime.UtcNow;
            }

            newPositions.Add(new QueuePosition
            {
                QueueId = wrapper.Entry.QueueId.ToString(),
                SingerName = string.IsNullOrWhiteSpace(wrapper.Entry.Singers) ? wrapper.Entry.RequestorUserName : wrapper.Entry.Singers,
                Score = 0,
                Reason = string.Empty,
                IsVip = false,
                IsOffline = false,
                OnHold = wrapper.Entry.IsOnBreak,
                RequestorUserName = wrapper.Entry.RequestorUserName,
                SongId = wrapper.Entry.SongId.ToString(),
                Position = wrapper.Entry.Position,
                UserId = string.Empty,
                AddedAt = wrapper.Entry.CreatedAt
            });

            nextPosition++;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return newPositions;
    }

    private static string GenerateReason(SuggestionDetail detail)
    {
        var reasons = new List<string>();

        if (detail.WaitMinutes >= 1)
        {
            reasons.Add($"Waiting {Math.Round(detail.WaitMinutes)} minutes");
        }

        if (detail.SingerSongsInTopTwenty > 3)
        {
            reasons.Add($"Rotation penalty for {detail.SingerSongsInTopTwenty} songs in top 20");
        }

        if (detail.HasVipPriority)
        {
            reasons.Add("VIP priority");
        }

        if (detail.HasManagerPriority)
        {
            reasons.Add("Event manager priority");
        }

        if (detail.HasOfflinePenalty)
        {
            reasons.Add("Singer offline");
        }

        if (detail.HasHoldPenalty)
        {
            reasons.Add($"{detail.NearbyHoldCount} nearby hold{(detail.NearbyHoldCount == 1 ? string.Empty : "s")}");
        }

        if (detail.HasSelfHoldPenalty)
        {
            reasons.Add("Entry on hold");
        }

        if (reasons.Count == 0)
        {
            reasons.Add("Balanced placement based on fairness metrics");
        }

        return string.Join(", ", reasons);
    }

    private sealed class SuggestionDetail
    {
        public SuggestionDetail(EventQueue entry)
        {
            Entry = entry;
        }

        public EventQueue Entry { get; }
        public double Score { get; set; }
        public double WaitMinutes { get; set; }
        public double WaitScore { get; set; }
        public int SingerSongsInTopTwenty { get; set; }
        public double RotationPenalty { get; set; }
        public bool HasVipPriority { get; set; }
        public bool HasManagerPriority { get; set; }
        public bool HasOfflinePenalty { get; set; }
        public bool HasHoldPenalty { get; set; }
        public bool HasSelfHoldPenalty { get; set; }
        public int NearbyHoldCount { get; set; }
        public bool IsSingerLoggedIn { get; set; }
        public bool IsSingerJoined { get; set; }
        public bool IsSingerOnBreak { get; set; }
        public string? UserId { get; set; }
        public int? SuggestedPosition { get; set; }
    }

    private sealed record QueueEntryWrapper(EventQueue Entry, double SortKey);
}
