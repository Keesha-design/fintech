-- KEYS[1] = stock key, e.g. "stock:gadget-1"
-- ARGV[1] = reservation TTL in seconds
-- Returns 1 if a unit was reserved, 0 if sold out.
--
-- This runs as a single atomic operation because Redis executes Lua scripts
-- single-threaded/atomically: no two callers can both read stock > 0 and both
-- decrement past zero, which is exactly the race condition that causes overselling.

local stock = tonumber(redis.call('GET', KEYS[1]))

if stock == nil then
  return -1 -- key doesn't exist / product not configured
end

if stock > 0 then
  redis.call('DECR', KEYS[1])
  return 1
else
  return 0
end
