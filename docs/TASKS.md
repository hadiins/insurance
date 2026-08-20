# TASKS.md — ترتیب اجرا (v2)

**Work strictly in order.** Each task ends with a verifiable check. Do not start the next until the
current one passes. Tick the box when done, then commit.

Order matters for two reasons: the schema is expensive to change once data exists, and the tab
shell must exist before any page is written.

---

## Milestone 0 — Foundation (Tasks 1–5)

### [x] Task 1 — Solution scaffold

```
/src
  /Aqsat.Domain          entities, enums, value objects — no dependencies
  /Aqsat.Application     services, interfaces, DTOs
  /Aqsat.Infrastructure  EF Core, api.ir client, Hangfire jobs
  /Aqsat.Api             controllers, SignalR hubs, DI, middleware
  /aqsat-web             Vite + React + TS
/tests/Aqsat.UnitTests
docker-compose.yml       SQL Server + api
```

Plus: `.editorconfig`, `.gitignore`, Serilog, `/health`, global exception handler returning a
structured problem response (**never a bare 500 with no body**), and the three mock interfaces:
`ISmsSender`, `IIdentityVerifier`, `IPaymentGateway` — logging fakes so development never stalls
on external accounts.

**Check:** `docker compose up` starts · `/health` returns 200 · the web app renders.

---

### [x] Task 2 — MDI tab shell (frontend only)

Read the *Frontend* section of `CLAUDE.md` in full first. Reference behaviour:
`docs/reference-tabs-demo.html` — match the interaction, not the markup.

Sidebar (9 groups, 40+ items, accordion, search with highlighting, collapsed mode with hover
flyout) · tab bar (pinned «امروز», dirty dot, close confirm, `Ctrl+W`, `Ctrl+Tab`) · Zustand store
for tabs + per-tab preserved state · multi-instance rules per `CLAUDE.md` · live-updating create-tab
titles · configurable limit (default 12) with overflow scroll and an all-tabs dropdown · light/dark
theme persisted with **no flash on load**.

**Check:** type into a create-form, open two more tabs, return — every value, scroll position and
selection intact. Open two create-tabs; titles differ. Close a dirty tab; it asks.

---

### [x] Task 3 — Data model + RLS ⚠️ HIGHEST RISK

All of section 2 of `PHASE-1-SPEC.md` — **including the tables whose UI ships later**
(`Endorsement`, regional/HQ levels). Retrofitting a schema onto live data is the one mistake with
no cheap fix.

Sequential GUID PK non-clustered + `BizId` clustered · `AgencyId` on every operational table and
**first in every index** · global `DeleteBehavior.Restrict` · `[Timestamp] RowVersion` everywhere ·
soft delete with global query filter · RLS predicate function + `CREATE SECURITY POLICY` in a
raw-SQL migration · session context per request · `AuditEntry` with `bigint` identity and `PolicyId`
on every row · unique indexes for payment dedupe, marketer-user, active lock, presence.

**Check — all five must pass:**
1. Read the migration line by line. **Zero** `ReferentialAction.Cascade`.
2. Seed two agencies, authenticate as A → **zero rows of B**, even with a deliberately unfiltered `DbSet`.
3. Insert referencing an out-of-scope FK → service layer rejects it.
4. Duplicate payment insert → treated as success, one row.
5. Assign one marketer to a second agency → unique index rejects it.

---

### [x] Task 4 — Auth, roles, org hierarchy, marketer accounts

JWT · `AppUser`/`Role`/`UserOrgRole` · permission policies · self-referencing `Organization` with
all three levels in seed · scope middleware · **`Marketer` profile separate from `AppUser`** (many
marketers never log in) · `Marketer.SelfView` restricted role.

**Check:** a marketer account sees **only** their own customers. Attempting to read another
marketer's data returns 403, not empty. A marketer without a login still receives commission
entries.

---

### [x] Task 5 — Audit infrastructure

`SaveChangesAsync` override writing audit rows **in the same transaction**. Human-readable Persian
description composed at write time. Encrypted fields recorded as "changed" only. Marketer views
logged.

**Check:** modify a policy → one audit row with correct `PolicyId` and description. Force an
exception mid-transaction → **neither** the change nor the audit row persists.

---

## Milestone 1 — Policies & import (Tasks 6–9)

### [x] Task 6 — Insurance lines & issuance form

