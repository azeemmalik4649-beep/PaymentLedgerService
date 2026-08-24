using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentLedgerService.Data;
using PaymentLedgerService.DTOs;
using PaymentLedgerService.Models;

namespace PaymentLedgerService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReconciliationController : ControllerBase
    {
        private readonly LedgerDbContext _db;

        // "Kitni der Pending rehne ke baad usko stuck maana jaye" — threshold
        // Production mein ye configurable hota (appsettings se), abhi demo ke liye hardcode.
        private const int StuckThresholdMinutes = 5;

        public ReconciliationController(LedgerDbContext db)
        {
            _db = db;
        }

        [HttpGet("stuck-payments")]
        public async Task<ActionResult<List<StuckPaymentDto>>> GetStuckPayments()
        {
            var cutoffTime = DateTime.UtcNow.AddMinutes(-StuckThresholdMinutes);

            var stuckIntents = await _db.PaymentIntents
                .Where(p => p.Status == PaymentIntentStatus.Pending && p.CreatedAtUtc < cutoffTime)
                .OrderBy(p => p.CreatedAtUtc) // sabse purani pehle — sabse zyada urgent
                .ToListAsync();

            var result = stuckIntents.Select(p => new StuckPaymentDto
            {
                IntentId = p.Id,
                FromAccountId = p.FromAccountId,
                ToAccountId = p.ToAccountId,
                AmountMinorUnits = p.AmountMinorUnits,
                Currency = p.Currency,
                CreatedAtUtc = p.CreatedAtUtc,
                MinutesPending = (DateTime.UtcNow - p.CreatedAtUtc).TotalMinutes
            }).ToList();

            return Ok(result);
        }

        // Poori ledger + provider ka mismatch summary bhi ek jagah dikhane ke liye
        [HttpGet("summary")]
        public async Task<ActionResult> GetReconciliationSummary()
        {
            var pendingCount = await _db.PaymentIntents.CountAsync(p => p.Status == PaymentIntentStatus.Pending);
            var completedCount = await _db.PaymentIntents.CountAsync(p => p.Status == PaymentIntentStatus.Completed);
            var failedCount = await _db.PaymentIntents.CountAsync(p => p.Status == PaymentIntentStatus.Failed);

            var cutoffTime = DateTime.UtcNow.AddMinutes(-StuckThresholdMinutes);
            var stuckCount = await _db.PaymentIntents
                .CountAsync(p => p.Status == PaymentIntentStatus.Pending && p.CreatedAtUtc < cutoffTime);

            return Ok(new
            {
                TotalPending = pendingCount,
                TotalCompleted = completedCount,
                TotalFailed = failedCount,
                StuckPendingBeyondThreshold = stuckCount,
                ThresholdMinutes = StuckThresholdMinutes
            });
        }
    }
}