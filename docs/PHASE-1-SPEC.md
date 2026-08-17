# مشخصات فاز ۱ — Phase 1 Specification

**Scope:** what ships. Anything not listed here is out of scope for Phase 1.

---

# 1. Scope

## In

| # | Capability | Why |
|---|---|---|
| 1 | Import Fanavaran policy report (xlsx) | Replaces manual entry |
| 2 | Installment templates + contract mapping | 80% of records depend on this |
| 3 | Auto-generate installment schedule | start + N months, same day |
| 4 | **Settlement countdown** | The product's reason to exist |
| 5 | Payment recording (one-click, partial, overpay) | The daily work |
| 6 | Cheque/collateral tracking | Explicitly requested by agents |
| 7 | SMS reminders with payment link | Highest-impact feature |
| 8 | Reports with filters | Explicitly requested |
| 9 | Per-file audit trail | Sells to multi-staff agencies |
| 10 | MDI tab shell + sidebar | Foundation, must be first |

## Out (later phases)

Credit checks · shared network · customer self-registration portal · regional/HQ views · legal
notice generation · mobile app · full accounting

## Blocked (build behind an interface, mock until unblocked)

| Dependency | Blocked on |
|---|---|
| SMS sending | api.ir account + access level |
| Shahkar mobile verification | same |
| Payment gateway | company registration + enamad |

**Define `ISmsSender`, `IIdentityVerifier`, `IPaymentGateway` now with logging fake
implementations.** Development must not stall.

---

# 2. Data model

## Organization (self-referencing — supports all three levels from day one)

| Column | Type | Notes |
|---|---|---|
| Id | uniqueidentifier | PK, non-clustered |
| BizId | bigint identity | **clustered index** |
| ParentId | uniqueidentifier? | FK → Organization |
| Level | tinyint | 1=Agency 2=Regional 3=HQ |
| Code | nvarchar(20) | e.g. "2491" |
| Name, City | nvarchar | |
| InsurerName | nvarchar(80) | which insurance company |
| IsActive | bit | |

## OrgSettings (operational parameters — never constants in code)

| Column | Type | Default |
|---|---|---|
| OrganizationId | uniqueidentifier | PK/FK |
| SettlementDeadlineDays | int | 3 |
| LockScope | tinyint | 1=full 2=installment-only |
| DueDateRule | tinyint | 1 = start + N months same day |
| ShiftOnHoliday | bit | 1 |
| MaxInstallments | int | 9 (authority ceiling) |
| ReminderDaysBefore | nvarchar(40) | "7,3,0" |
| MaxOpenTabs | int | 12 |

## AppUser / Role / UserOrgRole

A user may hold different roles in different organizations. **Role is separate from organization.**

Permissions needed in Phase 1:
`Policy.Read` `Policy.Write` `Payment.Write` `Import.Run` `Settings.Write`
`Lock.ForceRelease` `Report.Read`

## Customer

| Column | Type | Notes |
|---|---|---|
| Id, BizId, AgencyId | | |
| ExternalCode | nvarchar(30) | Fanavaran internal code — **the join key in Phase 1** |
| FullName | nvarchar(120) | |
| NationalId | varbinary | **encrypted**; nullable — not in exports |
| NationalIdHash | binary(32) | for lookup without decryption |
| Mobile | nvarchar(15) | nullable |
| MobileVerifiedAt | datetimeoffset? | Shahkar |

**No `Notes` column. No free text about a person. Ever.**

## Vehicle

Plate, VIN, chassis, make/model, year. **Belongs to the policy, not the customer** — vehicles change
hands and the new owner is innocent.

## ContractTemplate (the 80% problem)

| Column | Type | Notes |
|---|---|---|
| AgencyId | | |
| ContractNamePattern | nvarchar(200) | e.g. "تجارت آفرینان تسنیم" |
| IsInstallment | bit | |
| DefaultInstallmentCount | int | |
| SuggestedDownPaymentPercent | decimal(5,2)? | hint only |

Matching is **prefix/contains on the contract column**, configured by the agency. Never search for
the word «اقساطی».

## Policy

| Column | Type | Notes |
|---|---|---|
| Id, BizId, AgencyId | | |
| PolicyNumber | nvarchar(40) | |
| CustomerId, VehicleId | FK | |
| ContractName | nvarchar(200) | raw, as imported |
| IsInstallment | bit | resolved via template |
| IssueDate, StartDate, EndDate | date | **StartDate drives due dates** |
| TotalPremium | decimal(18,0) | **toman** |
| DownPayment | decimal(18,0) | free amount |
| InstallmentCount | int | |
| Status | tinyint | 1=active 2=settled 3=cancelled |
| ImportBatchId | FK? | provenance |

