using StackExchange.Redis;

namespace PaymentService.Services
{
    public interface IIdempotencyService
    {
        // Returns true if this is the FIRST time we've seen this key (safe to proceed).
        // Returns false if it's a repeat (caller should short-circuit and return the cached result).
        Task<bool> TryClaimAsync(string idempotencyKey, TimeSpan ttl);
    }

    public class RedisIdempotencyService : IIdempotencyService
    {
        private readonly IConnectionMultiplexer _redis;

        public RedisIdempotencyService(IConnectionMultiplexer redis) => _redis = redis;

        public async Task<bool> TryClaimAsync(string idempotencyKey, TimeSpan ttl)
        {
            var db = _redis.GetDatabase();
            // SET key value NX EX ttl — atomic "claim if not already claimed".
            // This is the same pattern as the Redis Lua stock-decrement, just simpler:
            // one atomic command instead of a script, because there's no arithmetic involved.
            var claimed = await db.StringSetAsync(
                $"idem:{idempotencyKey}",
                "claimed",
                ttl,
                When.NotExists);

            return claimed;
        }
    }
}
