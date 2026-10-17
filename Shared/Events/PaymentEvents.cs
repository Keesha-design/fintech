namespace Shared.Events
{
    // Published by WebhookService the moment a Paystack webhook is verified (signature valid).
    // This is a "trigger to verify" event — NOT trusted as the source of truth for payment status.
    public record PaymentWebhookReceived
    {
        public string Reference { get; init; } = default!;
        public string EventType { get; init; } = default!; // e.g. "charge.success"
        public DateTime ReceivedAtUtc { get; init; } = DateTime.UtcNow;
    }

    // Published by PaymentService AFTER it has independently verified the transaction
    // with Paystack's /transaction/verify endpoint. This IS trusted.
    public record PaymentVerified
    {
        public string Reference { get; init; } = default!;
        public decimal AmountKobo { get; init; }
        public string Currency { get; init; } = "NGN";
        public Guid UserId { get; init; }
        public bool Success { get; init; }
    }

    // Published by InventoryService once stock has been atomically reserved for a user.
    public record StockReserved
    {
        public string ProductId { get; init; } = default!;
        public Guid UserId { get; init; }
        public string ReservationId { get; init; } = default!;
        public DateTime ExpiresAtUtc { get; init; }
    }

    public record PaymentFailed
    {
        public string Reference { get; init; } = default!;
        public string Reason { get; init; } = default!;
    }
}
