namespace PaymentLedgerService.Models
{
    public class WebhookEvent
    {
        public int Id { get; set; }

        // Provider se aane wala unique event ID — deduplication isi se hoti hai
        public string ProviderEventId { get; set; } = string.Empty;

        public string EventType { get; set; } = string.Empty;

        // Poora raw JSON payload store karte hain (audit/debugging ke liye)
        public string Payload { get; set; } = string.Empty;

        public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;

        // Provider ne event kab generate kiya (out-of-order detect karne ke liye use hoga)
        public DateTime ProviderTimestampUtc { get; set; }
    }
}