# CLAUDE.md — دفتر اقساط (Aqsat)

Installment management & collection system for Iranian insurance agencies (بیمه شخص ثالث).

**Read `docs/PHASE-1-SPEC.md` before writing any code. Work through `docs/TASKS.md` in order.**

---

## What agents actually asked for

Requirements were collected from working agents (`niaz.md`). They ranked three things as decisive:

1. **سود و زیان** — profit & loss (commission in − marketer commission out − defaults)
2. **اعلان‌ها** — due dates, overdue, renewals
3. **پیامک** — to customers **and to marketers**

Plus the settlement countdown below, which prevents an operational disaster.

**P&L is not accounting.** Do not build a general ledger, journal entries, or tax filing. Every
input already exists in the system; it is arithmetic over data you already hold.

## The one thing this product does

An agency sells third-party motor insurance on installments. **The insurance company extends the
credit, not the agency** — but the agency must collect each installment from the customer and remit
it within **3 days of that installment's due date**. Miss the window and the agency's *entire*
issuing system is locked — no new policies of any kind. Agents borrow money to avoid this.

**So this system is an early-warning system, not an accounting tool.** Every design decision serves
one goal: the agent never gets locked out.

---

## Stack

| Layer | Choice |
|---|---|
| Backend | ASP.NET Core Web API (.NET 8+) |
| ORM | EF Core |
| DB | **SQL Server** (not Postgres) |
| Jobs | Hangfire (SQL Server storage) |
| Realtime | SignalR |
| Frontend | React + Vite + TypeScript |
| State | Zustand (tab/UI state) + TanStack Query (server state) |
| Styling | Tailwind + Radix UI |
| Deploy | Docker, Iranian cloud (data must stay in Iran) |

**Do not substitute.** Postgres, Next.js, Redux, TypeORM are all wrong for this project — the team
and hosting constraints are fixed.

---

## NON-NEGOTIABLE RULES

Violating any of these is a bug, not a style preference.

### Data & schema

