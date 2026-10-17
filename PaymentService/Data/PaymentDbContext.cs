using Microsoft.EntityFrameworkCore;
using PaymentService.Models;

namespace PaymentService.Data
{
    public class PaymentDbContext : DbContext
    {
        public PaymentDbContext(DbContextOptions<PaymentDbContext> options) : base(options) { }

        public DbSet<Transaction> Transactions => Set<Transaction>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Transaction>()
                .HasIndex(t => t.Reference)
                .IsUnique(); // last line of defense against duplicate charges

            modelBuilder.Entity<Transaction>()
                .HasIndex(t => t.IdempotencyKey)
                .IsUnique();
        }
    }
}
