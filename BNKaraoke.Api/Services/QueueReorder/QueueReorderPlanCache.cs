using System;
using BNKaraoke.Api.Data.QueueReorder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace BNKaraoke.Api.Services.QueueReorder
{
    public interface IQueueReorderPlanCache
    {
        QueueReorderPlan? Get(Guid planId);

        void Set(QueueReorderPlan plan, TimeSpan ttl);

        void Remove(Guid planId);
    }

    public class QueueReorderPlanCache : IQueueReorderPlanCache
    {
        private readonly IMemoryCache _memoryCache;
        private readonly ILogger<QueueReorderPlanCache> _logger;

        public QueueReorderPlanCache(IMemoryCache memoryCache, ILogger<QueueReorderPlanCache> logger)
        {
            _memoryCache = memoryCache;
            _logger = logger;
        }

        public QueueReorderPlan? Get(Guid planId)
        {
            if (_memoryCache.TryGetValue(planId, out QueueReorderPlan? plan) && plan != null)
            {
                return plan;
            }

            return null;
        }

        public void Set(QueueReorderPlan plan, TimeSpan ttl)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (ttl <= TimeSpan.Zero)
            {
                ttl = TimeSpan.FromMinutes(5);
            }

            _logger.LogInformation("Caching queue reorder plan {PlanId} for {Duration}.", plan.PlanId, ttl);
            _memoryCache.Set(plan.PlanId, plan, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl,
            });
        }

        public void Remove(Guid planId)
        {
            _logger.LogDebug("Evicting queue reorder plan {PlanId} from cache.", planId);
            _memoryCache.Remove(planId);
        }
    }
}
