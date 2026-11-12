# Ledger Architecture Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement task by task.

**Goal:** Preserve debt and payment history behind a testable local ledger.

**Architecture:** Core owns immutable-in-use principal and payment allocation rules.
Storage owns EF transactions, migrations and backup. UI receives the ledger and displays
individual and family balances separately. Retain the current desktop framework.

**Tech Stack:** .NET 10, Avalonia 12, EF Core 10, SQLite; dependency-free executable tests.

**Spec:** `docs/superpowers/specs/2026-11-13-ledger-design.md`

## Global Constraints

- No real user data deletion; legacy databases must be backed up before baseline adoption.
- Positive amounts with at most two fractional digits; excess payments rejected.
- No network service or visual redesign in this local-foundation change.
- Execute in current folder on `refactor/ledger-architecture` as authorized by “uygulamaya geçelim”.

## Review Focus

- Partial and full payments preserve principal, dates and allocations across reopen.
- Invalid/excess payment leaves no partial writes.
- Individual and direct-family totals never relabel another person's debt as one's own.
- Archived history and repeated settlement cannot lose records or award repeated scores.
- Existing EnsureCreated databases and live SQLite backups remain readable.

### Task 1: Core ledger rules

**Files:** `CreditBook.Core/`, `CreditBook.Tests/Program.cs`, project files.
**Interfaces:** `LedgerRules.Allocate(debts, amount)` returns debt/amount allocations;
`LedgerRules.Score(debts, now)` derives a bounded score.

- [x] Write executable cases for FIFO, settlement preserving principal, invalid precision,
  excess amount and score repeatability; run and observe missing implementation.
- [x] Implement models and rules without Avalonia/EF dependencies.
- [x] Run `dotnet run --project CreditBook.Tests` and verify the core cases pass.

### Task 2: Transactional SQLite ledger

**Files:** `CreditBook.Storage/`, migrated model/migration files, integration tests.
**Interfaces:** `Ledger` owns customer/relative management, debt posting, payment recording,
settlement, archive, account queries, overdue queries and `Backup(destination)`.

- [x] Add tests using temporary file databases for allocation persistence, rollback,
  independent family totals, protected history, migration adoption and backup reopening.
- [x] Run and observe missing storage implementation.
- [x] Implement transaction-scoped contexts, migration/baseline support and SQLite backup.
- [x] Run all executable tests and verify.

### Task 3: Desktop integration

**Files:** UI ViewModels, window bindings, App composition, solution and README.
**Interfaces:** UI consumes `Ledger` through constructor injection; storage exposes snapshots
  so no tracked EF entity or context reaches a view.

- [x] Cover desktop viewmodel workflows without starting Avalonia or a real user database.
- [x] Replace direct EF access with ledger calls; remove toggled payment semantics,
  correct relative unlink, show original/remaining amounts and own/family balances.
- [x] Build the solution and run all tests; review whole diff with a fresh reviewer.
- [x] Document test/start commands and the deferred phone/server phase.
