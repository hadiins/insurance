# مشخصات فاز ۱ — Phase 1 Specification

**v2 — rewritten after collecting requirements from working agents (`niaz.md`).**

The three capabilities agents named as decisive:
1. **سود و زیان** — profit & loss
2. **اعلان‌ها** — due dates, overdue, renewals
3. **پیامک** — to customers **and to marketers**

Everything below serves those three, plus the settlement countdown that prevents lockout.

---

# 1. Scope

## In

| # | Capability | Source |
|---|---|---|
| 1 | Import Fanavaran policy report | replaces manual entry |
| 2 | **Multi-line policies** (ثالث، بدنه، آتش‌سوزی، مسئولیت…) with sub-types | niaz #1, #10 |
| 3 | Installment templates + contract mapping | 80% of records depend on it |
| 4 | Auto-generate schedule, **editable amounts and dates** | niaz #3 |
| 5 | **Service fee (کارمزد خدمات)** in the issuance flow | niaz #9 |
| 6 | **Settlement countdown** | prevents lockout |
| 7 | Payment recording (one-click, partial, overpay) | daily work |
| 8 | **Customer file spanning multiple policies** | niaz #7 |
| 9 | **Marketers + per-line commission, per-installment** | niaz #11, #14 |
| 10 | **Profit & loss report** | ⭐ top-ranked by agents |
| 11 | **Renewal reminders**, incl. policies at other insurers | niaz #4 |
| 12 | SMS to customers **and marketers**, with manual filtered sends | niaz #6, #13 |
| 13 | Cheque / collateral tracking | agent-requested |
| 14 | Reports with filters | agent-requested |
| 15 | Per-file audit trail | sells to multi-staff agencies |
| 16 | MDI tab shell + sidebar + theme | niaz #8, foundation |

## Data model now, UI later

Build the tables in Task 3; the screens come in Phase 2. Changing a schema on live data is the one
expensive mistake.

- **الحاقیه (endorsements)** — niaz #12. Schema + recalculation logic now, management UI later.
- **Regional / HQ levels** — self-referencing org + RLS predicate now, dashboards later.

## Out (Phase 2+)

Credit-check APIs · shared credit network · customer self-registration portal · endorsement UI ·
regional/HQ dashboards · legal notice generation · mobile app · full accounting (ledger, journal
entries, tax filing)

> **P&L is not accounting.** It is `commission earned − marketer commission − defaults`. All inputs
> already exist in the system. Do not build a general ledger.

## Blocked — build behind an interface, mock until unblocked

`ISmsSender` · `IIdentityVerifier` · `IPaymentGateway` — real implementations land in Task 14.

---

# 2. Data model

## 2.1 Organization & settings

**Organization** — self-referencing, three levels from day one.
`Id · BizId(clustered) · ParentId? · Level(1=Agency 2=Regional 3=HQ) · Code · Name · City ·
InsurerName · IsActive`

**OrgSettings**
`SettlementDeadlineDays(3) · LockScope · DueDateRule · ShiftOnHoliday · MaxInstallments(9) ·
ReminderDaysBefore("7,3,0") · MaxOpenTabs(12) · DefaultServiceFee · ServiceFeeMode(1=fixed 2=percent)`

## 2.2 Users, roles, marketers

`AppUser · Role · UserOrgRole` — role is separate from organization.

Permissions: `Policy.Read` `Policy.Write` `Payment.Write` `Import.Run` `Settings.Write`
`Lock.ForceRelease` `Report.Read` `Finance.Read` `Marketer.Manage` `Marketer.SelfView`

### Marketer — profile and login are separate entities

```
Marketer
  Id · BizId · AgencyId
  FullName · Mobile · NationalId(encrypted)?
  Type          1 = staff, 2 = independent
  AppUserId?    ← optional. Many marketers only receive SMS and never log in.
  IsActive
```

```sql
-- niaz: one marketer, one agency. Enforce in the database, not the UI.
CREATE UNIQUE INDEX UX_Marketer_User ON Marketer (AppUserId)
  WHERE AppUserId IS NOT NULL AND IsDeleted = 0;
```

