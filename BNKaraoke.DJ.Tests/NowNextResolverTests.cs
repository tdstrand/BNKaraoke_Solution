using System.Collections.Generic;
using BNKaraoke.DJ.Models;
using BNKaraoke.DJ.Services.Playback;
using Xunit;

namespace BNKaraoke.DJ.Tests
{
    public class NowNextResolverTests
    {
        [Fact]
        public void ResolveNowReturnsPlayheadMatchFromQueue()
        {
            var queue = new List<QueueEntry>
            {
                CreateEntry(1, position: 1),
                CreateEntry(2, position: 2)
            };

            var playhead = new QueueEntry { QueueId = 2 };
            var resolver = new NowNextResolver(queue, playhead);

            var result = resolver.ResolveNow();

            Assert.NotNull(result);
            Assert.Equal(2, result!.QueueId);
        }

        [Fact]
        public void ResolveUpNextSkipsMatureInDeferMode()
        {
            var queue = new List<QueueEntry>
            {
                CreateEntry(1, position: 1),
                CreateEntry(2, position: 2, isMature: true),
                CreateEntry(3, position: 3)
            };

            var resolver = new NowNextResolver(queue, queue[0]);

            var next = resolver.ResolveUpNext(ReorderMode.DeferMature);

            Assert.NotNull(next);
            Assert.Equal(3, next!.QueueId);
        }

        [Fact]
        public void ResolveUpNextReturnsFirstPlayableWhenNowMissing()
        {
            var queue = new List<QueueEntry>
            {
                CreateEntry(10, position: 10, isActive: false),
                CreateEntry(11, position: 11, isOnHold: true),
                CreateEntry(12, position: 12)
            };

            var resolver = new NowNextResolver(queue, null);

            var next = resolver.ResolveUpNext(ReorderMode.AllowMature);

            Assert.NotNull(next);
            Assert.Equal(12, next!.QueueId);
        }

        [Fact]
        public void ResolveUpNextRespectsOrderingByPosition()
        {
            var queue = new List<QueueEntry>
            {
                CreateEntry(5, position: 20),
                CreateEntry(6, position: 5),
                CreateEntry(7, position: 15)
            };

            var resolver = new NowNextResolver(queue, queue[1]);

            var next = resolver.ResolveUpNext(ReorderMode.AllowMature);

            Assert.NotNull(next);
            Assert.Equal(7, next!.QueueId);
        }

        private static QueueEntry CreateEntry(int queueId, int position, bool isMature = false, bool isActive = true, bool isOnHold = false)
        {
            return new QueueEntry
            {
                QueueId = queueId,
                Position = position,
                IsMature = isMature,
                IsActive = isActive,
                IsOnHold = isOnHold
            };
        }
    }
}
