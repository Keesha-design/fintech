using MassTransit;
using Microsoft.EntityFrameworkCore;
using PaymentService.Data;
using PaymentService.Models;
using Shared.Events;

namespace PaymentService.Services
{
    // This is where "don't trust the webhook payload" is enforced in code: receiving the event
    // only tells us WHICH reference to check, not what its status actually is.
    public class PaymentWebhookConsumer : IConsumer<PaymentWebhookReceived>
    {
        private readonly PaymentDbContext _db;
        private readonly IPaystackClient _paystack;
        private readonly IPublishEndpoint _publisher;
        private readonly ILogger<PaymentWebhookConsumer> _logger;

        public PaymentWebhookConsumer(
            PaymentDbContext db,
            IPaystackClient paystack,
            IPublishEndpoint publisher,
            ILogger<PaymentWebhookConsumer> logger)
        {
            _db = db;
            _paystack = paystack;
            _publisher = publisher;
            _logger = logger;
        }

        public async Task Consume(ConsumeContext<PaymentWebhookReceived> context)
        {
            var msg = context.Message;

            var txn = await _db.Transactions.SingleOrDefaultAsync(t => t.Reference == msg.Reference);
            if (txn is null)
            {
                _logger.LogWarning("Webhook for unknown reference {Reference} — ignoring", msg.Reference);
                return;
            }

            // Idempotent no-op: if we already verified this transaction, don't re-process
            // (Paystack can and does send the same webhook more than once).
            if (txn.Status != TransactionStatus.Pending)
            {
                _logger.LogInformation("Reference {Reference} already {Status}, skipping", msg.Reference, txn.Status);
                return;
            }

            txn.VerificationAttempts++;

            // This call is wrapped in Polly (retry + circuit breaker) at the HttpClient level.
            // If Paystack is down, this throws, MassTransit's own retry middleware retries the
            // whole consumer, and after N attempts the message lands in the error queue (DLQ).
            var verifyResult = await _paystack.VerifyTransactionAsync(msg.Reference);

            txn.Status = verifyResult.Status ? TransactionStatus.Success : TransactionStatus.Failed;
            txn.VerifiedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            if (verifyResult.Status)
            {
                await _publisher.Publish(new PaymentVerified
                {
                    Reference = txn.Reference,
                    AmountKobo = txn.AmountKobo,
                    Currency = txn.Currency,
                    UserId = txn.UserId,
                    Success = true
                });
            }
            else
            {
                await _publisher.Publish(new PaymentFailed
                {
                    Reference = txn.Reference,
                    Reason = verifyResult.GatewayResponse
                });
            }
        }
    }
}
