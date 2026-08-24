using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PaymentLedgerService.Data;
using PaymentLedgerService.DTOs;
using PaymentLedgerService.Models;
using StackExchange.Redis;
using System.Net.Http.Json;
using System.Text.Json;

namespace PaymentLedgerService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentsController : ControllerBase
    {
        private readonly LedgerDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly IConnectionMultiplexer _redis;

        public PaymentsController(LedgerDbContext db, IHttpClientFactory httpClientFactory, IConfiguration config, IConnectionMultiplexer redis)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _config = config;
            _redis = redis;
        }

        [HttpPost]
        public async Task<ActionResult<PaymentResponse>> CreatePayment(
            CreatePaymentRequest request,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
        {
            if (string.IsNullOrWhiteSpace(idempotencyKey))
                return BadRequest("Idempotency-Key header is required.");

            // Pehle check karo — ye key pehle use ho chuki hai kya?
            var existingKey = await _db.IdempotencyKeys
                .FirstOrDefaultAsync(k => k.Key == idempotencyKey);

            if (existingKey != null)
            {
                var cachedResponse = JsonSerializer.Deserialize<PaymentResponse>(existingKey.ResponseBody);
                return StatusCode(existingKey.ResponseStatusCode, cachedResponse);
            }

            // Basic validation
            if (request.AmountMinorUnits <= 0)
                return BadRequest("Amount must be greater than zero.");

            if (request.FromAccountId == request.ToAccountId)
                return BadRequest("From and To accounts must be different.");

            var fromExists = await _db.Accounts.AnyAsync(a => a.Id == request.FromAccountId);
            var toExists = await _db.Accounts.AnyAsync(a => a.Id == request.ToAccountId);

            if (!fromExists || !toExists)
                return BadRequest("One or both accounts do not exist.");

            var transactionId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var debitEntry = new LedgerEntry
            {
                TransactionId = transactionId,
                AccountId = request.FromAccountId,
                Type = EntryType.Debit,
                AmountMinorUnits = request.AmountMinorUnits,
                Currency = request.Currency,
                Description = request.Description,
                CreatedAtUtc = now
            };

            var creditEntry = new LedgerEntry
            {
                TransactionId = transactionId,
                AccountId = request.ToAccountId,
                Type = EntryType.Credit,
                AmountMinorUnits = request.AmountMinorUnits,
                Currency = request.Currency,
                Description = request.Description,
                CreatedAtUtc = now
            };

            var response = new PaymentResponse
            {
                TransactionId = transactionId,
                FromAccountId = request.FromAccountId,
                ToAccountId = request.ToAccountId,
                AmountMinorUnits = request.AmountMinorUnits,
                Currency = request.Currency,
                CreatedAtUtc = now
            };

            using var dbTransaction = await _db.Database.BeginTransactionAsync();
            try
            {
                _db.LedgerEntries.AddRange(debitEntry, creditEntry);

                var idempotencyRecord = new IdempotencyKey
                {
                    Key = idempotencyKey,
                    ResponseBody = JsonSerializer.Serialize(response),
                    ResponseStatusCode = 200
                };
                _db.IdempotencyKeys.Add(idempotencyRecord);

                await _db.SaveChangesAsync();
                await dbTransaction.CommitAsync();
                // Cache invalidate karo — dono accounts ka balance ab stale hai
                var cacheDb = _redis.GetDatabase();
                await cacheDb.KeyDeleteAsync($"balance:account:{request.FromAccountId}");
                await cacheDb.KeyDeleteAsync($"balance:account:{request.ToAccountId}");
            }
            catch (DbUpdateException ex)
            {
                await dbTransaction.RollbackAsync();

                // Check karo — kya ye duplicate-key wali error hai (race condition)?
                var isDuplicateKeyError = ex.InnerException is SqlException sqlEx
                    && (sqlEx.Number == 2601 || sqlEx.Number == 2627);

                if (isDuplicateKeyError)
                {
                    // Dusri request ne yehi key isi waqt insert kar di — uska result wapis do
                    var raceResult = await _db.IdempotencyKeys.FirstAsync(k => k.Key == idempotencyKey);
                    var cachedResponse = JsonSerializer.Deserialize<PaymentResponse>(raceResult.ResponseBody);
                    return StatusCode(raceResult.ResponseStatusCode, cachedResponse);
                }

                // Koi aur database error — dobara throw karo, silently ignore nahi karna
                throw;
            }

            return Ok(response);
        }

        public class InitiatePaymentRequest
        {
            public int FromAccountId { get; set; }
            public int ToAccountId { get; set; }
            public long AmountMinorUnits { get; set; }
            public string Currency { get; set; } = "PKR";
            public string Description { get; set; } = string.Empty;
            public bool? ForceProviderFail { get; set; } // testing ke liye
        }

        [HttpPost("initiate")]
        public async Task<IActionResult> InitiatePayment(InitiatePaymentRequest request)
        {
            if (request.AmountMinorUnits <= 0)
                return BadRequest("Amount must be greater than zero.");

            if (request.FromAccountId == request.ToAccountId)
                return BadRequest("From and To accounts must be different.");

            var fromExists = await _db.Accounts.AnyAsync(a => a.Id == request.FromAccountId);
            var toExists = await _db.Accounts.AnyAsync(a => a.Id == request.ToAccountId);
            if (!fromExists || !toExists)
                return BadRequest("One or both accounts do not exist.");

            // ---- STEP 1: Pending intent pehle save karo (external call se PEHLE) ----
            var intent = new PaymentIntent
            {
                FromAccountId = request.FromAccountId,
                ToAccountId = request.ToAccountId,
                AmountMinorUnits = request.AmountMinorUnits,
                Currency = request.Currency,
                Description = request.Description,
                Status = PaymentIntentStatus.Pending,
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.PaymentIntents.Add(intent);
            await _db.SaveChangesAsync(); // isko commit karna zaroori hai — agar crash ho, ye record reh jayega

            // ---- STEP 2: External provider ko call karo ----
            bool providerSuccess;
            string? providerReference = null;
            string? failureReason = null;

            try
            {
                var client = _httpClientFactory.CreateClient("default");
                var chargeUrl = _config["ProviderSettings:ChargeUrl"] ?? "";

                var chargePayload = new
                {
                    amountMinorUnits = request.AmountMinorUnits,
                    currency = request.Currency,
                    forceFail = request.ForceProviderFail
                };

                var response = await client.PostAsJsonAsync(chargeUrl, chargePayload);
                var result = await response.Content.ReadFromJsonAsync<ChargeResult>();

                providerSuccess = result?.Success ?? false;
                providerReference = result?.ProviderReference;
                failureReason = result?.FailureReason;
            }
            catch (Exception ex)
            {
                // Network fail ho gaya — provider ka pata hi nahi chala kya hua
                // Intent 'Pending' hi reh jayega — reconciliation job isko baad mein flag karega
                providerSuccess = false;
                failureReason = $"Provider call failed: {ex.Message}";

                // NOTE: Yahan hum intent ko 'Failed' mark NAHI kar rahe, kyun ke hume asal mein
                // pata hi nahi ke provider ne charge process kiya ya nahi. 'Pending' rehne dena
                // zyada safe hai — reconciliation job isay manually verify karayega.
                return StatusCode(202, new
                {
                    message = "Payment intent created but provider call failed. Status remains Pending for reconciliation.",
                    intentId = intent.Id
                });
            }

            // ---- STEP 3: Result ke hisab se intent update karo ----
            if (!providerSuccess)
            {
                intent.Status = PaymentIntentStatus.Failed;
                intent.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                return Ok(new
                {
                    message = "Payment failed at provider.",
                    reason = failureReason,
                    intentId = intent.Id
                });
            }

            // ---- STEP 4: Success — ab actual double-entry ledger entries banao ----
            var transactionId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var debitEntry = new LedgerEntry
            {
                TransactionId = transactionId,
                AccountId = request.FromAccountId,
                Type = EntryType.Debit,
                AmountMinorUnits = request.AmountMinorUnits,
                Currency = request.Currency,
                Description = request.Description,
                CreatedAtUtc = now
            };

            var creditEntry = new LedgerEntry
            {
                TransactionId = transactionId,
                AccountId = request.ToAccountId,
                Type = EntryType.Credit,
                AmountMinorUnits = request.AmountMinorUnits,
                Currency = request.Currency,
                Description = request.Description,
                CreatedAtUtc = now
            };

            using var dbTransaction = await _db.Database.BeginTransactionAsync();
            try
            {
                _db.LedgerEntries.AddRange(debitEntry, creditEntry);

                intent.Status = PaymentIntentStatus.Completed;
                intent.TransactionId = transactionId;
                intent.ProviderReference = providerReference;
                intent.UpdatedAtUtc = now;

                await _db.SaveChangesAsync();
                await dbTransaction.CommitAsync();
                // Cache invalidate karo — dono accounts ka balance ab stale hai
                var cacheDb = _redis.GetDatabase();
                await cacheDb.KeyDeleteAsync($"balance:account:{request.FromAccountId}");
                await cacheDb.KeyDeleteAsync($"balance:account:{request.ToAccountId}");
            }
            catch
            {
                await dbTransaction.RollbackAsync();
                throw;
            }

            return Ok(new
            {
                message = "Payment completed successfully.",
                transactionId,
                intentId = intent.Id,
                providerReference
            });
        }

        private class ChargeResult
        {
            public bool Success { get; set; }
            public string? ProviderReference { get; set; }
            public string? FailureReason { get; set; }
        }
    }
}