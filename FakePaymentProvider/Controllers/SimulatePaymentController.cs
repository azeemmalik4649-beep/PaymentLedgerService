using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FakePaymentProvider.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SimulatePaymentController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;

        public SimulatePaymentController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
        }

        public class SimulatePaymentRequest
        {
            public string EventType { get; set; } = "payment.completed";
            public string Data { get; set; } = string.Empty;

            // Testing ke liye: agar true ho, same EventId dobara bhejega (duplicate test ke liye)
            public string? ReuseEventId { get; set; }
        }

        [HttpPost]
        public async Task<IActionResult> SimulatePayment(SimulatePaymentRequest request)
        {
            var eventId = request.ReuseEventId ?? Guid.NewGuid().ToString();
            var timestampUtc = DateTime.UtcNow;

            // Same format jo PaymentLedgerService expect karta hai signature ke liye
            var rawPayload = $"{eventId}|{request.EventType}|{timestampUtc:o}|{request.Data}";

            var secret = _config["WebhookSettings:SharedSecret"] ?? "";
            var signature = ComputeHmac(rawPayload, secret);

            var webhookBody = new
            {
                eventId,
                eventType = request.EventType,
                timestampUtc,
                data = request.Data
            };

            var targetUrl = _config["WebhookSettings:TargetWebhookUrl"] ?? "";

            var client = _httpClientFactory.CreateClient("default");
            var content = new StringContent(
                JsonSerializer.Serialize(webhookBody),
                Encoding.UTF8,
                "application/json");

            var httpRequest = new HttpRequestMessage(HttpMethod.Post, targetUrl)
            {
                Content = content
            };
            httpRequest.Headers.Add("X-Signature", signature);

            var response = await client.SendAsync(httpRequest);
            var responseBody = await response.Content.ReadAsStringAsync();

            return Ok(new
            {
                SentEventId = eventId,
                WebhookStatusCode = (int)response.StatusCode,
                WebhookResponse = responseBody
            });
        }

        private static string ComputeHmac(string message, string secret)
        {
            var keyBytes = Encoding.UTF8.GetBytes(secret);
            var messageBytes = Encoding.UTF8.GetBytes(message);

            using var hmac = new HMACSHA256(keyBytes);
            var hashBytes = hmac.ComputeHash(messageBytes);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }
    }
}