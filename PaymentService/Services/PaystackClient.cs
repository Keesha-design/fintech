using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PaymentService.Services
{
    // This class does NOT implement retry/circuit-breaker logic itself — that lives entirely
    // in the Polly policies attached to the named HttpClient in Program.cs. Keeping resilience
    // at the HttpClient level (not scattered in business code) is the idiomatic .NET approach.
    public class PaystackClient : IPaystackClient
    {
        private readonly HttpClient _http;

        public PaystackClient(HttpClient http, IConfiguration config)
        {
            _http = http;
            _http.BaseAddress = new Uri(config["Paystack:BaseUrl"] ?? "https://api.paystack.co");
            _http.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", config["Paystack:SecretKey"]);
        }

        public async Task<PaystackInitResult> InitializeTransactionAsync(
            string email, long amountKobo, string reference, CancellationToken ct = default)
        {
            var payload = new { email, amount = amountKobo, reference };
            var response = await _http.PostAsJsonAsync("/transaction/initialize", payload, ct);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var data = doc.RootElement.GetProperty("data");

            return new PaystackInitResult(
                Status: doc.RootElement.GetProperty("status").GetBoolean(),
                AuthorizationUrl: data.GetProperty("authorization_url").GetString()!,
                AccessCode: data.GetProperty("access_code").GetString()!,
                Reference: data.GetProperty("reference").GetString()!);
        }

        // Never trust a webhook payload alone — always independently confirm with Paystack.
        public async Task<PaystackVerifyResult> VerifyTransactionAsync(string reference, CancellationToken ct = default)
        {
            var response = await _http.GetAsync($"/transaction/verify/{Uri.EscapeDataString(reference)}", ct);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var data = doc.RootElement.GetProperty("data");
            var status = data.GetProperty("status").GetString(); // "success" | "failed" | "abandoned"

            return new PaystackVerifyResult(
                Status: status == "success",
                GatewayResponse: data.TryGetProperty("gateway_response", out var gr) ? gr.GetString()! : status!,
                Reference: data.GetProperty("reference").GetString()!,
                AmountKobo: data.GetProperty("amount").GetInt64(),
                Currency: data.GetProperty("currency").GetString()!);
        }
    }
}
