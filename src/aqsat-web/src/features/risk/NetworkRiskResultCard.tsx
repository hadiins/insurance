import { useState } from "react";
import * as Dialog from "@radix-ui/react-dialog";
import { fa } from "../../lib/persian";
import { toJalaliDateTimeDisplay } from "../../lib/jalali";
import { RISK_DECISION_STYLES, RISK_LEVEL_STYLES, type NetworkRiskResultDto, type RiskLevelKey } from "./riskTypes";

const STAT_TILE = "rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2.5";

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div className={STAT_TILE}>
      <div className="text-[11px] text-(--ice-3)">{label}</div>
      <div className="mt-1 text-[14px] font-bold tabular-nums text-(--ice)">{value}</div>
    </div>
  );
}

/** One agency's status row for the queried person (Phase 2B-1). Status only by design (owner
 * decision 2026-09-07): no amounts, no identity — shared by the «استعلام شبکه‌ای» page and the
 * wizard's details dialog. */
export function NetworkRiskResultCard({ result }: { result: NetworkRiskResultDto }) {
  const level = RISK_LEVEL_STYLES[result.riskLevelKey] ?? RISK_LEVEL_STYLES.Medium;
  const decision = RISK_DECISION_STYLES[result.decisionKey] ?? RISK_DECISION_STYLES.ManualReview;

  return (
    <div className="rounded-[12px] border border-(--edge-2) bg-(--card) p-3.5">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <b className="text-[13.5px] text-(--ice)">{result.agencyName}</b>
          {result.insurerName && <span className="text-[11.5px] text-(--ice-3)">— {result.insurerName}</span>}
          {result.isOwnAgency && (
            <span className="rounded-full border border-(--mint)/40 bg-(--mint)/15 px-2 py-0.5 text-[10.5px] font-semibold text-(--mint)">
              دفتر خودتان
            </span>
          )}
        </div>
        <span className="text-[11px] text-(--ice-3)">{toJalaliDateTimeDisplay(result.calculatedAtUtc)}</span>
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-2">
        <span className="rounded-full border border-(--edge-2) bg-(--fld) px-2.5 py-1 text-[12px] font-bold tabular-nums text-(--ice)">
          امتیاز {fa(result.score)}
        </span>
        <span className={`rounded-full border px-2.5 py-1 text-[11.5px] font-semibold ${level.chip}`}>
          سطح ریسک {level.label}
        </span>
        <span className={`rounded-full border px-2.5 py-1 text-[11.5px] font-semibold ${decision.chip}`}>
          {decision.label}
        </span>
      </div>

      <div className="mt-3 grid grid-cols-2 gap-2 sm:grid-cols-3">
        <Stat label="اقساط معوق" value={fa(result.overdueCount)} />
        <Stat label="بیشترین تأخیر (روز)" value={fa(result.maxDaysOverdue)} />
        <Stat label="چک برگشتی" value={fa(result.returnedChequeCount)} />
        {/* No settled installments means the stored 0% is "no data", not "never on time". */}
        <Stat
          label="نرخ وصول به‌موقع"
          value={result.settledInstallmentCount === 0 ? "—" : `${fa(result.onTimeRatePercent)}٪`}
        />
        <Stat label="سابقه (ماه)" value={fa(result.tenureMonths)} />
        <Stat label="اقساط تسویه‌شده" value={fa(result.settledInstallmentCount)} />
      </div>
    </div>
  );
}

const RISK_LEVEL_ORDER: Record<RiskLevelKey, number> = { VeryLow: 0, Low: 1, Medium: 2, High: 3, Critical: 4 };

/** The wizard's one-line take on the network history (Phase 2B-1): a customer with policies at
 * many agencies produces one card per agency, and past a handful those cards bury the issuance
 * form they sit above. So step 1 renders just the count plus the worst level, and the full cards
 * live behind this dialog. */
