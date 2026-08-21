namespace PaymentLedgerService.DTOs
{
    public class PaymentResponse
    {
        public Guid TransactionId { get; set; }
        public int FromAccountId { get; set; }
        public int ToAccountId { get; set; }
        public long AmountMinorUnits { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
    }
}