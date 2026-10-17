using System.Security.Cryptography;
using System.Text;

namespace WebhookService.Services
{
    public interface IPaystackSignatureVerifier
    {
        bool IsValid(string rawBody, string? signatureHeader);
    }

    // Paystack signs every webhook body with HMAC-SHA512 using your secret key, and sends the
    // result in the "x-paystack-signature" header. If this doesn't match, the request did NOT
    // come from Paystack — anyone could otherwise POST a fake "charge.success" event.
    public class PaystackSignatureVerifier : IPaystackSignatureVerifier
    {
        private readonly string _secretKey;

        public PaystackSignatureVerifier(IConfiguration config)
        {
            _secretKey = config["Paystack:SecretKey"]!;
        }

        public bool IsValid(string rawBody, string? signatureHeader)
        {
            if (string.IsNullOrEmpty(signatureHeader))
                return false;

            using var hmac = new HMACSHA512(Encoding.UTF8.GetBytes(_secretKey));
            var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(rawBody));
            var computedSignature = Convert.ToHexString(computedHash).ToLowerInvariant();

            // Constant-time comparison to avoid timing attacks on the signature check.
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedSignature),
                Encoding.UTF8.GetBytes(signatureHeader.ToLowerInvariant()));
        }
    }
}
