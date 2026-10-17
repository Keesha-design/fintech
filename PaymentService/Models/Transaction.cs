namespace PaymentService.Models
{
    public enum TransactionStatus { Pending, Success, Failed }

    // Simplified double-entry style record: every transaction is a debit against the payer
    // and (conceptually) a credit to the merchant/platform. A full ledger would split this
    // into two rows (DebitEntry/CreditEntry) in a separate LedgerEntries table so money is
    // never created or destroyed by a bug — flagged here as the "v2" addition.
    public class Transaction
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        // Paystack's unique reference for this attempt. Reused on retry — never regenerated —
        // so Paystack itself treats a retried request as the same transaction, not a new charge.
        public string Reference { get; set; } = default!;

        // Client-supplied idempotency key for this specific "initiate payment" attempt.
        // Distinct from Reference: this guards the API call itself against double-submission
        // (e.g. a double-click) before a Paystack reference even exists.
        public string IdempotencyKey { get; set; } = default!;

        public decimal AmountKobo { get; set; }
        public string Currency { get; set; } = "NGN";
        public TransactionStatus Status { get; set; } = TransactionStatus.Pending;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? VerifiedAtUtc { get; set; }
        public int VerificationAttempts { get; set; } = 0;
    }
}
