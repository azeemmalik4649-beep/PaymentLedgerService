namespace PaymentLedgerService.Models
{
    public class Account
    {
        public int Id { get; set; }

        // Human-readable naam, jaise "Customer Wallet - Ali" ya "Merchant Revenue"
        public string Name { get; set; } = string.Empty;

        // Konsi type ka account hai (Wallet, Revenue, Fees, wagera) — reporting ke liye useful
        public string AccountType { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // Navigation property — is account ki saari ledger entries (balance calculate karne ke liye)
        public ICollection<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
    }
}