export function NetworkRiskWizardSummary({ results }: { results: NetworkRiskResultDto[] }) {
  const [open, setOpen] = useState(false);
  const worst = results.reduce(
    (acc, r) => (acc === null || RISK_LEVEL_ORDER[r.riskLevelKey] > RISK_LEVEL_ORDER[acc.riskLevelKey] ? r : acc),
    null as NetworkRiskResultDto | null,
  );
  const worstLevel = worst ? (RISK_LEVEL_STYLES[worst.riskLevelKey] ?? RISK_LEVEL_STYLES.Medium) : null;

  return (
    <div className="flex flex-wrap items-center justify-between gap-2">
      <div className="flex flex-wrap items-center gap-2 text-[11.5px] text-(--ice-3)">
        سابقهٔ شبکه‌ای: <b className="tabular-nums text-(--ice)">{fa(results.length)} نمایندگی</b>
        {worstLevel && (
          <span className={`rounded-full border px-2 py-0.5 text-[11px] font-semibold ${worstLevel.chip}`}>
            بدترین سطح {worstLevel.label}
          </span>
        )}
      </div>
      <Dialog.Root open={open} onOpenChange={setOpen}>
        <Dialog.Trigger asChild>
          <button
            type="button"
            className="rounded-[8px] border border-(--edge-2) bg-(--btn-bg) px-3 py-1 text-[11.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
          >
            نمایش جزئیات
          </button>
        </Dialog.Trigger>
        <Dialog.Portal>
          <Dialog.Overlay className="fixed inset-0 z-[60] bg-black/55" />
          <Dialog.Content className="fixed inset-0 z-[60] grid place-items-center p-5">
            <div className="flex max-h-[85vh] w-full max-w-[640px] flex-col rounded-2xl border border-(--edge-2) bg-(--slate) p-5.5 shadow-[var(--sh)]">
              <Dialog.Title className="mb-3 text-[14.5px] font-bold text-(--ice)">
                سابقهٔ شبکه‌ای این مشتری
              </Dialog.Title>
              <div className="flex-1 space-y-2.5 overflow-y-auto pe-1">
                {results.map((r) => (
                  <NetworkRiskResultCard key={`${r.agencyName}-${r.calculatedAtUtc}`} result={r} />
                ))}
                <div className="text-[11px] text-(--ice-3)">
                  بر اساس تصمیم مالک پلتفرم، در استعلام شبکه‌ای فقط وضعیت (امتیاز، معوقات و سابقهٔ پرداخت)
                  نمایش داده می‌شود؛ هیچ مبلغ یا اطلاعات هویتی با نمایندگی‌های دیگر به اشتراک گذاشته نمی‌شود.
                </div>
              </div>
              <Dialog.Close asChild>
                <button
                  type="button"
                  className="mt-3 self-start rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-1.5 text-[12px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
                >
                  بستن
                </button>
              </Dialog.Close>
            </div>
          </Dialog.Content>
        </Dialog.Portal>
      </Dialog.Root>
    </div>
  );
}

/** The page-level wrapper around one lookup: disabled-switch notice, error, empty, and the
 * result list with the status-only disclaimer. */
export function NetworkRiskResults({
  data,
  isPending,
  error,
  onRetry,
}: {
  data: import("./riskTypes").NetworkRiskLookupDto | undefined;
  isPending: boolean;
  error: string | null;
  onRetry: () => void;
}) {
  if (isPending) {
    return <div className="text-[12.5px] text-(--ice-3)">در حال استعلام از شبکهٔ نمایندگی‌ها…</div>;
  }

  if (error) {
    return (
      <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
        {error}
        <button type="button" onClick={onRetry} className="ms-2 underline underline-offset-2">
          تلاش مجدد
        </button>
      </div>
    );
  }

  if (!data) {
    return null;
  }

  if (!data.isEnabled) {
    return (
      <div className="rounded-[10px] border border-(--amber)/30 bg-(--amber)/10 px-3 py-2.5 text-[12.5px] text-(--amber)">
        استعلام شبکه‌ای ریسک فعلاً توسط مالک پلتفرم غیرفعال شده است. نتیجه‌ای از نمایندگی‌های دیگر نمایش داده
        نمی‌شود.
      </div>
    );
  }

  if (data.results.length === 0) {
    return (
      <div className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2.5 text-[12.5px] text-(--ice-3)">
        سابقه‌ای برای این {data.queryKind === "plate" ? "پلاک" : "کد ملی"} در شبکهٔ نمایندگی‌ها ثبت نشده است.
      </div>
    );
  }

  return (
    <div className="space-y-2.5">
      {data.results.map((r) => (
        <NetworkRiskResultCard key={`${r.agencyName}-${r.calculatedAtUtc}`} result={r} />
      ))}
      <div className="text-[11px] text-(--ice-3)">
        بر اساس تصمیم مالک پلتفرم، در استعلام شبکه‌ای فقط وضعیت (امتیاز، معوقات و سابقهٔ پرداخت)
        نمایش داده می‌شود؛ هیچ مبلغ یا اطلاعات هویتی با نمایندگی‌های دیگر به اشتراک گذاشته نمی‌شود.
      </div>
    </div>
  );
}
