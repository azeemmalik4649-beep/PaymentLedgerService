# Payment & Double-Entry Ledger Service

A backend payment processing system built with **ASP.NET Core 8**, demonstrating core patterns used in real-world financial systems: double-entry bookkeeping, idempotent APIs, at-least-once webhook handling, the dual-write problem, reconciliation, and caching with invalidation.

This project simulates an external payment provider (`FakePaymentProvider`) so the full payment lifecycle — charge attempt, webhook confirmation, failure handling, and reconciliation — can be exercised end-to-end without any real payment gateway.

## Why this project exists

Payment systems have a reputation for being deceptively hard to get right. Money can't be "approximately" correct, requests get retried, networks fail mid-call, and external providers don't guarantee ordering or single delivery. This project was built to work through those failure modes directly rather than just reading about them — each concept below was implemented, broken on purpose, and then fixed.

## Architecture

Two ASP.NET Core Web API projects in one solution:

| Project | Role |
|---|---|
| `PaymentLedgerService` | The core service — ledger, payments, idempotency, webhook receiver, reconciliation |
| `FakePaymentProvider` | Simulates an external payment gateway — processes charges (with a configurable failure rate) and sends webhook confirmations back |

```
Client → PaymentLedgerService (/api/payments/initiate)
              │
              ├─► 1. Persist PaymentIntent (status: Pending)
              ├─► 2. Call FakePaymentProvider (/api/simulateprovider/charge)
              ├─► 3. On success → write double-entry LedgerEntry pair, mark intent Completed
              └─► 4. On failure/timeout → intent stays Pending/Failed for reconciliation

FakePaymentProvider → PaymentLedgerService (/api/webhook/payment-events)
              (signed, deduplicated, async confirmation — separate from the synchronous charge call)
```

## Core Concepts Implemented

### 1. Double-entry ledger, balances derived not stored
Every payment writes **two** immutable `LedgerEntry` rows (one Debit, one Credit) sharing a `TransactionId`. There is no `Balance` column anywhere — account balances are always computed on read via `SUM(Credits) - SUM(Debits)`. This guarantees the ledger can never silently drift from reality; a `/api/reconciliation/summary` check can always confirm total debits equal total credits system-wide.

### 2. Minor-unit currency storage
Amounts are stored as `long` values in the smallest currency unit (e.g. paisa, not rupees) rather than `decimal`. This avoids floating-point and rounding ambiguity entirely — the same convention used by Stripe, PayPal, and most production payment systems.

### 3. Idempotency
`POST /api/payments` requires an `Idempotency-Key` header. The key is stored with a **database-level unique constraint**, so even a race condition (two identical requests arriving simultaneously) can't produce two payments — the second insert fails at the DB level and the first result is returned instead.

### 4. Dual-write problem
Calling an external provider and writing to your own database is not atomic — a crash between the two leaves an inconsistent state. This is handled by:
1. Persisting a `PaymentIntent` (status `Pending`) **before** calling the provider
2. Calling the provider
3. Updating the intent based on the result (`Completed` / `Failed`)

If the app crashes mid-call, the intent is left `Pending` rather than guessed at — it becomes visible to reconciliation instead of silently disappearing.

### 5. At-least-once webhook delivery
Webhooks are treated as **at-least-once**, not exactly-once — the same event may arrive more than once. Each incoming webhook is:
- **Signature-verified** via HMAC-SHA256 (shared secret, constant-time comparison to prevent timing attacks)
- **Deduplicated** via a unique constraint on the provider's event ID — a repeated event is acknowledged with 200 OK but not reprocessed
- Stored with the provider's own timestamp, so downstream processing can reason about ordering independently of arrival order

### 6. Reconciliation (detect, don't auto-fix)
A reconciliation endpoint scans for `PaymentIntent` records stuck in `Pending` beyond a threshold — evidence that a provider call failed or the app crashed mid-flow. It deliberately **only reports** these; it does not auto-resolve them, since the service cannot know on its own whether the provider actually processed the charge.

### 7. Redis caching with invalidation
Account balance lookups are cached in Redis (`GET /api/accounts/{id}/balance`) with a 60-second TTL as a safety net. On every successful payment, the cache entries for both involved accounts are explicitly invalidated — so reads are fast, but never more than one write behind.

## Tech Stack

- ASP.NET Core 8 Web API
- Entity Framework Core 8 + SQL Server
- StackExchange.Redis (Memurai for local Windows development)
- xUnit + EF Core InMemory (unit tests)
- HMAC-SHA256 for webhook signing

## API Endpoints

| Method | Endpoint | Purpose |
|---|---|---|
| POST | `/api/accounts` | Create an account |
| GET | `/api/accounts/{id}/balance` | Get derived balance (cached) |
| POST | `/api/payments` | Record a payment directly (idempotent) |
| POST | `/api/payments/initiate` | Full dual-write flow via the simulated provider |
| POST | `/api/webhook/payment-events` | Receive signed, deduplicated provider webhooks |
| GET | `/api/reconciliation/stuck-payments` | List payments stuck in Pending beyond threshold |
| GET | `/api/reconciliation/summary` | System-wide intent status counts |
| POST | `/api/simulateprovider/charge` | (FakePaymentProvider) Simulate a charge attempt |
| POST | `/api/simulatepayment` | (FakePaymentProvider) Send a signed webhook event |

## Running Locally

**Prerequisites:** .NET 8 SDK, SQL Server (local instance), Redis-compatible server (Memurai on Windows).

1. Clone the repo and open `PaymentLedgerService.sln` in Visual Studio
2. Set both `PaymentLedgerService` and `FakePaymentProvider` as startup projects (**Solution → Configure Startup Projects → Multiple startup projects**)
3. Update connection strings in both projects' `appsettings.json` if your local ports/instances differ
4. Run EF Core migrations:
   ```
   Update-Database
   ```
   (in Package Manager Console, with `PaymentLedgerService` set as the default project)
5. Ensure Redis/Memurai is running on `localhost:6379`
6. Press F5 — both services will launch with Swagger UIs open

## Testing

Unit tests cover the two areas most prone to subtle bugs in payment systems:
- Double-entry balance correctness (single and cumulative multi-payment scenarios)
- Idempotency key uniqueness behavior

Run via Visual Studio Test Explorer, or:
```
dotnet test
```

## What I'd Add With More Time

- Multi-currency support with exchange-rate snapshots per transaction
- A background hosted service to run reconciliation on a schedule rather than on-demand
- Retry with exponential backoff for the provider charge call, rather than failing immediately
- Structured logging/correlation IDs across the payment → webhook flow for tracing a single transaction end-to-end
