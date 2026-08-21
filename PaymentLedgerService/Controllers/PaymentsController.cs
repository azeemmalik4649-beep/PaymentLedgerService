using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PaymentLedgerService.Data;
using PaymentLedgerService.DTOs;
using PaymentLedgerService.Models;
using System.Text.Json;

namespace PaymentLedgerService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PaymentsController : ControllerBase
    {
        private readonly LedgerDbContext _db;

        public PaymentsController(LedgerDbContext db)
        {
            _db = db;
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
    }
}