using Microsoft.EntityFrameworkCore;
using PaymentLedgerService.Models;

namespace PaymentLedgerService.Data
{
    public class LedgerDbContext : DbContext
    {
        public LedgerDbContext(DbContextOptions<LedgerDbContext> options) : base(options) { }

        public DbSet<Account> Accounts => Set<Account>();
        public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
        public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
        public DbSet<WebhookEvent> WebhookEvents => Set<WebhookEvent>();
        public DbSet<PaymentIntent> PaymentIntents => Set<PaymentIntent>();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<LedgerEntry>(entity =>
            {
                // Fast balance lookups ke liye — hum baar baar "sab entries for AccountId X" query karenge
                entity.HasIndex(e => e.AccountId);

                // Reconciliation aur debugging ke liye — "sab entries for TransactionId Y" fast honi chahiye
                entity.HasIndex(e => e.TransactionId);

                entity.HasOne(e => e.Account)
                      .WithMany(a => a.LedgerEntries)
                      .HasForeignKey(e => e.AccountId)
                      .OnDelete(DeleteBehavior.Restrict); // Account delete ho to entries delete NAHI honi chahiye
            });
            modelBuilder.Entity<IdempotencyKey>(entity =>
            {
                // UNIQUE constraint — yehi asal mein duplicate ko rokta hai database level pe
                entity.HasIndex(e => e.Key).IsUnique();
            });
            modelBuilder.Entity<WebhookEvent>(entity =>
            {
                // Yehi duplicate events ko rokta hai — same ProviderEventId dobara insert nahi ho sakta
                entity.HasIndex(e => e.ProviderEventId).IsUnique();
            });
            modelBuilder.Entity<PaymentIntent>(entity =>
            {
                entity.HasIndex(e => e.Status);
            });
        }
    }
}