using MassTransit;
using Shared.Events;

namespace NotificationService.Consumers
{
    // In a real system this would send email/SMS/push (SendGrid, Twilio, etc.).
    // Stubbed to logging here so the event-driven chain is demonstrable end-to-end
    // without needing real provider credentials.
    public class PaymentVerifiedConsumer : IConsumer<PaymentVerified>
    {
        private readonly ILogger<PaymentVerifiedConsumer> _logger;
        public PaymentVerifiedConsumer(ILogger<PaymentVerifiedConsumer> logger) => _logger = logger;

        public Task Consume(ConsumeContext<PaymentVerified> context)
        {
            var msg = context.Message;
            _logger.LogInformation(
                "Notification: payment {Reference} confirmed for user {UserId}, amount {AmountKobo} {Currency}",
                msg.Reference, msg.UserId, msg.AmountKobo, msg.Currency);

            // TODO (v2): send real email/SMS receipt here.
            return Task.CompletedTask;
        }
    }

    public class PaymentFailedConsumer : IConsumer<PaymentFailed>
    {
        private readonly ILogger<PaymentFailedConsumer> _logger;
        public PaymentFailedConsumer(ILogger<PaymentFailedConsumer> logger) => _logger = logger;

        public Task Consume(ConsumeContext<PaymentFailed> context)
        {
            var msg = context.Message;
            _logger.LogWarning("Notification: payment {Reference} failed — {Reason}", msg.Reference, msg.Reason);
            return Task.CompletedTask;
        }
    }
}
