namespace PaymentLedgerService.Models
{
    public enum PaymentIntentStatus
    {
        Pending,
        Completed,
        Failed
    }

    public class PaymentIntent
    {
        public int Id { get; set; }

        public int FromAccountId { get; set; }
        public int ToAccountId { get; set; }
        public long AmountMinorUnits { get; set; }
        public string Currency { get; set; } = "PKR";
        public string Description { get; set; } = string.Empty;

        public PaymentIntentStatus Status { get; set; } = PaymentIntentStatus.Pending;

        // Jab actual ledger entries ban jayengi (payment complete), ye link store karenge
        public Guid? TransactionId { get; set; }

        // External provider ka apna reference ID (agar mile)
        public string? ProviderReference { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }
    }
}