`InsuranceLine` seeded with all lines and sub-types · **line dropdown is the first field** (niaz
#10) · form adapts: vehicle fields for ثالث/بدنه, property fields for آتش‌سوزی · `RequiresVehicle`
enforced in the service layer, not just the UI.

**Fixed field order** (niaz #9):
```
نوع بیمه‌نامه → مشتری → مورد بیمه → حق بیمه → کارمزد خدمات →
پیش‌پرداخت → تعداد اقساط → جدول اقساط → بازاریاب → وثیقه
```

**Check:** create an آتش‌سوزی policy with no vehicle — accepted. Create a ثالث with no vehicle —
rejected with a clear Persian message.

---

### [x] Task 7 — Import engine + Fanavaran parser

Generic pipeline (upload → detect → **preview 20 rows with detected currency and date format** →
column mapping savable per agency → validate → commit → batch report) plus the concrete Fanavaran
adapter: sheet `CarSalesBNVer`, parse `نام خانوادگی کد 8030987`, strip `شماره قرارداد...` from the
contract name. Rials → toman. Duplicate key `PolicyNumber + SeqNo`. A failed row never aborts a batch.

**Check:** with a 180-row fixture and templates configured, **137 policies (76%) flagged
installment. Not 28.** If you get 28, you searched for «اقساطی» — go back and read the contract
mapping rule.

---

### [x] Task 8 — Templates, schedule, service fee

Contract→template mapping UI · schedule per 3.1/3.2 · **service fee inserted between premium and
down payment** · down-payment suggestion for round installments · warning above `MaxInstallments` ·
**Excel-like batch grid** where the agent edits down payment and count across many policies (not
137 separate forms).

**Check:** unit tests for due dates including 31st-of-month overflow and leap years.
`10,700,000 premium − 1,700,000 down ÷ 9 = exactly 1,000,000 each.`
A 500,000 service fee raises `TotalReceivable` but **not** the commission base.

---

### [x] Task 9 — Editable installments (niaz #3)

Amount and due date editable per installment · `IsManuallyEdited` flag · audit row per edit ·
**commission recalculated for that installment** · deadline recomputed.

**Check:** edit one installment's amount → its commission entry updates, others do not. Edit its
due date → its settlement deadline shifts, holiday rule reapplied.

---

## Milestone 2 — The core loop (Tasks 10–12)

### [x] Task 10 — Settlement countdown ⭐

Query per 3.3 · urgency classification · holiday shift via `IsHoliday` (cached) ·
**SHORTFALL = owed − collected** as the dominant element on the dashboard · daily Hangfire
recalculation.

**Check:** seed installments at every urgency level — the dashboard orders them correctly and the
shortfall figure is arithmetically right. Advance the clock one day; everything re-sorts.

---

### [x] Task 11 — Payments & commission activation

Payment + allocation per 3.4 · one-click from a queue row with pre-filled but **editable** amount ·
batch mode · partial status · overpayment → next installment, agent-editable · reversal with audit ·
idempotency · **on full settlement, flip the matching `CommissionEntry` to payable**.

**Check:** 1,500,000 against a 1,000,000 installment → first settles, 500,000 to the next. 600,000
→ `partial`, balance 400,000, installment stays open, **commission stays pending**. Identical
payment twice → one record.

---

### [x] Task 12 — Concurrency

All three layers per `docs/CONCURRENCY.md`. SignalR presence · atomic `MERGE` lock acquisition ·
force-release with mandatory reason + audit + instant notification · polling fallback.

**Check:** two browsers, same policy. Presence shows in both. Second edit refused with the holder's
name. Force-release notifies the holder immediately and **their unsaved input survives**.

---

## Milestone 3 — What agents asked for (Tasks 13–17)

### [ ] Task 13 — Marketers & commission ⭐

`Marketer` + `MarketerRate` per line · commission generation at issuance per 3.5 ·
**down-payment slice payable immediately** · per-installment slices pending until settled ·
proportional on partial default, **no clawback** · payment batches · marketer screen ·
**restricted marketer panel** per spec 2.2 — no export, every view audited.

**Check — the arithmetic must be exact:**
- `downShare + Σ instShare == totalCommission` to the rial
- base is `NetPremium`, **never** `TotalReceivable`
- customer pays 7 of 9 → exactly 7 slices payable, 2 stay pending forever
- a rate change today does **not** alter yesterday's entries
- a marketer sees their own customers and **nothing else**; export is unavailable

---

### [ ] Task 14 — api.ir client + SMS

Typed client · shared envelope with `success` checked · per-service caching · retry with backoff ·
circuit breaker · cost logging · **Sandbox by default, paid endpoints behind an explicit flag**.

Then the daily reminder job per 3.7, **conditional at send time**, plus manual filtered sending
(niaz #13): filter → **preview count and cost** → confirm → send. Templates with placeholders,
per-installment send log, delivery report, CallOTP fallback.

**Check — this is a cost test, not a feature test:** seed 100 installments, settle 85 before the
second window, run the job. **Exactly 15 second reminders. Not 100.** If it is 100, the sequence was
pre-scheduled — fix it.

---

### [ ] Task 15 — Profit & loss ⭐ TOP-RANKED BY AGENTS

Report per 3.6 · **accrual/cash toggle** · configurable default write-off threshold · breakdown by
line, marketer, month · period comparison · dashboard summary card · xlsx export.

**Check:** hand-compute one month from seed data and match it to the rial. Flip accrual→cash — the
numbers change coherently and both are defensible.

---

### [ ] Task 16 — Renewal tracking (niaz #4)

`RenewalWatch` · walk-in prospect registration (name + mobile + current insurer + expiry +
`NotifyDaysBefore`) · automatic watches from `Policy.EndDate` · reminder job → customer **and their
marketer** · conversion tracking.

**Check:** register a prospect expiring in 60 days with `NotifyDaysBefore = 2`. Advance the clock
to day 58 → exactly one reminder, to both customer and marketer. Mark converted → linked to the new
policy.

---

### [ ] Task 17 — Customer file & policy file (niaz #7)

**Customer file:** all policies across all lines, aggregate balance, full payment history,
collateral, unified timeline.
**Policy file:** installments, endorsements, commission, history tab (one indexed query on
`AuditEntry.PolicyId`) rendered as
`علی رضایی — ۱۹ مرداد ۱۰:۴۲ — ثبت پرداخت قسط ۲`.

---

## Milestone 4 — Ship (Tasks 18–20)

### [ ] Task 18 — Reports & cheques
Filtered reports with server-side paging and xlsx export, read from a view or Hangfire-populated
reporting table — **never a heavy join on live operational tables**. Try
`READ COMMITTED SNAPSHOT` and measure before building a reporting table.
Cheque registration, status lifecycle, upcoming view, `ChequeColor` (cached 30 days).

### [ ] Task 19 — Hardening
Loading/empty/error on every list · clear-filter control on every filter · total count always shown
· invalid scope throws rather than returning empty · structured errors · rate limiting · Serilog
with rotation.

**Check:** kill the database mid-session. **No blank screen anywhere.**

### [ ] Task 20 — Deployment
Multi-stage Dockerfile · prod compose · migration-on-startup with a lock · daily backup ·
**documented and tested restore** · env configs · README runbook.

**Check:** restore a backup into a clean container and confirm the data. A backup never restored is
not a backup.

---

## Milestone 5 — Update system (defer until ~20 customers)

Full spec: `docs/UPDATE-SYSTEM.md`. Until you run multiple servers, a deploy script is enough and
this opens a security-sensitive path earlier than necessary.

### [ ] Task 21 — `Aqsat.Updater` service
Separate project with Docker socket access. The main API must **never** have it.

### [ ] Task 22 — Update panel
`Platform.Owner` + 2FA · SignalR progress · maintenance mode with 60s warning · Persian error
screen with copy-for-support · rollback · history.

### [ ] Task 23 — Release pipeline
Build + sign, package registration, staged rollout, yank, runbook.

---

## Progress

| Milestone | Tasks | Est. |
|---|---|---|
| 0 — Foundation | 1–5 | 3 weeks |
| 1 — Policies & import | 6–9 | 3 weeks |
| 2 — Core loop | 10–12 | 3 weeks |
| 3 — What agents asked for | 13–17 | 4 weeks |
| 4 — Ship | 18–20 | 1.5 weeks |
| 5 — Updates | 21–23 | deferred |

**~14–15 weeks for one full-time developer.**

---

## Four tasks to slow down on

| Task | Why |
|---|---|
| **3 — Data model** | the only thing that is expensive to change later |
| **10 — Countdown** | prevents the operational disaster that sells the product |
| **13 — Commission** | wrong arithmetic here means underpaying real people real money |
| **15 — P&L** | the feature agents ranked first |

**Task 14's test is about money, not correctness.** A wrong implementation still "works" and
quietly costs 185,000 toman per customer per month.

Ask before deviating. The rules in `CLAUDE.md` come from constraints discovered in the field.
