using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Shared.Events;
using StackExchange.Redis;
using System.Text.Json;
using WebhookService.Services;

namespace WebhookService.Controllers
{
    [ApiController]
    [Route("api/webhooks")]
    public class WebhookController : ControllerBase
    {
        private readonly IPaystackSignatureVerifier _verifier;
        private readonly IPublishEndpoint _publisher;
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<WebhookController> _logger;

        public WebhookController(
            IPaystackSignatureVerifier verifier,
            IPublishEndpoint publisher,
            IConnectionMultiplexer redis,
            ILogger<WebhookController> logger)
        {
            _verifier = verifier;
            _publisher = publisher;
            _redis = redis;
            _logger = logger;
        }

        [HttpPost("paystack")]
        public async Task<IActionResult> Paystack()
        {
            Request.EnableBuffering();
            using var reader = new StreamReader(Request.Body, leaveOpen: true);
            var rawBody = await reader.ReadToEndAsync();
            Request.Body.Position = 0;

            var signature = Request.Headers["x-paystack-signature"].FirstOrDefault();

            // 1. Verify signature FIRST, before touching the payload contents at all.
            if (!_verifier.IsValid(rawBody, signature))
            {
                _logger.LogWarning("Rejected webhook with invalid signature");
                return Unauthorized();
            }

            using var doc = JsonDocument.Parse(rawBody);
            var eventType = doc.RootElement.GetProperty("event").GetString()!;
            var reference = doc.RootElement.GetProperty("data").GetProperty("reference").GetString()!;

            // 2. Deduplicate: Paystack retries webhooks that don't get a fast 200 OK, so the
            // same event can arrive more than once. Use a short-lived Redis flag per event+reference
            // so a replay is acknowledged but not re-published.
            var db = _redis.GetDatabase();
            var dedupeKey = $"webhook:{eventType}:{reference}";
            var isNew = await db.StringSetAsync(dedupeKey, "1", TimeSpan.FromHours(24), When.NotExists);

            if (isNew)
            {
                // 3. Publish and let PaymentService independently verify — this endpoint does
                // NOT trust the payload's own "status": it only tells us WHAT to go check.
                await _publisher.Publish(new PaymentWebhookReceived
                {
                    Reference = reference,
                    EventType = eventType
                });
            }
            else
            {
                _logger.LogInformation("Duplicate webhook for {Reference}, acking without republishing", reference);
            }

            // 4. Always ack fast with 200 — Paystack will keep retrying (and eventually give up
            // or flag the endpoint) if it doesn't get a prompt 200 response.
            return Ok();
        }
    }
}
