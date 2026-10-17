using Microsoft.EntityFrameworkCore;
using PaymentService.Data;
using PaymentService.Models;

namespace PaymentService.Services
{
    // Catches the "charged but never reflected in DB" case: if a webhook was lost, the server
    // crashed mid-processing, or the queue message went to the DLQ and nobody replayed it yet,
    // this job independently re-checks with Paystack rather than waiting forever on a webhook.
    public class ReconciliationJob : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReconciliationJob> _logger;
        private static readonly TimeSpan StuckThreshold = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

        public ReconciliationJob(IServiceScopeFactory scopeFactory, ILogger<ReconciliationJob> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReconcileOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reconciliation pass failed");
                }

                await Task.Delay(PollInterval, stoppingToken);
            }
        }

        private async Task ReconcileOnceAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
            var paystack = scope.ServiceProvider.GetRequiredService<IPaystackClient>();

            var cutoff = DateTime.UtcNow - StuckThreshold;
            var stuck = await db.Transactions
                .Where(t => t.Status == TransactionStatus.Pending && t.CreatedAtUtc < cutoff)
                .ToListAsync(ct);

            foreach (var txn in stuck)
            {
                try
                {
                    var result = await paystack.VerifyTransactionAsync(txn.Reference, ct);
                    txn.Status = result.Status ? TransactionStatus.Success : TransactionStatus.Failed;
                    txn.VerifiedAtUtc = DateTime.UtcNow;
                    _logger.LogInformation("Reconciled stuck transaction {Reference} -> {Status}", txn.Reference, txn.Status);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not reconcile {Reference} this pass", txn.Reference);
                }
            }

            if (stuck.Count > 0)
                await db.SaveChangesAsync(ct);
        }
    }
}