## Installment

| Column | Type | Notes |
|---|---|---|
| PolicyId | FK | |
| SeqNo | int | 1..N |
| DueDate | date | computed |
| SettlementDeadline | date | DueDate + deadline, holiday-shifted |
| Amount | decimal(18,0) | |
| PaidAmount | decimal(18,0) | sum of allocations |
| Status | tinyint | 1=unpaid 2=partial 3=settled |
| RemittedAt | datetimeoffset? | paid to insurer |

Computed: `Balance = Amount - PaidAmount` · `DaysToDeadline = SettlementDeadline - today`

## Payment + PaymentAllocation

A payment is an event. Allocations distribute it across installments.

```
Payment { Id, AgencyId, CustomerId, Amount, PaidOn, Method, ReferenceNo, RecordedByUserId }
PaymentAllocation { PaymentId, InstallmentId, Amount }
```

```sql
CREATE UNIQUE INDEX UX_Payment_Dedupe ON Payments (InstallmentIdHint, PaidOn, Amount)
  WHERE IsDeleted = 0;
```

## Collateral

Type (1=cheque صیادی, 2=سفته, 3=none), SayadId, BankName, Amount, DueDate,
Status (held / at-bank / cleared / bounced), ColorCode + CheckedAt (later phase).

## ImportBatch + ImportRow

Every import is recorded: file name, hash, row counts (new / duplicate / failed), errors per row.
**Duplicate detection key: PolicyNumber + SeqNo.**

## AuditEntry

| Column | Type |
|---|---|
| Id | bigint identity — **not GUID**, this table is huge |
| AgencyId, UserId, UserDisplayName | |
| EntityType, EntityId | |
| **PolicyId** | **always populated — the per-file history key** |
| Action | tinyint |
| Description | nvarchar(400) — composed at write time |
| ChangesJson | nvarchar(max)? — **never for encrypted fields** |
| OccurredAt, IpAddress | |

```sql
CREATE INDEX IX_Audit_Policy ON AuditEntry (PolicyId, OccurredAt DESC);
```
Partition monthly. Archive after 12 months.

## RecordPresence / RecordLock

See `docs/CONCURRENCY.md`.

---

# 3. Core algorithms

## 3.1 Due date

```
DueDate(n) = StartDate + n months, same day-of-month
```

Clamp overflow (31st → 30th/29th). If `ShiftOnHoliday`, move `SettlementDeadline` to the next
working day — **shift the deadline, not the due date.**

Verified against real cartable data:
`issued 1404/07/21 → due 1405/05/21` · `issued 1404/08/22 → due 1405/08/22`

## 3.2 Installment amounts

```
financed = TotalPremium - DownPayment
base     = floor(financed / N)
last     = financed - base * (N - 1)      // rounding lands on the final installment
```

Down-payment suggestion (optional, never enforced): pick the down payment that makes `base` land on
a round number. Real example: premium 10,700,000 − down 1,700,000 = 9,000,000 ÷ 9 = 1,000,000.

## 3.3 Settlement countdown — the core query

```sql
SELECT ...
FROM Installments i
JOIN Policies p ON p.Id = i.PolicyId
WHERE i.AgencyId = @AgencyId              -- RLS also enforces this
  AND i.Status <> 3
  AND i.SettlementDeadline >= DATEADD(DAY, -30, @Today)
  AND i.DueDate <= DATEADD(DAY, 7, @Today)
ORDER BY i.SettlementDeadline, i.Amount DESC;
```

Urgency: `overdue` (deadline passed, unsettled) · `critical` (0–1 days) · `warning` (2–3) ·
`upcoming` (due but window not open) · `future`.

**The dashboard shows two separate figures plus the gap:**

```
Owed to insurer     = Σ Amount     of installments in an open window
Collected           = Σ PaidAmount of the same
SHORTFALL           = the difference    ← large, red; this is what comes out of the agent's pocket
```

## 3.4 Payment allocation

```
remaining = payment.Amount
for inst in unsettled installments of this customer, ordered by DueDate:
    take = min(remaining, inst.Amount - inst.PaidAmount)
    allocate(take); remaining -= take
    if remaining == 0: break
if remaining > 0:
    park as credit on the customer, flag for agent review
```

Agent may override the allocation. **Underpayment is always partial — never a discount.**

## 3.5 Reminder scheduling — read rule 25 in CLAUDE.md

A single Hangfire job runs daily:

```
for each installment due in ReminderDaysBefore:
    if installment.Status == settled: skip        ← THE CHECK. Do not remove.
    if already sent for (installmentId, offset): skip
    send; record
```

