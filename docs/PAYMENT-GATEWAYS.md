# Payment Gateways

How this system collects money through a real PSP. Three code references depend on this document,
including its section numbering: `GooyaPayOptions` and `GooyaPayPaymentGateway` both point at §4
for live-merchant testing, and `AppDbContext` points at this file for the `GatewayTransactions`
table. Renumbering the sections below silently breaks those references.

Providers are selected at runtime through `PaymentProvider` (`src/Aqsat.Domain/Enums/PaymentProvider.cs`:
`Mock = 1`, `ZarinPal = 2`, `GooyaPay = 3`, serialized as strings on the API) — CLAUDE.md's
"no hard-coded vendor" rule: the service layer only ever sees `IPaymentGateway`.

## §1. The two-phase contract

No Iranian PSP can be charged in one call: the customer pays on the PSP's own page and their
browser returns to us, so every real charge is two phases with an anonymous callback in between
(`src/Aqsat.Application/Payments/IPaymentGateway.cs`):

1. **Request** — `RequestAsync(GatewayChargeRequest)` posts the charge (amount in **toman**, the
   system's single internal unit, rule 19) and returns either an inline settlement (Mock: no
   redirect, money "already accounted for") or `RequiresRedirect` + a `GatewayReference`
   (the PSP's Authority) + a `RedirectUrl`.
2. **Redirect** — the customer's browser leaves for the PSP. Nothing is settled yet.
3. **Callback** — the PSP calls back one of the three endpoints in §6 with only its own reference.
   `GatewayPaymentCoordinator.ResolveCallbackTransactionAsync` matches that reference to the
   Pending `GatewayTransaction` (§5). The callback's success flag is attacker-controllable and
   only decides whether a verify is *worth attempting*.
4. **Verify** — `VerifyAsync(reference, merchantId, amountToman)` is a server-to-server call and
   the ONLY evidence that may produce a `Payment` row. The amount and merchant id come from the
   Pending row, never from the callback.

`GatewayPaymentCoordinator` (`src/Aqsat.Infrastructure/Payments/GatewayPaymentCoordinator.cs`) is
the one place this flow lives, so the three portal paths (inquiry fee, down payment, installment)
cannot drift apart. On phase one it enforces each PSP's amount bounds BEFORE any HTTP request —
an out-of-range amount otherwise surfaces as an opaque PSP rejection — then records the Pending
row **before the browser leaves** (if the process dies mid-payment, the PSP's callback retry must
find the reference), superseding this subject's earlier Pending rows so an abandoned authority can
never be replayed into a settlement. Pending rows expire after 30 minutes (ZarinPal's own reverse
window), lazily on read.

Adding a fourth PSP = one `IPaymentGateway` implementation + one DI block (§3). Nothing else.

## §2. Providers at a glance

| | Mock | ZarinPal (زرینپال) | GooyaPay (گویا پی) |
|---|---|---|---|
| Purpose | dev/test, zero-config fresh installs | real PSP | real PSP |
| Amount range (toman) | 0 … `decimal.MaxValue` | 1,000 … 100,000,000 | 1,000 … 200,000,000 |
| Unit sent | n/a | `amount` + explicit `currency: "IRT"` on **both** request and verify | integer toman, no currency field |
| Credential | none | `merchant_id` in JSON body | `MerchantID` in JSON body (is the credential — never logged) |
| Sandbox | n/a | yes — separate test host via `ZarinPal:BaseUrl` | **none** — live host only |
| Verify success | always | code `100`, also `101` (already verified) = success | `Status` `100` |
| Callback fields | never arrives | `Status` + `Authority` (GET), lower-case aliases accepted | `PaymentStatus=OK` + `Authority` (+`InvoiceID`); NOK = failure claim |
| Redirect phase | none (inline) | `/pg/StartPay/{authority}` | response `PaymentUrl`, fallback `{BaseUrl}/startPay/{authority}` |

Wire details both implementations get wrong at their peril, each guarded by a contract test (§7):

- **ZarinPal** sends `currency: "IRT"` explicitly everywhere; omitting it invites a silent
  factor-of-ten unit bug. Its failure envelope is polymorphic — `errors` is an object on failure
  and an array on success, `data` the reverse — so it is read from a `JsonDocument`, not a POCO.
  Its published error table (-9 … -55) is transcribed verbatim into Persian operator messages in
  `ZarinPalPaymentGateway.DescribeFailure`. Verify code `101` ("already verified") is SUCCESS:
  a retried callback must not turn a real payment into an error (rule 24).
- **GooyaPay** sends no `RequestMethod` field (the callback therefore arrives as POST), accepts
  only whole toman, and enforces its published 1,000–200,000,000 bounds both here and pre-flight
  in the coordinator. Its negative status codes are deliberately NOT transcribed — the numeric
  code is surfaced verbatim rather than a guessed meaning. Verify `RefID` is read with a
  string-or-number converter; `MaskCardNumber`/`Amount`/`BuyerIP` are persisted on the row (§5).
- **Mock** resolves phase one inline (`RequiresRedirect: false`, `SimulatedPaidAmountToman` set)
  so a fresh install completes the whole portal flow before any PSP account exists. This is a
  hard requirement: the full test suite and first-run experience depend on it. The three portal
  services skip callback-base resolution entirely when the provider is Mock.

## §3. Configuration

Endpoints live in `appsettings.json` (`src/Aqsat.Api/appsettings.json`), shaped like `ApiIr` —
one base URL plus paths, so pointing at a test host is always configuration, never code:

```json
"GooyaPay": { "BaseUrl": "https://gooyapay.ir", "PaymentRequestPath": "/webservice/rest/PaymentRequest",
              "PaymentVerificationPath": "/webservice/rest/PaymentVerification",
              "StartPayPath": "/startPay", "TimeoutSeconds": 30 },
"ZarinPal": { "BaseUrl": "https://payment.zarinpal.com", "RequestPath": "/pg/v4/payment/request.json",
              "VerifyPath": "/pg/v4/payment/verify.json", "StartPayPath": "/pg/StartPay",
              "TimeoutSeconds": 30 }
```

Neither options class holds a credential — merchant ids are per-owner/per-agency data in the DB:

| Setting | Where | Used by |
|---|---|---|
| `Provider` + `Enabled` + `OwnerMerchantId` + `CallbackBaseUrl` + `InquiryFeeToman` | `PlatformPaymentSettings` (owner panel: «تنظیمات درگاه پرداخت مالک») | inquiry fee (کارمزد استعلام) — the OWNER's gateway |
| `PaymentProvider` + `AgentMerchantId` | `OrgSettings` (agency panel: «تنظیمات درگاه پرداخت») | down payment + installments — the AGENCY's gateway |

Both merchant-id fields are write-only over the API (GET returns a mask only). Both panels offer
`Mock`/`ZarinPal`/`GooyaPay`; `Mock` is the default everywhere.

**Callback base URL** — `GatewayPaymentCoordinator.ResolveCallbackBaseAsync` resolves
`PlatformPaymentSettings.CallbackBaseUrl` first (one API deployment serves every agency, so the
owner sets it once), falling back to config `Portal:ApiPublicBaseUrl` for local development, and
throws a Persian operator-facing error if neither exists. A blank `CallbackURL` would otherwise
fail at the PSP — at the customer's expense. The three portal services call this only for a real
PSP; Mock skips it (zero-config rule).

**DI** (`src/Aqsat.Infrastructure/DependencyInjection.cs`, ~lines 115–150): Mock registered first
(no credential), then each real PSP as a typed `HttpClient` (BaseAddress/Timeout from its options
section + `AddStandardResilienceHandler`) re-registered behind `IPaymentGateway`, so services see
one `IEnumerable<IPaymentGateway>` and pick by `Provider`. Wiring a new PSP is one block there.

## §4. Real-gateway smoke testing

Referenced by `GooyaPayOptions` and `GooyaPayPaymentGateway` — this is the procedure for
exercising a real PSP end to end, which unit tests (§7) cannot cover because they stop at the
HTTP boundary.

**Preconditions (both PSPs):**

1. `CallbackBaseUrl` in the owner's payment settings points at a PUBLICLY reachable HTTPS origin
   (use a tunnel for local dev). The PSP must be able to POST/GET back to §6's endpoints.
2. The chosen panel (owner for the fee, agency for down payment/installments) has
   `Provider`/`PaymentProvider` = the PSP, `Enabled` = true (fee flow), and the merchant id saved.
3. The test agency/customer exists with an amount inside §2's bounds (both floors: 1,000 toman).

**ZarinPal — sandbox (no money moves):** ZarinPal documents a separate test host; override
`ZarinPal:BaseUrl` to it (env var `ZarinPal__BaseUrl` or appsettings override) with a sandbox
merchant id. Run one payment per flow (fee, down payment, installment): expect redirect →
sandbox pay page → callback → `GatewayTransaction` reaching `Verified` with a `RefId`, a
`Payment` row in the same DB transaction, and the stage/ledger change applied. Replaying the
callback must be a no-op (101-as-success path). Restore `BaseUrl` afterwards.

**GooyaPay — NO sandbox exists** (its docs publish only the live host), so this needs a REAL
MerchantID and moves real money: use the smallest meaningful amount (≥ 1,000 toman) against one
flow at a time. Same expected observations as above; on the PSP side check `MaskCardNumber` and
`RefID` landed on the row. Verify failures here are safe-to-retry by design: a failed verify
leaves the row `Pending`/`Failed`, never a duplicate `Payment`.

**Pass criteria:** each phase-one produces exactly one Pending row (older ones `Expired`);
callback without a matching row yields the Persian 400, not an exception; verified payment
produces exactly one `Payment` + one `AuditEntry`; amounts reconcile to the toman figure on the
row; nothing crosses agency scope (RLS).

## §5. `GatewayTransactions` — persistence & lifecycle

Migration `20260922121908_AddGatewayTransactions`; entity
`src/Aqsat.Domain/Payments/GatewayTransaction.cs`; `AppDbContext` points here. One row per
real-PSP charge attempt (request → redirect → callback → verify). It exists because the callback
is anonymous and carries only the gateway's reference — without this row, finalising would have
to trust whatever amount the (untrusted) request contained.

- `AgencyOwnedEntity` → covered by `AgencyAccessPolicy` RLS like every other money-bearing
  table; the anonymous callback resolves it *inside* the agency scope the token fixed.
- Key fields: `Provider`, `Purpose` (`InquiryFee`/`DownPayment`/`Installment` — tells the
  callback what to finalise), `GatewayReference` (unique per provider — the lookup key),
  `MerchantIdUsed` (verify must replay it; both PSPs reject a verify by a non-owning merchant),
  `AmountToman` (re-sent at verify; required to match), `Status`, subject ids
  (`InvitationId`/`InstallmentId`/`PolicyId`/`CustomerId`), `ExpiresAtUtc` (+30 min).
- Statuses (`GatewayTransactionStatus`): `Pending` (request accepted — not yet money) →
  `Verified` (server-side verify confirmed — the ONLY status that may produce a `Payment`, and
  the business action commits in the SAME SaveChanges as the flip) | `Failed` (kept, never
  deleted, so a repeated callback re-reads the same decision) | `Expired` (customer didn't
  finish in the window; applied lazily; also applied to superseded attempts on re-charge).
- Deliberately NOT `IAuditableEntity`: the money movement writes `AuditAction.PaymentRecorded`
  in the same transaction as the `Payment`; the generic audit path would double-log it.
- Verify evidence persisted on the row: `RefId`, masked card, buyer IP — the only way to
  reconcile against the PSP's panel later.

## §6. Callback endpoints

`PortalPublicController` (`[Route("api/portal")]`, anonymous, rate-limited, no-store). Each is
GET **and** POST — ZarinPal returns via GET, GooyaPay POSTs form data — and merges
**query string over form body** into one dictionary (`ReadCallbackParameters`) before handing it
to the provider's own `ReadCallback`:

| Flow | Endpoint |
|---|---|
| Inquiry fee (owner gateway) | `GET/POST /api/portal/{token}/callback` |
| Down payment (agency gateway) | `GET/POST /api/portal/{token}/pay-down-payment/callback` |
| Installment (agency gateway) | `GET/POST /api/portal/pay/{token}/installments/{installmentId:guid}/callback` |

The full callback URL registered with the PSP is
`{CallbackBaseUrl}/api/portal/{token}/callback` etc. Field-name knowledge (GooyaPay's
`PaymentStatus`/`Authority`/`InvoiceID` incl. lower-case aliases; ZarinPal's `Status`/`Authority`)
lives ONLY behind `IPaymentGateway.ReadCallback`. `ResolveCallbackTransactionAsync` tries every
registered gateway's reader against the DB and requires the row's own `Provider` to agree — a
cross-provider authority string is skipped, never trusted. The provider is never taken from the
callback parameters themselves (attacker-controllable).

## §7. Tests

`tests/Aqsat.UnitTests/Payments/GatewayContractTests.cs` (19 tests) pins the wire contracts:
GooyaPay floor/fractional refusal, request-body schema (including absence of `RequestMethod`),
rejection codes, numeric+string `RefID` verify, zero-amount fallback, callback lower-case
aliases + NOK; ZarinPal explicit IRT currency, 100M cap, code 101 = success, both envelope
shapes, callback OK/NOK; coordinator unregistered-provider rejection and out-of-range amount
refused before any HTTP call. The full unit suite must stay green (461 at last validation) —
it is what proves the Mock zero-config contract (§2) still holds. Run with a test database via
`AQSAT_TEST_CONNECTION`.
