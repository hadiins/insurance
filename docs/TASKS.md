# TASKS.md — ترتیب اجرا

**Work strictly in order.** Each task ends with a verifiable check. Do not start the next task
until the current one passes. Tick the box when done.

Order matters for two reasons: the data model is expensive to change once data exists, and the tab
shell must exist before any page is written.

---

## Milestone 0 — Foundation

### [ ] Task 1 — Solution scaffold

Create:
```
/src
  /Aqsat.Domain          entities, enums, value objects — no dependencies
  /Aqsat.Application     services, interfaces, DTOs
  /Aqsat.Infrastructure  EF Core, api.ir client, Hangfire jobs
  /Aqsat.Api             controllers, SignalR hubs, DI, middleware
  /aqsat-web             Vite + React + TS
/tests
  /Aqsat.UnitTests
docker-compose.yml       SQL Server + api
```

Also: `.editorconfig`, `.gitignore`, Serilog, health check endpoint, global exception handler that
returns a structured problem response (**never a bare 500 with no body**).

**Check:** `docker compose up` starts; `/health` returns 200; the web app renders a blank page.

---

### [ ] Task 2 — MDI tab shell (frontend only, no backend)

Read the *Frontend* section of `CLAUDE.md` in full first.

- Right sidebar: 9 groups, 40+ items, accordion, menu search with match highlighting
- Collapsed mode with hover flyout
- Tab bar: pinned «امروز», dirty dot, close confirm, `Ctrl+W`, `Ctrl+Tab`
- Zustand store: tabs + per-tab preserved state (form values, scroll, selection)
- Multi-instance rules per `CLAUDE.md`; create-tab titles update from first meaningful field
- Configurable tab limit (default 12) with horizontal scroll and an all-tabs dropdown
- Light/dark theme, persisted, **no flash on load** (inline script in `<head>`)
- Placeholder content pages

**Check:** open a create-form, type, open two more tabs, return — every value, scroll position and
selection intact. Open two create-tabs simultaneously; titles differ. Close a dirty tab; it asks.

> A working reference implementation of this interaction exists at `tabs_demo.html` — match the
> behaviour, not the markup.

---

### [ ] Task 3 — Data model + RLS ⚠️ HIGHEST RISK

Everything in section 2 of `PHASE-1-SPEC.md`. This is the task where a mistake is most expensive.

- All entities with sequential GUID PK (non-clustered) + `BizId` clustered
- `AgencyId` on every operational table, **first column of every index**
- Global `DeleteBehavior.Restrict`
- `[Timestamp] RowVersion` everywhere
- Soft delete + global query filter
- RLS: predicate function + `CREATE SECURITY POLICY` in a raw-SQL migration
- Session context set per request from the authenticated user
- `AuditEntry` with `bigint` identity and `PolicyId` on every row
- Unique indexes: payment dedupe, active lock, presence

**Check — all four must pass:**
1. Read the generated migration line by line. Zero `ReferentialAction.Cascade`.
2. Seed two agencies. Authenticate as agency A. **Query returns zero rows of agency B**, even with
   a deliberately unfiltered `DbSet` query.
3. Insert a row referencing an out-of-scope FK → the service layer rejects it.
4. Duplicate payment insert → treated as success, one row.

---

### [x] Task 4 — Auth, roles, org hierarchy

JWT, `AppUser`/`Role`/`UserOrgRole`, permission-based authorization policies, self-referencing
`Organization` with all three levels represented in seed data, scope resolution middleware.

**Check:** a user with two roles in two agencies sees the correct scope after switching. A user
without `Payment.Write` receives 403, not 500.

---

### [x] Task 5 — Audit infrastructure

`SaveChangesAsync` override writing audit rows **in the same transaction**. Human-readable
description composed at write time. Encrypted fields recorded as "changed" only.

**Check:** modify a policy; one audit row appears with the correct `PolicyId` and description.
Force an exception mid-transaction; **neither** the change nor the audit row persists.

---

## Milestone 1 — Import

### [x] Task 6 — Import engine

Generic pipeline: upload → detect format → **preview (first 20 rows, detected currency and date
format)** → column mapping (savable per agency) → validate → commit → batch report.

- `ImportBatch` + `ImportRow` with per-row errors
- Duplicate key: `PolicyNumber + SeqNo`
- A failed row never aborts the batch
- Rials → toman conversion, surfaced in the preview

**Check:** import the same file twice — second run reports 100% duplicates, creates nothing.

---

### [ ] Task 7 — Fanavaran parser

Concrete adapter over Task 6. Sheet `CarSalesBNVer`; extract per section 4.1; parse
`نام خانوادگی کد 8030987` into name + external code; strip `شماره قرارداد...` from the contract
name.

**Check:** with a fixture of 180 rows and templates configured, 137 policies (76%) are flagged
installment. **Not 28.** If you get 28, you searched for «اقساطی» — go back and read rule on
contract mapping.

---

### [ ] Task 8 — Templates + schedule generation

Contract→template mapping UI; schedule generation per 3.1 and 3.2; down-payment suggestion;
warning above `MaxInstallments`; batch grid where the agent edits down payment and count for many
policies at once (Excel-like tabbing, not 137 separate forms).

**Check:** unit tests for due dates including 31st-of-month overflow and leap years. A 10,700,000
premium with a 1,700,000 down payment and 9 installments yields exactly 1,000,000 each.

