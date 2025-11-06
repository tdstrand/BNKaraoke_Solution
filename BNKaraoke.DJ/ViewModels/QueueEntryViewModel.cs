using System;
using System.Collections.Generic;
using BNKaraoke.DJ.Models;

namespace BNKaraoke.DJ.ViewModels
{
    public class QueueEntryViewModel : QueueEntry
    {
        private static readonly HashSet<string> PlayedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Sung",
            "Skipped",
            "Archived",
            "Completed",
            "Done"
        };

        public bool IsPlayed => SungAt != null || WasSkipped || (Status != null && PlayedStatuses.Contains(Status));

        public bool IsReady => IsActive && IsSingerLoggedIn && IsSingerJoined && !IsSingerOnBreak && !IsOnHold && !IsPlayed && !IsCurrentlyPlaying;

        protected override void OnPropertyChanged(string? propertyName = null)
        {
            base.OnPropertyChanged(propertyName);

            if (propertyName is nameof(SungAt) or nameof(WasSkipped) or nameof(Status))
            {
                base.OnPropertyChanged(nameof(IsPlayed));
            }

            if (propertyName is nameof(IsSingerLoggedIn)
                or nameof(IsSingerJoined)
                or nameof(IsSingerOnBreak)
                or nameof(IsActive)
                or nameof(IsOnHold)
                or nameof(IsCurrentlyPlaying)
                or nameof(SungAt)
                or nameof(WasSkipped)
                or nameof(Status))
            {
                base.OnPropertyChanged(nameof(IsReady));
            }
        }
    }
}
