using System;
using System.Collections.Generic;
using System.Linq;
using BNKaraoke.DJ.Models;

namespace BNKaraoke.DJ.Services.Playback
{
    public class NowNextResolver
    {
        private readonly IReadOnlyList<QueueEntry> _queue;
        private readonly QueueEntry? _playhead;

        public NowNextResolver(IReadOnlyList<QueueEntry>? queue, QueueEntry? playhead)
        {
            _queue = queue ?? Array.Empty<QueueEntry>();
            _playhead = playhead;
        }

        public QueueEntry? ResolveNow()
        {
            if (_playhead == null)
            {
                return null;
            }

            var matching = _queue.FirstOrDefault(entry => entry.QueueId == _playhead.QueueId);
            return matching ?? _playhead;
        }

        public QueueEntry? ResolveUpNext(ReorderMode matureMode)
        {
            var ordered = _queue
                .Where(entry => entry.IsActive && !entry.IsOnHold)
                .OrderBy(entry => entry.Position)
                .ToList();

            if (ordered.Count == 0)
            {
                return null;
            }

            var now = ResolveNow();
            var startIndex = now != null
                ? ordered.FindIndex(entry => entry.QueueId == now.QueueId)
                : -1;

            for (var index = startIndex + 1; index < ordered.Count; index++)
            {
                var candidate = ordered[index];
                if (matureMode == ReorderMode.DeferMature && candidate.IsMature)
                {
                    continue;
                }

                return candidate;
            }

            if (now == null)
            {
                foreach (var candidate in ordered)
                {
                    if (matureMode == ReorderMode.DeferMature && candidate.IsMature)
                    {
                        continue;
                    }

                    return candidate;
                }
            }

            return null;
        }
    }
}
