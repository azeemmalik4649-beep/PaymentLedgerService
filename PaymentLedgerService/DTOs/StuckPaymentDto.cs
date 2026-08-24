namespace PaymentLedgerService.DTOs
{
    public class StuckPaymentDto
    {
        public int IntentId { get; set; }
        public int FromAccountId { get; set; }
        public int ToAccountId { get; set; }
        public long AmountMinorUnits { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
        public double MinutesPending { get; set; }
    }
}