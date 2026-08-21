namespace PaymentLedgerService.Models
{
    public class IdempotencyKey
    {
        public int Id { get; set; }

        // Client se aane wala unique key (header se)
        public string Key { get; set; } = string.Empty;

        // Response ko JSON string ke taur pe store karenge, taake dobara request aane par
        // exact same response wapis bhej sakein
        public string ResponseBody { get; set; } = string.Empty;

        public int ResponseStatusCode { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}