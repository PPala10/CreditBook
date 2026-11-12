# CreditBook ledger architecture

Small-business customer debt and collection tracking. Desktop entry must work offline;
a later authenticated server will expose the last synchronized state to read-only phones.
This change delivers the local foundation, not that server or phone application.

Retain .NET 10, Avalonia, EF Core and SQLite. Split Core (models and deterministic
ledger rules), Storage (transactions, queries, migrations and consistent backups),
and UI (display state, validation feedback and user interactions). No pass-through
repository layer. The UI receives a ledger implementation instead of owning a DbContext.

User approved proceeding without the remaining interview. Builder defaults:
payments settle oldest unpaid debt first; family members retain individual balances
with a separately labeled group total. Reject excess payment rather than silently
truncating it. Debt principal and payment dates remain intact after settlement.
Reverse mistakes explicitly; never delete settled records or customers with history.
Existing paid checkboxes become one-way settlement actions, with no unpaid toggle.
Score is derived from history and current time, never incremented by checkbox clicks.
Notifications are derived from current overdue debts and survive reopening by recomputation.

There are no real records to migrate, but do not erase any existing database. Apply
incremental EF migrations. Recognize the legacy EnsureCreated schema only after verifying
its columns and taking a SQLite backup; baseline the existing migration history before
adding the new schema. Unknown schemas fail clearly. Existing remaining debt amounts
are retained; lost historical payments cannot be reconstructed.

Use temporary SQLite databases in integration tests, never the user's database.
Test partial/full allocation, precision, invalid amounts, excess payment, stable
FIFO ordering, own/family totals, archive restrictions, settlement idempotency,
migrations and backup reopening. Desktop compilation must pass.

Deferred: mobile/server synchronization, login, visual redesign, configurable due dates,
credit balances, export/restore UI, and multiple currencies. Current due rule stays 30 days.
