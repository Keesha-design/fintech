using StackExchange.Redis;

namespace InventoryService.Services
{
    public interface IStockService
    {
        Task<int> SeedStockAsync(string productId, int quantity);
        Task<(bool reserved, string? reservationId)> TryReserveAsync(string productId, Guid userId, TimeSpan reservationTtl);
        Task ReleaseAsync(string productId, string reservationId);
    }

    public class RedisStockService : IStockService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RedisStockService> _logger;
        private readonly LuaScript _reserveScript;

        public RedisStockService(IConnectionMultiplexer redis, ILogger<RedisStockService> logger)
        {
            _redis = redis;
            _logger = logger;

            var scriptText = File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Scripts", "reserve_stock.lua"));
            _reserveScript = LuaScript.Prepare(scriptText);
        }

        public async Task<int> SeedStockAsync(string productId, int quantity)
        {
            var db = _redis.GetDatabase();
            await db.StringSetAsync($"stock:{productId}", quantity);
            return quantity;
        }

        public async Task<(bool reserved, string? reservationId)> TryReserveAsync(
            string productId, Guid userId, TimeSpan reservationTtl)
        {
            var db = _redis.GetDatabase();
            var stockKey = $"stock:{productId}";

            // Atomic check-and-decrement — the fix for the "10 gadgets, 1,000,000 buyers" problem.
            var result = (int)await db.ScriptEvaluateAsync(_reserveScript, new { KEYS = new RedisKey[] { stockKey } });

            if (result != 1)
            {
                _logger.LogInformation("Reservation failed for {ProductId} by {UserId} (sold out)", productId, userId);
                return (false, null);
            }

            // Reservation with TTL: if the user abandons checkout, this key expires and the slot
            // should be released back to stock by a separate cleanup pass (see ReleaseAsync / a
            // scheduled sweep in a fuller implementation).
            var reservationId = Guid.NewGuid().ToString("N");
            await db.StringSetAsync($"reservation:{reservationId}", $"{productId}:{userId}", reservationTtl);

            return (true, reservationId);
        }

        // Called when checkout is abandoned or payment fails — gives the slot back to the pool
        // instead of leaving it permanently lost.
        public async Task ReleaseAsync(string productId, string reservationId)
        {
            var db = _redis.GetDatabase();
            var removed = await db.KeyDeleteAsync($"reservation:{reservationId}");
            if (removed)
            {
                await db.StringIncrementAsync($"stock:{productId}");
                _logger.LogInformation("Released reservation {ReservationId} back to {ProductId} stock", reservationId, productId);
            }
        }
    }
}
