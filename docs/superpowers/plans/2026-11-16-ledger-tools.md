# Ledger tools implementation plan

> **For agentic workers:** Use superpowers:executing-plans or superpowers:subagent-driven-development. Track progress below; run tests before completion.

**Goal:** Deliver the five approved desktop features, including encrypted Google Drive backups.
**Architecture:** Core owns dated movements and due rules; Storage owns atomic ledger changes and database recovery. Independent backup transport and statement rendering modules integrate through the ViewModel.
**Tech Stack:** .NET 10, Avalonia 12, EF Core 10, SQLite, OAuth PKCE, AES-GCM.
**Spec:** docs/superpowers/specs/2026-11-16-ledger-tools-design.md

## Global Constraints

- Offline desktop, one writer per ledger, Turkish money/date presentation.
- Preserve history and existing data through incremental EF migrations.
- Default Drive interval 3 hours; configurable 2 hours.
- Never expose tokens or recovery keys in logs; never test against the real database.

## Review Focus

- Reversals crossing statement periods preserve original payments.
- Restore rejects corrupted/foreign/newer schemas and preserves the original on failure.
- Offline uploads retry without claiming cloud protection.
- Filters do not alter balances; Turkish text and multiline notes export safely.
- Keys recover on a new machine; retention never touches unrelated/manual files.

### Task 1: Ledger history, customer edits, due dates and statements

Files: Core/LedgerModels.cs, LedgerRules.cs, new Statement.cs; Storage/Ledger.cs, migrations; Tests/Program.cs.
Produces UpdateCustomer, ChangeDueDate, reason-bearing VoidDebt/ReversePayment, GetStatement; detached due/history/allocation summaries.
- [x] Write tests for customer edits, due boundaries, changes/history and period-crossing reversal; run RED.
- [x] Add migration and transactional methods, pure movement calculation.
- [x] Run full temporary SQLite suite GREEN.

### Task 2: Backup transport and secure storage

Files: Storage/Backup/* and Tests/BackupTests.cs, docs/google-drive-setup.md.
Consumes Ledger.Backup; produces encrypted archives, OAuth desktop Drive client, scheduler and retention, secret store; UI-facing facade documented by implementer.
- [x] Write RED tests for encryption, tamper/wrong-key rejection, scheduling, retries and retention.
- [x] Implement local/Drive backup, secure secrets, restore download and setup guide.
- [x] Run isolated backup tests GREEN with fake HTTP/clock.

### Task 3: CSV/PDF rendering

Files: Storage/StatementExport.cs (or separate rendering project), Tests/ExportTests.cs.
Consumes Core.AccountStatement immutable model; produces CSV and PDF from identical rows, Unicode and pagination.
- [x] Write RED tests for escaping, totals and multipage PDF.
- [x] Implement rendering and printable PDF output.
- [x] Run export tests GREEN.

### Task 4: Desktop workflows and safe restore

Files: UI ViewModels/Views; Storage/DatabaseRecovery.cs; README.
- [x] Write RED integration tests for validated restore and filtered totals.
- [x] Add customer editor, due controls/history, reason prompt, filters, date-range export, backup settings/status/connect/recovery and restore confirmation.
- [x] Build full solution, run all tests, inspect final diff through independent review and fix important findings.

## Progress

- Design records decisions explicitly approved in conversation; implementation authorized by “ilerleyelim”.
- User-owned untracked image assets preserved.
- Native implementation of ledger/UI; independent workers implemented backup and export. Independent review identified foreign SQLite restore acceptance, blocking cloud upload, and unbounded PDF headers. Each was reproduced in a regression and fixed.
- Additional desktop binding regression reproduced DateTimeOffset/DateTime mismatch in calendar filters; filters now use the calendar's DateTime type.
- Background backups record the exact SQLite snapshot revision, so concurrent ledger edits stay pending instead of being skipped.
- Final verification: `dotnet build CreditBook.slnx --no-restore -m:1 --disable-build-servers -v:minimal` → 0 warnings, 0 errors. `dotnet run --project CreditBook.Tests --no-restore -m:1 --disable-build-servers` → Failures: 0; desktop rendered at 1200×900 and 980×740.
- Live Google account authorization and native OS credential-store integration were not exercised by tests. User setup remains in docs/google-drive-setup.md. No actual customer database or Google account was changed.
