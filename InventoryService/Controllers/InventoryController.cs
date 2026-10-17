using InventoryService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace InventoryService.Controllers
{
    public record ReserveRequest(string ProductId);
    public record SeedRequest(string ProductId, int Quantity);

    [ApiController]
    [Route("api/inventory")]
    public class InventoryController : ControllerBase
    {
        private readonly IStockService _stock;

        public InventoryController(IStockService stock) => _stock = stock;

        // Admin-only: set up stock for a flash sale, e.g. 10 units of "gadget-1".
        [Authorize(Roles = "Admin")]
        [HttpPost("seed")]
        public async Task<IActionResult> Seed(SeedRequest req)
        {
            var qty = await _stock.SeedStockAsync(req.ProductId, req.Quantity);
            return Ok(new { req.ProductId, quantity = qty });
        }

        [Authorize]
        [HttpPost("reserve")]
        public async Task<IActionResult> Reserve(ReserveRequest req)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub")!);

            var (reserved, reservationId) = await _stock.TryReserveAsync(
                req.ProductId, userId, TimeSpan.FromMinutes(10));

            if (!reserved)
                return Conflict(new { message = "Sold out" });

            return Ok(new { reservationId, message = "Reserved — complete payment within 10 minutes" });
        }

        [Authorize]
        [HttpPost("release/{reservationId}")]
        public async Task<IActionResult> Release(string reservationId, [FromQuery] string productId)
        {
            await _stock.ReleaseAsync(productId, reservationId);
            return Ok();
        }
    }
}
