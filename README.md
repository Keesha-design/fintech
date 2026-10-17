# Fintech Payments Demo — Microservices (.NET 8 + Angular)

A small but realistic payments platform demonstrating the patterns needed in a
fintech/banking backend: idempotency, webhook signature verification, async
event-driven settlement, resilience (retry + circuit breaker), atomic
concurrency control, and reconciliation.

## Architecture

```
Angular (4200) → API Gateway (5000, YARP) → {
    Auth Service (5001)       — JWT issuing, RBAC, Postgres
    Payment Service (5002)    — Paystack integration, idempotency, Polly, Postgres
    Webhook Service (5003)    — HMAC signature verification, publishes events
    Inventory Service (5004)  — Redis Lua atomic stock reservation
}
    Notification Service      — consumes payment events (no public endpoint)

Infra: RabbitMQ (events/queue), Redis (idempotency + stock + dedup), Postgres x2
```

### Event flow (async settlement)

```
1. Angular → Payment Service: POST /api/payments/initiate  (idempotency key attached)
2. Payment Service → Paystack: initialize transaction, store as "Pending"
3. User completes payment on Paystack's hosted page
4. Paystack → Webhook Service: POST /api/webhooks/paystack
5. Webhook Service: verify HMAC signature → dedupe in Redis → publish PaymentWebhookReceived
6. Payment Service (consumer): re-verify DIRECTLY with Paystack's /transaction/verify
   (never trusts the webhook payload's own status field) → updates DB → publishes
   PaymentVerified / PaymentFailed
7. Notification Service (consumer): logs/sends a receipt
8. ReconciliationJob (background, every 2 min): re-checks anything still "Pending"
   after 5 minutes, in case a webhook was lost entirely
```

## What each concept maps to in code

| Concept | Where |
|---|---|
| Idempotency keys | `PaymentService/Services/IdempotencyService.cs`, `PaymentController.Initiate` |
| Webhook signature verification | `WebhookService/Services/PaystackSignatureVerifier.cs` |
| Webhook replay dedup | `WebhookService/Controllers/WebhookController.cs` |
| "Don't trust the webhook, verify directly" | `PaymentService/Services/PaymentWebhookConsumer.cs` |
| Circuit breaker + retry (Polly) | `PaymentService/Program.cs` (`GetRetryPolicy`, `GetCircuitBreakerPolicy`) |
| Dead letter queue | MassTransit's `_error` queue, configured via `UseMessageRetry` in `PaymentService/Program.cs` |
| Reconciliation | `PaymentService/Services/ReconciliationJob.cs` |
| Atomic stock decrement (flash-sale/TOCTOU fix) | `InventoryService/Scripts/reserve_stock.lua`, `StockService.cs` |
| Reservation TTL / abandon handling | `InventoryService/Services/StockService.cs` |
| JWT + RBAC | `AuthService/Controllers/AuthController.cs`, `[Authorize(Roles = "Admin")]` usages |
| DB-level double-charge safety net | Unique indexes in `AuthDbContext`/`PaymentDbContext` |
| Event-driven microservices (pub/sub) | `Shared/Events`, MassTransit wiring in each `Program.cs` |
| API Gateway | `ApiGateway/appsettings.json` (YARP routes) |

## Running it locally

**Prerequisites:** Docker Desktop, a free Paystack test account.

1. Get your Paystack **test secret key** from the Paystack dashboard (Settings → API Keys & Webhooks).
2. Replace `sk_test_REPLACE_WITH_YOUR_PAYSTACK_TEST_SECRET_KEY` in:
   - `PaymentService/appsettings.json`
   - `WebhookService/appsettings.json`
3. Replace `CHANGE_ME_super_long_random_secret_key_min_32_chars` (the JWT secret) with the
   **same** random string in `AuthService`, `PaymentService`, and `InventoryService` appsettings.
4. From the repo root:
   ```bash
   docker compose up --build
   ```
5. In a separate terminal, run the Angular frontend:
   ```bash
   cd angular-frontend
   npm install
   npm start
   ```
   Visit `http://localhost:4200`.

6. **Webhook testing locally:** Paystack needs a public URL to send webhooks to. Use `ngrok`:
   ```bash
   ngrok http 5000
   ```
   Then set the ngrok URL + `/api/webhooks/paystack` as your webhook URL in the Paystack dashboard.

## Testing the concepts yourself

- **Double-charge attempt:** call `POST /api/payments/initiate` twice with the *same*
  `idempotencyKey` — second call returns the original transaction instead of charging again.
- **Circuit breaker:** temporarily point `Paystack:BaseUrl` at an invalid host, hit the
  initiate endpoint 5+ times in a row, and watch subsequent calls fail instantly (check logs)
  instead of hanging on a timeout.
- **Flash sale / no overselling:** seed 10 units via `POST /api/inventory/seed` (Admin role),
  then load-test `POST /api/inventory/reserve` with a tool like `k6` or Apache Bench —
  confirm exactly 10 succeed regardless of concurrency.
- **Webhook signature rejection:** POST to `/api/webhooks/paystack` with a tampered body or
  missing `x-paystack-signature` header — confirm you get a 401.
- **Dead letter queue:** temporarily break the DB connection string for Payment Service,
  trigger a webhook event, and watch it land in RabbitMQ's `payment-webhook-received_error`
  queue (visible at `http://localhost:15672`) after retries are exhausted.

## Deliberately left as "v2" (mention in interviews, don't stress about building)

- Full double-entry ledger (separate `LedgerEntries` table with matching debit/credit rows)
- KYC/AML checks
- Maker-checker approval flow for high-value transactions
- MFA / OTP on login
- Encryption at rest for sensitive fields
- Multi-region failover / high availability

## Security notes / demo simplifications

- The JWT is stored in `localStorage` on the frontend for simplicity. A production banking
  app should prefer an httpOnly, Secure, SameSite cookie issued by the backend to reduce XSS risk.
- Each service uses a shared JWT secret for simplicity; a fuller setup would use asymmetric
  signing (RS256) so only Auth Service holds the private key and other services only need
  the public key to validate tokens.
- CORS is wide open to `localhost:4200` for local dev — lock this down before any real deployment.