**MarketerRate** — `MarketerId · InsuranceLineId · RatePercent · EffectiveFrom · EffectiveTo?`
Rates differ per insurance line (niaz #11). Historical rates are kept; never updated in place.

### What a marketer may see — hard limits

| Visible | Hidden |
|---|---|
| Only customers **they introduced** | every other customer |
| Name + mobile of those | national ID · plate · address |
| Policy status (issued / not) | policy financial detail |
| **Their own commission** — earned, paid, pending | agency commission · P&L |
| Renewal dates of their customers | agency installment/overdue lists |
| **Overdue status of their customers** (status only, no amounts) | amounts |

Two non-negotiables:
1. **No export.** No xlsx, no print, no copy-list. Otherwise the customer list walks out of the agency.
2. **Every marketer view is audited**, and the agency owner can see what their marketer looked at.

> Showing overdue status is deliberate: commission is earned per settled installment, so the
> marketer has a direct financial interest in chasing their own customers.

## 2.3 Insurance lines (niaz #1, #10)

```
InsuranceLine
  Id · Code · NameFa · ParentId?      ← self-referencing: مسئولیت has sub-types
  RequiresVehicle bit                 ← ثالث/بدنه yes, آتش‌سوزی no
  RequiresProperty bit
  SortOrder · IsActive
```

Seed: ثالث · بدنه · آتش‌سوزی · مسئولیت (+ children: کارفرما، حرفه‌ای، عمومی) · حوادث · عمر ·
باربری · درمان

**A policy's subject varies by line.** Do not force a `VehicleId` on every policy — nullable, with
a service-layer rule keyed off `RequiresVehicle`.

## 2.4 Customer & subjects

**Customer** — `ExternalCode(join key) · FullName · NationalId(encrypted) · NationalIdHash ·
Mobile · MobileVerifiedAt?`

**No `Notes` column. No free text about a person. Ever.**

**Vehicle** — plate, VIN, chassis, make, model, year. **Attached to the policy, not the customer.**
**PropertySubject** — address, postal code, type, value. For آتش‌سوزی and similar.

## 2.5 Policy — the money fields matter

```
Policy
  Id · BizId · AgencyId
  PolicyNumber · InsuranceLineId · CustomerId
  VehicleId? · PropertySubjectId?
  ContractName · IsInstallment
  IssueDate · StartDate · EndDate          ← StartDate drives due dates

  -- money, in TOMAN, in this exact order (niaz #9)
  NetPremium          حق بیمه              ← commission base
  ServiceFee          کارمزد خدمات          ← agency's own fee, added after premium
  TotalReceivable     = NetPremium + ServiceFee
  DownPayment         پیش‌پرداخت            ← free amount, not a ratio
  FinancedAmount      = TotalReceivable − DownPayment
  InstallmentCount

  AgencyCommissionPercent · AgencyCommissionAmount   ← from the insurer, for P&L
  MarketerId? · MarketerRatePercent                  ← locked at issuance

  Status · ImportBatchId? · PreviousInsurer? · IsRenewal
```

> **Order is a business rule, not a display preference** (niaz #9): premium → service fee →
> down payment → installments. The service fee is part of what the customer owes and is spread
> across installments, **but it is NOT part of the commission base.**

## 2.6 Endorsement — الحاقیه (schema now, UI later)

```
Endorsement
  PolicyId · EndorsementNo · Type · IssueDate
  PremiumDelta        can be negative
  ServiceFeeDelta
  AffectsInstallments bit
  Description         ← about the endorsement, never about a person
```

When `AffectsInstallments`, unsettled installments are recalculated; settled ones are never
touched. Commission is recalculated on the new base for future installments only.

## 2.7 Installment & payment

```
Installment
  PolicyId · SeqNo · DueDate · SettlementDeadline
  Amount · PaidAmount · Status(1=unpaid 2=partial 3=settled)
  RemittedAt? · IsManuallyEdited bit      ← niaz #3
```

**Amount and DueDate are editable** (niaz #3). Every edit: audit row + `IsManuallyEdited` +
commission recalculation for that installment.

```
Payment            Id · AgencyId · CustomerId · Amount · PaidOn · Method · ReferenceNo · RecordedBy
PaymentAllocation  PaymentId · InstallmentId · Amount
```

```sql
CREATE UNIQUE INDEX UX_Payment_Dedupe ON Payments (InstallmentIdHint, PaidOn, Amount)
  WHERE IsDeleted = 0;
```

## 2.8 Commission — per installment (the settled design)

```
CommissionEntry
  Id · AgencyId · MarketerId · PolicyId
  InstallmentId?          null ⇒ this is the down-payment share
  BasePortion             this slice's share of NetPremium
  RatePercent             locked at issuance — later rate changes never affect old entries
  Amount
  Status                  1=pending 2=payable 3=paid 4=forfeited
  EligibleAt?             the moment that installment fully settled
  PaidAt? · PaymentBatchId?
```

**Rules:**
- Base is **NetPremium only** — never `TotalReceivable`. The service fee is the agency's, not the
  marketer's.
- Slices are proportional to **amount**, not count — the final installment differs after rounding.
- **The down payment gets its own slice**, eligible immediately. Forgetting this silently underpays
  every marketer.
- An entry becomes `payable` only on **full** settlement of its installment. Partial is not enough.
- Partial default ⇒ proportional: settled installments pay, unsettled ones never activate.
- No clawback, no deduction from future commissions.

## 2.9 Renewal tracking (niaz #4)

```
RenewalWatch
  Id · AgencyId · CustomerId?
  ProspectName · ProspectMobile           ← walk-in with no policy here yet
  InsuranceLineId · CurrentInsurer
  CurrentExpiryDate
  NotifyDaysBefore        default 2 — "call me 2 days before mine expires"
  MarketerId?
  Status                  1=watching 2=notified 3=converted 4=lost
  PolicyId?               set when it converts
```

Also covers renewals of the agency's own policies: a job creates a watch as `Policy.EndDate`
approaches.

> This is the only part of the product that **creates revenue** rather than reducing loss. Its
> conversion rate is the single best sales argument you will have.

## 2.10 Collateral, import, audit, concurrency

Unchanged from v1 — see `docs/CONCURRENCY.md` and section 4.

`AuditEntry` uses `bigint` identity (not GUID) and carries **`PolicyId` on every row**, which makes
per-file history one indexed lookup.

---

# 3. Core algorithms

## 3.1 Due date
`DueDate(n) = StartDate + n months, same day-of-month`, clamped for short months.
`SettlementDeadline = DueDate + SettlementDeadlineDays`, shifted forward off holidays.
**Shift the deadline, not the due date.**

Verified: `issued 1404/07/21 → due 1405/05/21` · `1404/08/22 → 1405/08/22`

## 3.2 Amounts — order matters

```
TotalReceivable = NetPremium + ServiceFee
Financed        = TotalReceivable − DownPayment
base            = floor(Financed / N)
last            = Financed − base × (N−1)
```

Optional suggestion: pick the down payment that makes `base` round.
Real case: 10,700,000 − 1,700,000 = 9,000,000 ÷ 9 = 1,000,000 exactly.

## 3.3 Settlement countdown

```sql
SELECT ... FROM Installments i JOIN Policies p ON p.Id = i.PolicyId
WHERE i.AgencyId = @AgencyId AND i.Status <> 3
  AND i.SettlementDeadline >= DATEADD(DAY,-30,@Today)
  AND i.DueDate <= DATEADD(DAY,7,@Today)
ORDER BY i.SettlementDeadline, i.Amount DESC
```

Dashboard shows three figures — **the third is the point:**
```
Owed to insurer  = Σ Amount     of installments in an open window
Collected        = Σ PaidAmount of the same
SHORTFALL        = difference    ← largest element on screen, red
```

## 3.4 Payment allocation
Oldest unsettled first. Overflow → next installment (agent may override). Leftover → customer
credit, flagged. **Underpayment is always partial — there are no discounts on insurance
installments.**

On full settlement of an installment → flip its `CommissionEntry` to `payable`.

## 3.5 Commission calculation

```
totalCommission = NetPremium × RatePercent          // NOT TotalReceivable

downShare    = totalCommission × (DownPayment        / TotalReceivable)
instShare(i) = totalCommission × (Installment[i].Amount / TotalReceivable)

// down-payment entry: payable immediately
// each installment entry: pending → payable when that installment fully settles
```

**Guard:** `downShare + Σ instShare` must equal `totalCommission` to the rial. Put the rounding
remainder on the last entry and assert it in a unit test.

## 3.6 Profit & loss (the top-ranked feature)

Per period, per line, per marketer:

```
درآمد
  کارمزد از شرکت بیمه     Σ AgencyCommissionAmount   (issued in period)
  کارمزد خدمات            Σ ServiceFee
  ─────────────────────────────────────────
  جمع درآمد

هزینه
  پورسانت بازاریاب        Σ CommissionEntry.Amount   (paid or payable in period)
  سوخت نکول               Σ unpaid amounts of installments past deadline
  ─────────────────────────────────────────
  جمع هزینه

سود خالص = درآمد − هزینه
```

Two toggles the agent must control, because both are legitimate views:
- **Accrual vs cash** — recognise at issuance, or at collection
- **Default write-off threshold** — after how many days overdue an amount counts as burned

Break down by line, by marketer, by month. Compare periods.

## 3.7 Reminders — read rule 25 in CLAUDE.md

One daily Hangfire job:

```
for each installment due within ReminderDaysBefore:
    if installment.Status == settled: skip     ← THE CHECK. Never remove.
    if already sent (installmentId, offset): skip
    send; log
```

**Never pre-enqueue the sequence.** ~960 installments/month per agency: blind sending costs
561,405 toman/month against 1,000,000 revenue. Conditional costs 260,135.

Manual filtered send (niaz #13): the operator filters by date range, status, line, marketer,
amount → preview count and cost → confirm → send.

Renewal reminders: to the customer, and **to the marketer** if the customer was theirs.

---

# 4. Import

## 4.1 Fanavaran policy report
Sheet `CarSalesBNVer`. Extract: policy no · insured `نام خانوادگی کد 8030987` (parse the code) ·
plate/VIN/chassis · issue & **start** date · duration · **premium with tax (RIALS → ÷10)** ·
**contract name** (strip from «شماره قرارداد» onward, then match templates).

Sample of 180 rows: 60.6% تجارت آفرینان · 15.6% literal اقساطی · 12.8% cash · 9.4% blank
→ **76.1% installment.**

## 4.2 Cartable
Sheet `DebitSrdMoavvaghe`. Only installments already inside the window. **Optional weekly
reconciliation, never a required daily upload.**

## 4.3 Generic mapped import
Upload → preview 20 rows → map columns → save mapping per agency → import.
**This is what makes the product work for non-Fanavaran insurers.** Not optional.

## 4.4 Rules
Preview always (detected currency and date format shown) · report `X new / Y duplicate / Z failed`
with per-row reasons · a failed row never aborts the batch · every row links to its `ImportBatch`.

---

# 5. Screens

| # | Screen | Notes |
|---|---|---|
| 1 | **امروز** | shortfall · countdown queue · pinned |
| 2 | آپلود فایل | drag-drop, preview, mapping, batch result |
| 3 | تنظیم قالب‌های اقساط | contract → template |
| 4 | **ثبت بیمه‌نامه** | **line dropdown FIRST** (niaz #10), then a line-specific form |
| 5 | فهرست بیمه‌نامه‌ها | filter by line, status, marketer |
| 6 | **پروندهٔ مشتری** | **all policies** · timeline · payments · collateral · history (niaz #7) |
| 7 | پروندهٔ بیمه‌نامه | installments · endorsements · commission · history |
| 8 | فهرست اقساط | the queue, filterable, **editable amount/date** |
| 9 | ثبت پرداخت | one-click + batch |
| 10 | **بازاریاب‌ها** | profiles, per-line rates, commission status |
| 11 | **پنل بازاریاب** | restricted view per 2.2 |
| 12 | **سود و زیان** | ⭐ by period / line / marketer |
| 13 | **یادآور تمدید** | watch list, conversion tracking |
| 14 | **ارسال پیامک** | filter → preview count & cost → send |
| 15 | چک‌ها | upcoming, bounced |
| 16 | گزارش‌گیری | filters, xlsx export |
| 17 | تنظیمات | org, users, lines, parameters, templates |

**Issuance form order is fixed** (niaz #9, #10):
```
نوع بیمه‌نامه → مشتری → مورد بیمه → حق بیمه → کارمزد خدمات →
پیش‌پرداخت → تعداد اقساط → جدول اقساط → بازاریاب → وثیقه
```

**Dashboard hierarchy:** SHORTFALL (largest) → countdown queue → today's due / collected /
active → **this month's P&L summary** → upcoming renewals → upcoming cheques → last import.

---

# 6. api.ir

Base `https://s.api.ir` · Bearer · POST/JSON · envelope
`{ data, success, code, message }` — **check `success`, never `data` alone.**

| Service | Endpoint | Toman | Cache |
|---|---|---:|---|
| ShahkarLite | `/api/sw1/ShahkarLite` | 550 | forever |
| SendSms | `/api/sw1/SendSms` | 115 | — |
| SmsOTP | `/api/sw1/SmsOTP` | 115 | — |
| IsHoliday | `/api/sw1/IsHoliday` | 150 | to midnight |
| ChequeColor | `/api/sw1/ChequeColor` | 1,100 | 30 days |
| CallOTP | `/api/sw1/CallOTP` | 95 | — |

**Develop against `/api/Sandbox/Echo`.** Paid endpoints require an explicit config flag.

---

# 7. Seed data

2 agencies (**to prove RLS isolates them**) · 4 users · 3 marketers (1 staff, 2 independent, one
with a login) · all insurance lines · ~200 policies across lines, 76% installment · installments
past and future, some partial, some overdue · commission entries in every status · cheques in
every state · a few renewal watches.

**All fake.** The real uploaded files contain real names and plates — never use them as seed data.

---

# 8. Not in Phase 1

Credit-check APIs · shared network · customer portal · endorsement management UI · regional/HQ
dashboards · e-signature · legal notices · mobile app · general ledger · network-average comparison
