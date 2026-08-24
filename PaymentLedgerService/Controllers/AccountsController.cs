using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentLedgerService.Data;
using PaymentLedgerService.Models;
using StackExchange.Redis;

namespace PaymentLedgerService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountsController : ControllerBase
    {
        private readonly LedgerDbContext _db;
        private readonly IConnectionMultiplexer _redis;

        public AccountsController(LedgerDbContext db, IConnectionMultiplexer redis)
        {
            _db = db;
            _redis = redis;
        }

        public class CreateAccountRequest
        {
            public string Name { get; set; } = string.Empty;
            public string AccountType { get; set; } = string.Empty;
        }

        [HttpPost]
        public async Task<ActionResult<Account>> CreateAccount(CreateAccountRequest request)
        {
            var account = new Account
            {
                Name = request.Name,
                AccountType = request.AccountType
            };

            _db.Accounts.Add(account);
            await _db.SaveChangesAsync();

            return Ok(account);
        }

        [HttpGet("{id}/balance")]
        public async Task<ActionResult> GetBalance(int id)
        {
            var accountExists = await _db.Accounts.AnyAsync(a => a.Id == id);
            if (!accountExists)
                return NotFound();

            var cacheDb = _redis.GetDatabase();
            var cacheKey = $"balance:account:{id}";

            // Step 1: Pehle cache check karo
            var cachedValue = await cacheDb.StringGetAsync(cacheKey);
            if (cachedValue.HasValue)
            {
                return Ok(new
                {
                    AccountId = id,
                    BalanceMinorUnits = long.Parse(cachedValue!),
                    Source = "cache"
                });
            }

            // Step 2: Cache miss — DB se calculate karo
            var credits = await _db.LedgerEntries
                .Where(e => e.AccountId == id && e.Type == EntryType.Credit)
                .SumAsync(e => e.AmountMinorUnits);

            var debits = await _db.LedgerEntries
                .Where(e => e.AccountId == id && e.Type == EntryType.Debit)
                .SumAsync(e => e.AmountMinorUnits);

            var balanceMinorUnits = credits - debits;

            // Step 3: Cache mein save karo, 60 second expiry ke sath
            // (expiry safety-net hai — agar kabhi invalidation miss ho jaye, cache khud hi stale nahi rahega zyada der)
            await cacheDb.StringSetAsync(cacheKey, balanceMinorUnits, TimeSpan.FromSeconds(60));

            return Ok(new
            {
                AccountId = id,
                BalanceMinorUnits = balanceMinorUnits,
                Source = "database"
            });
        }

        [HttpGet("/api/ledger/verify")]
        public async Task<ActionResult> VerifyLedgerIntegrity()
        {
            var totalDebits = await _db.LedgerEntries
                .Where(e => e.Type == EntryType.Debit)
                .SumAsync(e => e.AmountMinorUnits);

            var totalCredits = await _db.LedgerEntries
                .Where(e => e.Type == EntryType.Credit)
                .SumAsync(e => e.AmountMinorUnits);

            var isBalanced = totalDebits == totalCredits;

            return Ok(new
            {
                TotalDebits = totalDebits,
                TotalCredits = totalCredits,
                IsBalanced = isBalanced
            });
        }
    }
}