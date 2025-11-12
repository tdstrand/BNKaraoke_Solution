using System.Collections.Generic;
using BNKaraoke.Api.Dtos;
using BNKaraoke.Api.Models;

namespace BNKaraoke.Api.Services
{
    internal static class DJQueueItemBuilder
    {
        public static SingerStatusDto BuildSingerStatus(
            EventQueueDto queueItem,
            SingerStatusData? snapshot,
            IDictionary<string, ApplicationUser> users)
        {
            var displayName = queueItem.RequestorFullName;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                if (!string.IsNullOrWhiteSpace(snapshot?.DisplayName))
                {
                    displayName = snapshot!.DisplayName;
                }
                else if (users.TryGetValue(queueItem.RequestorUserName, out var user))
                {
                    displayName = $"{user.FirstName} {user.LastName}".Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = queueItem.RequestorUserName;
            }

            var singerDto = new SingerStatusDto
            {
                UserId = queueItem.RequestorUserName,
                DisplayName = displayName,
                IsLoggedIn = snapshot?.IsLoggedIn ?? false,
                IsJoined = snapshot?.IsJoined ?? false,
                IsOnBreak = (snapshot?.IsOnBreak ?? false) || queueItem.IsOnBreak,
            };

            singerDto.Flags = BuildFlags(singerDto);
            return singerDto;
        }

        public static DJQueueItemDto BuildDjQueueItem(EventQueueDto queueItem, SingerStatusDto singer)
        {
            return new DJQueueItemDto
            {
                QueueId = queueItem.QueueId,
                EventId = queueItem.EventId,
                SongId = queueItem.SongId,
                SongTitle = queueItem.SongTitle ?? string.Empty,
                SongArtist = queueItem.SongArtist ?? string.Empty,
                YouTubeUrl = queueItem.YouTubeUrl,
                RequestorUserName = queueItem.RequestorUserName,
                RequestorDisplayName = queueItem.RequestorFullName,
                Singers = queueItem.Singers != null ? new List<string>(queueItem.Singers) : new List<string>(),
                Singer = singer,
                Position = queueItem.Position,
                Status = queueItem.Status,
                IsActive = queueItem.IsActive,
                WasSkipped = queueItem.WasSkipped,
                IsCurrentlyPlaying = queueItem.IsCurrentlyPlaying,
                IsUpNext = queueItem.IsUpNext,
                HoldReason = DetermineHoldReason(queueItem.HoldReason, singer),
                IsSingerLoggedIn = singer.IsLoggedIn,
                IsSingerJoined = singer.IsJoined,
                IsSingerOnBreak = singer.IsOnBreak,
                IsServerCached = queueItem.IsServerCached,
                IsMature = queueItem.IsMature,
                NormalizationGain = queueItem.NormalizationGain,
                FadeStartTime = queueItem.FadeStartTime,
                IntroMuteDuration = queueItem.IntroMuteDuration
            };
        }

        public static string DetermineHoldReason(string? existingHoldReason, SingerStatusDto singer)
        {
            if (!string.IsNullOrWhiteSpace(existingHoldReason))
            {
                return existingHoldReason;
            }

            if (!singer.IsJoined)
            {
                return "NotJoined";
            }

            if (!singer.IsLoggedIn)
            {
                return "NotLoggedIn";
            }

            if (singer.IsOnBreak)
            {
                return "OnBreak";
            }

            return string.Empty;
        }

        public static SingerStatusFlags BuildFlags(SingerStatusDto singer)
        {
            var flags = SingerStatusFlags.None;
            if (singer.IsLoggedIn)
            {
                flags |= SingerStatusFlags.LoggedIn;
            }

            if (singer.IsJoined)
            {
                flags |= SingerStatusFlags.Joined;
            }

            if (singer.IsOnBreak)
            {
                flags |= SingerStatusFlags.OnBreak;
            }

            return flags;
        }
    }

    internal sealed record SingerStatusData(
        string UserName,
        string DisplayName,
        bool IsLoggedIn,
        bool IsJoined,
        bool IsOnBreak);
}