1. **GUID primary keys, but NOT clustered.** Use `NEWSEQUENTIALID()` (or a sequential-GUID
   generator in C#). Put the clustered index on a separate `long BizId IDENTITY` column that never
   leaves the server. Random GUIDs as clustered keys destroy insert performance.
2. **Every table has `AgencyId`.** No exceptions among operational tables.
3. **`AgencyId` must be the FIRST column of every index.** RLS filters on it in every query; an
   index without it leading forces a scan.
4. **Every FK relationship is a real `FOREIGN KEY` in the database.** Not just C# validation.
5. **Every FK gets its own index.** SQL Server does not create these automatically.
6. **`DeleteBehavior.Restrict` globally.** In `OnModelCreating`:
   ```csharp
   foreach (var fk in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
       fk.DeleteBehavior = DeleteBehavior.Restrict;
   ```
   EF Core defaults to Cascade. **Read every generated migration before applying it.**
7. **No hard deletes.** `IsDeleted` + `DeletedAt`.
8. **No free-text field about a *person*.** Notes may describe a policy or an action, never a
   human being. This is the entire legal defence of the product. A `Customer.Notes` column is a
   bug.
9. **`[Timestamp] byte[] RowVersion` on every entity.**

### Security

10. **Row-Level Security in SQL Server**, via `CREATE SECURITY POLICY` + predicate function on
    `AgencyId`. Not `Where(x => x.AgencyId == ...)` in a repository. If a developer forgets the
    filter, the database must still refuse.
11. **RLS is not enough** — the service layer must also verify every referenced ID is inside the
    caller's scope. A FK to an invisible row does not throw; it silently succeeds.
12. **National ID encrypted at rest**, masked in every UI (`۰۰۷۲•••۴۵۳`).
13. **Never store postal code** unless a concrete feature needs it.
14. **API keys server-side only.** Never reach an external API from the browser.

### Error handling

15. **Empty `catch` is forbidden. Returning an empty list instead of an error is forbidden.**
    This is the single most common failure in comparable Iranian systems: the search page renders
    blank with no error and the user assumes there is no data.
16. **The UI must distinguish three states**: loading / empty-result / error. One shared component
    enforces this; no table renders without handling all three.
17. **If the caller's scope is empty or invalid, throw** — do not return zero rows. RLS returns
    nothing silently, which looks identical to "no data".
18. **Check `success` on every api.ir response**, not just the presence of `data`. Their own docs
    show `success:false` alongside populated `data`.

### Money & correctness

19. **Cartable amounts are in RIALS. Policy report amounts are in RIALS.** Store TOMAN internally
    (divide by 10). Show the unit in every import preview.
20. **A payment is its own entity**, not a field on an installment. One payment may span several
    installments; one installment may take several payments. Statuses: unpaid / partial / settled.
21. **An installment stays open until fully settled**, even if the next one is already due.
22. **Overpayment flows to the next installment**, editable by the agent. Shortfall carries forward.
23. **There are no discounts on installments** in Iranian insurance. Underpayment is always partial
    payment, never a discount.
24. **Payment recording must be idempotent.** Unique index on `(InstallmentId, PaidOn, Amount)`
    where not deleted; treat a duplicate-key violation as success.

### Cost control (this is a business rule, not an optimisation)

25. **SMS #2 and #3 must be sent conditionally at send-time, checking payment status.**
    Never pre-schedule the full reminder sequence.
    An agency has ~960 installments due per month. Blind 3-SMS-per-installment costs 561,405
    toman/month against 1,000,000 revenue (44% gross margin). Conditional sending costs 260,135
    (74% margin). **This one decision is worth 185,000 toman/month per customer.**
26. **Cache every api.ir call**: Shahkar forever, credit checks 30 days, IsHoliday until midnight.

### Audit

27. **One central immutable append-only audit table.** Per-record history is a *query* against it,
    not a second table.
28. **Every audit row carries `PolicyId`** — even when the subject is an installment or a cheque.
    This is what makes per-file history a single indexed lookup.
29. **Written inside the same transaction**, via a `SaveChangesAsync` override. No developer can
    forget it.
30. **Never log before/after values of encrypted fields.** Record "changed", not from-what-to-what.
31. **Human-readable description is composed at write time**, not render time.

---

## Business rules that are easy to get wrong

- **Due date = policy start date + N months, same day-of-month.** Verified against real cartable
  data. Not "30 days".
- **Installment count is NOT in the Fanavaran export.** It must come from a template or manual
  entry. Do not try to infer it from the premium.
- **Only 15.6% of installment policies carry the literal label «اقساطی».** 60.6% are identified by
  the *counterparty contract name* (e.g. «تجارت آفرینان تسنیم»). **Never grep for «اقساطی»** — use
  the agency-configured contract→template mapping. Getting this wrong loses 80% of records.
- **Down payment is a free amount, not a percentage.** Agents pick it so the remaining installments
  come out round. Offer a suggestion, never enforce a ratio.
- **Installment templates belong to the agency, not the system.** Agencies have different authority
  limits (some up to 6 installments, some up to 10). Warn above the configured ceiling.
- **The 3-day deadline runs per installment**, not on a fixed monthly date. Dozens of countdown
  windows are open simultaneously.
- **Holidays shift the deadline.** Use the IsHoliday service.

---

## Money rules (added in v2 — read carefully)

- **Field order in issuance is a business rule**: `NetPremium → ServiceFee → DownPayment →
  installments`. The agency charges its own service fee on top of the premium (this is existing
  industry practice, not something we invented).
- **`TotalReceivable = NetPremium + ServiceFee`.** The customer owes this; installments divide it.
- **Marketer commission base is `NetPremium` only — never `TotalReceivable`.** The service fee
  belongs to the agency. Getting this wrong overpays every marketer on every policy.
- **Commission accrues per installment, not per policy.** Slices proportional to *amount*, not
  count — the last installment differs after rounding.
- **The down payment gets its own commission slice, payable immediately.** Forgetting this silently
  underpays every marketer.
- **A slice becomes payable only on FULL settlement of its installment.** Partial is not enough.
- **Partial default is proportional.** Settled installments pay; unsettled ones never activate.
  **No clawback, no deduction from future commissions.**
- **Rate is locked at issuance.** Changing a marketer's rate today must not alter yesterday's
  entries.
- **Guard:** `downShare + Σ instShare == totalCommission` to the rial. Assert it in a test.

## Multi-line from day one

Policies are not only ثالث. Lines have sub-types (مسئولیت has several). The insured subject varies:
vehicle for ثالث/بدنه, property for آتش‌سوزی. **Do not force `VehicleId` onto every policy** —
nullable, with a service-layer rule keyed off `InsuranceLine.RequiresVehicle`.

## Marketer access — a security boundary, not a feature

An independent marketer is **outside the agency** and often works with several agencies. A marketer
sees only the customers they introduced, and:

- **No export. No xlsx, no print, no copy-list.** Otherwise the customer list walks out of the agency.
- **Every marketer view is audited**, and the agency owner can see what they looked at.
- **One marketer, one agency** — enforced by a unique index, not by UI validation.
- Marketers may see their customers' *overdue status* (status only, never amounts) — deliberate,
  since their commission depends on collection.

`Marketer` profile and `AppUser` login are **separate entities**. Many marketers never log in and
only receive SMS.

## Architecture: never couple to Fanavaran

Some insurers do not use Fanavaran at all. Three layers, strictly separated:

```
Domain model  ──  knows nothing about any source system
      ↑
Import adapters  ──  manual entry | generic mapped upload | Fanavaran parser | (future parsers)
      ↑
Business logic  ──  countdown, reminders, collection — source-agnostic
```

Operational parameters (deadline days, lock scope, due-date rule) are **per-agency settings**, not
constants.

---

## Multi-tenancy: build for three levels now

```
Headquarters ── Regional ── Agency
```

Only the Agency level ships in Phase 1, but the self-referencing `Organization` table and the RLS
predicate must support all three from day one. Retrofitting this later means a data migration on a
live system. Regional/HQ default to **aggregate** visibility, never customer detail.

---

## Frontend: MDI tab shell

**Build the shell before any content page.** Pages must know how to persist their state into the
tab store; retrofitting ten pages later means rewriting all ten.

- Right-hand collapsible sidebar, ~9 groups / 40+ items, with menu search
- Collapsed mode: icons with hover flyout
- «امروز» tab is pinned and cannot be closed
- Clicking a row opens a **new** tab; the source tab is untouched
- Full state preservation: form values, scroll position, selected row
- Dirty indicator + confirm-before-close (**never discard the user's input**)
- Tab limit: **configurable, default 12** — not a hard-coded 8
- Multi-instance rules:
  | Kind | Multiple? |
  |---|---|
  | Lists, dashboard | ❌ singleton |
  | **Create-new forms** | ✅ **unlimited** |
  | A specific record | ✅ different records · ❌ same record twice |
- Create-tab titles update live from the first meaningful field («بیمه‌نامهٔ جدید» → «بیمه‌نامه — ۷۴ب۳۲۱»)
- Light/dark theme, persisted, no flash on load

## Concurrency

See `docs/CONCURRENCY.md` — implement all three layers (rowversion / presence / locks).
Locks are for **writing only, never reading**.

---

## UI conventions

- **RTL, Persian throughout.** Vazirmatn font, full weight range 100–900.
- **Persian digits in all display**; Latin digits in inputs and API payloads.
- Jalali dates displayed; store `DateTimeOffset` UTC.
- Tabular numerals for every figure.
- `AgencyId` never shown to end users; national ID always masked.
- Every list shows a total count, even when zero.
- Any active filter renders a visible "clear filters" control.

---

## Definition of done for any task

- [ ] Builds with zero warnings
- [ ] Migration reviewed by eye (no accidental Cascade)
- [ ] RLS verified: a second agency's user genuinely cannot see the data
- [ ] Loading / empty / error states all implemented
- [ ] Audit row written in the same transaction
- [ ] Persian UI strings, Persian digits, RTL correct
- [ ] Unit test for any date or money calculation

---

## Ask before deciding

Do not silently guess on these. Stop and ask:

- Anything that changes the data model after Task 3
- Adding an npm/NuGet package not already in the project
- Anything touching real customer data (always use seed/fake data)
- Anything that would call a paid api.ir endpoint outside Sandbox
- Any deviation from the rules above
