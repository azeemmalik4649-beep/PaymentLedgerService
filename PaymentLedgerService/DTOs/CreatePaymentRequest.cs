namespace PaymentLedgerService.DTOs
{
    public class CreatePaymentRequest
    {
        public int FromAccountId { get; set; }
        public int ToAccountId { get; set; }

        // Minor units mein aayega client se (paisa/cents) — decimal nahi
        public long AmountMinorUnits { get; set; }

        public string Currency { get; set; } = "PKR";
        public string Description { get; set; } = string.Empty;
    }
}