---

## Milestone 2 — The core loop

### [ ] Task 9 — Settlement countdown ⭐ THE PRODUCT

Query per 3.3; urgency classification; holiday shift via `IsHoliday` (cached);
**shortfall = owed − collected** as the dominant element on the dashboard.

Hangfire job recalculating deadlines daily.

**Check:** seed installments at every urgency level; the dashboard orders them correctly and the
shortfall figure is arithmetically right. Move the system clock forward one day — everything
re-sorts.

---

### [ ] Task 10 — Payments

Payment + allocation per 3.4. One-click from a queue row with pre-filled but **editable** amount.
Batch mode. Partial payment status. Overpayment → next installment, agent-editable. Reversal with
audit. Idempotency enforced.

**Check:** pay 1,500,000 against a 1,000,000 installment → first settles, 500,000 lands on the
next. Pay 600,000 → status `partial`, balance 400,000, installment stays open. Submit the identical
payment twice → one record.

---

### [ ] Task 11 — Concurrency

All three layers per `docs/CONCURRENCY.md`. SignalR presence, atomic `MERGE` lock acquisition,
force-release with mandatory reason + audit + instant notification, polling fallback.

**Check:** two browsers, same policy. Presence appears in both. Second edit attempt is refused with
the holder's name. Force-release notifies the holder immediately and **their unsaved input
survives**.

---

## Milestone 3 — Reach

### [ ] Task 12 — api.ir client

Typed client, shared envelope handling with `success` checked, per-service cache durations, retry
with backoff, circuit breaker, cost logging. **Sandbox by default; paid endpoints require an
explicit config flag.**

**Check:** an api.ir outage degrades gracefully — the app keeps working, the user sees a clear
message, nothing crashes.

---

### [ ] Task 13 — SMS reminders

Daily Hangfire job per 3.5. **Conditional at send time — re-read rule 25 in `CLAUDE.md` before
writing this.** Templates with placeholders, per-installment send log, delivery report, CallOTP
fallback for critical items.

**Check — this is a cost test, not a feature test:** seed 100 installments, mark 85 paid before the
second reminder window, run the job. **Exactly 15 second reminders sent, not 100.** If the number
is 100, the sequence was pre-scheduled. Fix it.

---

### [ ] Task 14 — Collateral

Cheque/سفته registration, status lifecycle, upcoming-cheques view, `ChequeColor` lookup (cached
30 days), bounced handling.

---

### [ ] Task 15 — Reports

Date range, type, status filters. Server-side paging. xlsx export. **Read from a view or a
Hangfire-populated reporting table, never a heavy join against live operational tables.**
Try `READ COMMITTED SNAPSHOT` first and measure before building a reporting table.

---

### [ ] Task 16 — Policy file screen

Header, installment timeline, payment list, collateral, **history tab** (a single indexed query on
`AuditEntry.PolicyId`) rendering as a readable timeline: "علی رضایی — ۱۹ مرداد ۱۰:۴۲ — ثبت پرداخت قسط ۲".

---

## Milestone 4 — Ship

### [ ] Task 17 — Hardening

Every list has loading / empty / error states. Every filter has a clear control. Every list shows a
total. Invalid scope throws rather than returning empty. Structured errors everywhere. Rate limiting.
Serilog to file with rotation.

**Check:** kill the database mid-session. **No blank screen anywhere** — every page shows a clear
error.

---

### [ ] Task 18 — Deployment

Multi-stage Dockerfile, compose for prod, migration-on-startup with a lock, daily backup script,
**documented restore test**, environment configs, README with runbook.

**Check:** restore a backup into a clean container and confirm the data is intact. A backup that has
never been restored is not a backup.

---

---

## Milestone 5 — Update system (فقط بعد از ~۲۰ مشتری)

Full spec: `docs/UPDATE-SYSTEM.md`. **Do not build these before you have real customers on
multiple servers** — until then a deploy script is enough, and this opens a security-sensitive
path earlier than necessary.

### [ ] Task 19 — `Aqsat.Updater` service
Separate project with Docker socket access. The main API must **never** have it.
Signed-package verification, backup, migration, container swap, health check, auto-rollback.

### [ ] Task 20 — Update panel
`Platform.Owner` only + 2FA. SignalR progress. Maintenance mode with 60s warning.
Persian error screen with copy-for-support. Rollback button. History.

### [ ] Task 21 — Release pipeline
Build + sign script, package registration, staged rollout, yank, runbook.

---

## Progress

| Milestone | Tasks | Est. |
|---|---|---|
| 0 — Foundation | 1–5 | 2–3 weeks |
| 1 — Import | 6–8 | 2 weeks |
| 2 — Core loop | 9–11 | 2–3 weeks |
| 3 — Reach | 12–16 | 3 weeks |
| 4 — Ship | 17–18 | 1 week |
| 5 — Updates | 19–21 | 1–2 weeks · **defer** |

**~10–12 weeks for one full-time developer.**

---

## Reminders for whoever runs these tasks

- **Task 3 is the one to slow down on.** Everything after it is cheap to change; it is not.
- **Task 9 is the product.** If only one task is done exceptionally well, make it this one.
- **Task 13's test is about money**, not correctness. A wrong implementation still "works" and
  quietly costs 185,000 toman per customer per month.
- Ask before deviating. The rules in `CLAUDE.md` come from real constraints discovered in the
  field, not from preference.
