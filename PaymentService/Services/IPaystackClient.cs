namespace PaymentService.Services
{
    public record PaystackInitResult(bool Status, string AuthorizationUrl, string AccessCode, string Reference);
    public record PaystackVerifyResult(bool Status, string GatewayResponse, string Reference, long AmountKobo, string Currency);

    public interface IPaystackClient
    {
        Task<PaystackInitResult> InitializeTransactionAsync(string email, long amountKobo, string reference, CancellationToken ct = default);
        Task<PaystackVerifyResult> VerifyTransactionAsync(string reference, CancellationToken ct = default);
    }
}
