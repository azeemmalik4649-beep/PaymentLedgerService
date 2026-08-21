using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentLedgerService.Data;
using PaymentLedgerService.Models;
using System.Security.Cryptography;
using System.Text;

namespace PaymentLedgerService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhookController : ControllerBase
    {
        private readonly LedgerDbContext _db;
        private readonly IConfiguration _config;

        public WebhookController(LedgerDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public class WebhookPayload
        {
            public string EventId { get; set; } = string.Empty;
            public string EventType { get; set; } = string.Empty;
            public DateTime TimestampUtc { get; set; }
            public string Data { get; set; } = string.Empty;
        }

        [HttpPost("payment-events")]
        public async Task<IActionResult> ReceiveWebhook(
            [FromBody] WebhookPayload payload,
            [FromHeader(Name = "X-Signature")] string? signature)
        {
            if (string.IsNullOrWhiteSpace(signature))
                return BadRequest("Missing signature.");

            // Raw body dobara banao (jaisa provider ne sign kiya tha), signature verify karne ke liye
            var rawPayload = $"{payload.EventId}|{payload.EventType}|{payload.TimestampUtc:o}|{payload.Data}";

            var secret = _config["WebhookSettings:SharedSecret"] ?? "";
            var expectedSignature = ComputeHmac(rawPayload, secret);

            if (!SignaturesMatch(signature, expectedSignature))
                return Unauthorized("Invalid signature.");

            // Deduplication check
            var alreadyProcessed = await _db.WebhookEvents
                .AnyAsync(e => e.ProviderEventId == payload.EventId);

            if (alreadyProcessed)
            {
                // Ye event pehle process ho chuka hai — silently 200 return karo
                // (provider ko retry rokne ke liye success dikhana zaroori hai)
                return Ok(new { message = "Event already processed (duplicate ignored)." });
            }

            // Event save karo
            var webhookEvent = new WebhookEvent
            {
                ProviderEventId = payload.EventId,
                EventType = payload.EventType,
                Payload = payload.Data,
                ProviderTimestampUtc = payload.TimestampUtc,
                ReceivedAtUtc = DateTime.UtcNow
            };

            _db.WebhookEvents.Add(webhookEvent);
            await _db.SaveChangesAsync();

            // NOTE: Out-of-order handling — hum event ko sirf STORE kar rahe hain uske
            // ProviderTimestampUtc ke sath. Jab bhi hum "current state" nikalein (Step 7
            // reconciliation mein), hum ProviderTimestampUtc se sort karke dekhenge,
            // na ke jis order mein events aaye us se. Isi liye timestamp save karna zaroori tha.

            return Ok(new { message = "Webhook processed successfully." });
        }

        private static string ComputeHmac(string message, string secret)
        {
            var keyBytes = Encoding.UTF8.GetBytes(secret);
            var messageBytes = Encoding.UTF8.GetBytes(message);

            using var hmac = new HMACSHA256(keyBytes);
            var hashBytes = hmac.ComputeHash(messageBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static bool SignaturesMatch(string provided, string expected)
        {
            // Constant-time comparison — timing attacks se bachne ke liye
            // (normal == use karne se hacker response-time measure karke signature guess kar sakta hai)
            var providedBytes = Encoding.UTF8.GetBytes(provided);
            var expectedBytes = Encoding.UTF8.GetBytes(expected);

            if (providedBytes.Length != expectedBytes.Length)
                return false;

            return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
        }
    }
}