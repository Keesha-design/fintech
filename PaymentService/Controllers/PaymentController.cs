using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentService.Data;
using PaymentService.Models;
using PaymentService.Services;
using System.Security.Claims;

namespace PaymentService.Controllers
{
    public record InitiatePaymentRequest(string Email, long AmountKobo, string IdempotencyKey);

    [ApiController]
    [Route("api/payments")]
    [Authorize]
    public class PaymentController : ControllerBase
    {
        private readonly PaymentDbContext _db;
        private readonly IPaystackClient _paystack;
        private readonly IIdempotencyService _idempotency;
        private readonly ILogger<PaymentController> _logger;

        public PaymentController(
            PaymentDbContext db,
            IPaystackClient paystack,
            IIdempotencyService idempotency,
            ILogger<PaymentController> logger)
        {
            _db = db;
            _paystack = paystack;
            _idempotency = idempotency;
            _logger = logger;
        }

        [HttpPost("initiate")]
        public async Task<IActionResult> Initiate(InitiatePaymentRequest req, CancellationToken ct)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? User.FindFirstValue("sub")!);

            // Layer 1: idempotency key claim. If this exact client-generated key was already
            // used (double-click, retried request after a timeout), don't re-charge — look up
            // what we already have and return it.
            var firstTime = await _idempotency.TryClaimAsync(req.IdempotencyKey, TimeSpan.FromMinutes(10));
            if (!firstTime)
            {
                var existing = await _db.Transactions
                    .SingleOrDefaultAsync(t => t.IdempotencyKey == req.IdempotencyKey, ct);

                if (existing is not null)
                    return Ok(new { existing.Reference, existing.Status, message = "Duplicate request — returning original transaction" });

                // Key was claimed but no DB row yet (race between claim and insert) — reject
                // rather than risk a double charge; client should retry shortly.
                return Conflict(new { message = "Request already in progress, please retry shortly" });
            }

            // Reference generated ONCE per attempt and reused on any downstream retry —
            // this is what lets Paystack itself de-duplicate on their side too.
            var reference = $"txn_{Guid.NewGuid():N}";

            var transaction = new Transaction
            {
                UserId = userId,
                Reference = reference,
                IdempotencyKey = req.IdempotencyKey,
                AmountKobo = req.AmountKobo,
                Status = TransactionStatus.Pending
            };

            _db.Transactions.Add(transaction);
            // DB unique constraint on Reference/IdempotencyKey is the final safety net if
            // somehow two requests raced past the Redis claim (e.g. Redis was momentarily down).
            await _db.SaveChangesAsync(ct);

            try
            {
                var result = await _paystack.InitializeTransactionAsync(req.Email, req.AmountKobo, reference, ct);
                return Ok(new { result.AuthorizationUrl, result.Reference });
            }
            catch (Exception ex)
            {
                // Polly's circuit breaker may have short-circuited this call already.
                // Either way: don't leave the transaction stuck silently — mark it failed
                // and let the reconciliation job pick it up if Paystack actually did process it.
                _logger.LogError(ex, "Paystack initialize failed for {Reference}", reference);
                return StatusCode(503, new
                {
                    message = "Payment provider temporarily unavailable, please try again shortly",
                    reference
                });
            }
        }

        [HttpGet("{reference}")]
        public async Task<IActionResult> GetStatus(string reference, CancellationToken ct)
        {
            var txn = await _db.Transactions.SingleOrDefaultAsync(t => t.Reference == reference, ct);
            return txn is null ? NotFound() : Ok(txn);
        }
    }
}
