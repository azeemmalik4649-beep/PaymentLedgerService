namespace PaymentLedgerService.Models
{
    public class LedgerEntry
    {
        public long Id { get; set; }

        // Ye group karta hai ke konsi entries ek hi payment/transaction ka hissa hain
        // Ek transaction = kam se kam 2 entries (1 Debit + 1 Credit), same TransactionId ke sath
        public Guid TransactionId { get; set; }

        public int AccountId { get; set; }
        public Account? Account { get; set; }

        public EntryType Type { get; set; }

        // MINOR UNIT storage — paisa/cents mein, decimal nahi!
        // 150000 = 1500.00 rupay (agar currency ke 2 decimal places hain)
        public long AmountMinorUnits { get; set; }

        public string Currency { get; set; } = "PKR";

        // Kis wajah se ye entry bani (reference/description)
        public string Description { get; set; } = string.Empty;

        // Immutability enforce karne ke liye — is entry ko kabhi update nahi karna,
        // agar galti ho to naya "reversing entry" banate hain, purani ko edit nahi karte
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}