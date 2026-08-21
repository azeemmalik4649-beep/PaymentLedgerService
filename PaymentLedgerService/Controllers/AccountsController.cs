using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentLedgerService.Data;
using PaymentLedgerService.Models;

namespace PaymentLedgerService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountsController : ControllerBase
    {
        private readonly LedgerDbContext _db;

        public AccountsController(LedgerDbContext db)
        {
            _db = db;
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

            // Balance derive karna: Credits - Debits (agar Debit means paisa gaya, Credit means aaya)
            var credits = await _db.LedgerEntries
                .Where(e => e.AccountId == id && e.Type == EntryType.Credit)
                .SumAsync(e => e.AmountMinorUnits);

            var debits = await _db.LedgerEntries
                .Where(e => e.AccountId == id && e.Type == EntryType.Debit)
                .SumAsync(e => e.AmountMinorUnits);

            var balanceMinorUnits = credits - debits;

            return Ok(new { AccountId = id, BalanceMinorUnits = balanceMinorUnits });
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