**Never enqueue the whole sequence in advance.**

---

# 4. Import pipeline

## 4.1 Policy report (`گزارش_2.xlsx`)

Sheet `CarSalesBNVer`, header row 1, ~190 columns. Needed:

| Field | Note |
|---|---|
| Policy number | |
| Insured name + code | format: `نام خانوادگی کد 8030987` — parse the code out |
| Plate, VIN, chassis | |
| Issue date, **start date**, duration | start date drives everything |
| **Total premium with tax** | **RIALS → ÷10** |
| **Contract name** | strip everything from «شماره قرارداد» onward, then match templates |

Real distribution from the sample (180 rows): 60.6% تجارت آفرینان · 15.6% literal اقساطی ·
12.8% cash · 9.4% blank. **76.1% are installment policies.**

## 4.2 Cartable (`گزارش_سررسید_اقساط_جهت_پرداخت.xlsx`)

Sheet `DebitSrdMoavvaghe`. Contains only installments already inside the deadline window.
Columns: policy number, insured name + code, issue date, due date, **days remaining (0–3)**,
amount (**rials**).

**Role: optional weekly reconciliation, not the primary input.** Never require daily upload.
Reconciliation output: "3 items appear in the cartable but are marked paid here — review."

## 4.3 Generic mapped import

Upload → preview first 20 rows → user maps columns → save mapping per agency → import.
**This is what makes the product work for non-Fanavaran insurers.** Not optional.

## 4.4 Import rules

- Preview before commit — always. Show detected currency, date format, row counts.
- Report `X new / Y duplicate / Z failed`, with per-row reasons.
- A failed row never aborts the batch.
- Every imported row links to its `ImportBatch`.

---

# 5. Screens (Phase 1)

Build the shell first (Task 1), then these in order:

| # | Screen | Key content |
|---|---|---|
| 1 | **امروز** | shortfall figure · countdown queue · pinned |
| 2 | آپلود فایل | drag-drop, preview, column mapping, batch result |
| 3 | تنظیم قالب‌های اقساط | contract → template mapping |
| 4 | فهرست بیمه‌نامه‌ها | filter, search, paging |
| 5 | **پروندهٔ بیمه‌نامه** | installment timeline · payments · collateral · **history** |
| 6 | ثبت بیمه‌نامه (دستی) | multi-instance tab |
| 7 | فهرست اقساط | the settlement queue, filterable |
| 8 | ثبت پرداخت | one-click from a row + batch mode |
| 9 | چک‌ها | upcoming, bounced, status |
| 10 | گزارش‌گیری | date range, type, export xlsx |
| 11 | تنظیمات | org, users, parameters, templates |

## Dashboard hierarchy (top to bottom)

1. **SHORTFALL** — largest element on the screen
2. Countdown queue, sorted by urgency
3. Today's due / collected this month / active installment policies
4. Upcoming cheques
5. Last import status

---

# 6. api.ir integration

Base `https://s.api.ir` · `Authorization: Bearer {key}` · POST + JSON.

**All responses share one envelope:**
```json
{ "data": ..., "success": bool, "code": int, "message": "string|null" }
```

```csharp
if (!response.Success)
    throw new ApiIrException(response.Code, response.Message);   // never trust data alone
```

| Service | Endpoint | Cost (toman) | Cache |
|---|---|---:|---|
| ShahkarLite | `/api/sw1/ShahkarLite` | 550 | forever |
| SendSms | `/api/sw1/SendSms` | 115 | — |
| SmsOTP | `/api/sw1/SmsOTP` | 115 | — |
| IsHoliday | `/api/sw1/IsHoliday` | 150 | until midnight |
| ChequeColor | `/api/sw1/ChequeColor` | 1,100 | 30 days |
| CallOTP (fallback) | `/api/sw1/CallOTP` | 95 | — |

**Develop against `/api/Sandbox/Echo`.** Never hit a paid endpoint in dev or test.

Every call: log request/response (redacted), retry with backoff on 5xx, circuit-break after
repeated failure, and record cost per call for reporting.

---

# 7. Seed data

Ship a seed script producing:
- 2 agencies (**to prove RLS actually isolates them**)
- 4 users across roles
- ~200 policies, 76% installment, mixed contract names
- installments spread across past and future, some partial, some overdue
- a few cheques in each state

**All fake.** Never use the real uploaded files as seed data — they contain real names and plates.

---

# 8. Explicitly NOT in Phase 1

Credit check APIs · shared credit network · customer portal · e-signature · regional/HQ dashboards ·
legal notice templates · mobile app · accounting ledger · network-average comparison
