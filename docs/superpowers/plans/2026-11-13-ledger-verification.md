# Ledger architecture verification

Final checks: solution builds successfully; 27 executable behavioral cases pass.
`git diff --cached --check` passes. Tests use temporary SQLite databases only.
Package restore used the existing local NuGet cache with audit disabled for offline
verification; dependency advisory status is not re-certified by this change.

A fresh read-only reviewer found two Important issues: stale account data after a failed
selection load, and overly permissive legacy schema recognition. Both were reproduced
with failing tests and corrected. Cancellation eligibility was also aligned through a
single Core rule after reproducing the reversed-payment UI mismatch.

No rendered-window visual verification was completed. A temporary-database startup probe
kept the process alive for six seconds but did not establish that window initialization
completed, so it is not counted as a passing UI smoke test.

## Execution record and decisions

# SDD ledger — plan: docs/superpowers/plans/2026-11-13-ledger-architecture.md
Pre-flight: Core allocation types feed Storage; Storage snapshots feed UI. Interfaces consistent.
Ruling: execute in the existing folder on a feature branch, without an additional worktree — user asked to proceed in this project; no files were dirty — cost if wrong: worktree can be created later.
Ruling: stage local foundation first; authenticated phone projection/server is deferred and not claimed delivered — the financial history must be correct first — cost if wrong: a separate server phase is still needed.
Ruling: use a dependency-free executable test runner because no test packages are cached; SQLite integration assertions exercise real data — cost if wrong: tests run with dotnet run rather than dotnet test.
Task 1: core rules RED (missing Core namespace) → GREEN (8/8). Core contains no EF/Avalonia references.
Task 2: complete — integration tests RED on missing Storage namespace and missing migration, then GREEN with transaction-scoped SQLite ledger; legacy adoption and backup covered.
Task 3: complete — ViewModel RED on missing injected constructor/commands, then GREEN. Search-state regression reproduced RED, fixed, GREEN.
Final review: fresh reviewer found two Important findings and one Minor.
Final: fixed failed account switching — Failed account switch clears old records and disables edits RED→GREEN.
Final: fixed weak legacy recognition — same-column nullability and foreign-key mismatch tests RED→GREEN.
Final: Ruling: cancellation eligibility must be the same in the snapshot and storage; treated as integration correctness and fixed with a shared Core rule — cost if wrong: policy adjustment is isolated to one rule.
Final: fixed cancellation eligibility — reversal eligibility regression RED→GREEN. No deferred minor findings remain.
Final: Ruling: retain documented policy forbidding cancellation after any collection history — reversals preserve audit rows and principal — cost if wrong: a later explicit debt-correction workflow is needed.
Final: Ruling: deferred mobile/server/auth/sync, visual redesign, restore/export UI, configurable due dates, multicurrency and credit balances stay out of this foundation phase — scope communicated before implementation — cost if wrong: further development remains before phone rollout.
Final: Ruling: already-lost legacy payment history cannot be recovered; old closed debts lack dates for score calculation — do not fabricate records — cost if wrong: old reporting remains incomplete.
Final: Ruling: scoring weights are retained as an explicit deterministic policy (100, -10 open overdue, -25 late settled, +5 ontime, clamp 0–100) — no repeated UI bonuses — cost if wrong: weights need future business calibration.
Final: Ruling: rendered Avalonia layout and concurrent backup under separate OS processes have not been visually/stress tested; compiled bindings, ViewModel flows and SQLite backup reopen tests passed — cost if wrong: manual device checks may reveal presentation or concurrency defects.
Final verification: build succeeds; 27 executable cases pass; no user's live database opened.
