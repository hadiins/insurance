import { useState } from "react";
import { ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDateTimeDisplay } from "../../lib/jalali";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";
import { useAssessCustomer, useCreditLimit, useCustomerRisk, useRiskHistory, useSetCreditLimit } from "./riskApi";
import { RISK_DECISION_STYLES, RISK_LEVEL_STYLES, type RiskAssessmentDto } from "./riskTypes";

const BTN_PRIMARY =
  "rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50";

const BTN_SECONDARY =
  "rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)";

const INPUT_CLASS =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)";

/** The whole «اعتبار و ریسک» view of one customer (docs Phase 2A §7/§25/§26) — used both by the
 * customer file's tab and the standalone assessment page. Loading / error / empty states are all
 * explicit (doc §23): a white page is never acceptable. */
export function CustomerRiskPanel({ customerId }: { customerId: string }) {
  const risk = useCustomerRisk(customerId);
  const history = useRiskHistory(customerId);
  const limit = useCreditLimit(customerId);
  const assess = useAssessCustomer(customerId);
  const setLimit = useSetCreditLimit(customerId);

  if (risk.isPending) {
    return <div className="text-[12.5px] text-(--ice-3)">در حال محاسبهٔ وضعیت اعتباری…</div>;
  }

  if (risk.isError) {
    return (
      <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
        {(risk.error as ApiError).message ?? "خطا در بارگذاری وضعیت اعتباری"}
        <button type="button" onClick={() => void risk.refetch()} className="ms-2 underline">
          تلاش مجدد
        </button>
      </div>
    );
  }

  const data = risk.data;

  return (
    <div>
      {assess.isError && (
        <div className="mb-3 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {(assess.error as ApiError).message ?? "ارزیابی ناموفق بود"}
        </div>
      )}

      {!data.hasAssessment ? (
        <EmptyState
          icon="📊"
          title={data.insufficientData ? "اطلاعات کافی برای ارزیابی این مشتری وجود ندارد." : "هنوز ارزیابی اعتباری انجام نشده."}
          description={
            data.insufficientData
              ? "این مشتری هنوز بیمه‌نامه، قسط یا پرداختی ثبت‌شده‌ای ندارد که امتیاز اعتباری از آن استخراج شود."
              : "با دکمهٔ «ارزیابی اعتبار» نخستین ارزیابی را اجرا کنید."
          }
        />
      ) : (
        <AssessmentCard assessment={data.latest!} />
      )}

      <div className="mt-4">
        <button
          type="button"
          onClick={() => assess.mutate()}
          disabled={assess.isPending}
          className={BTN_PRIMARY}
        >
          {assess.isPending ? "در حال ارزیابی…" : data.hasAssessment ? "ارزیابی مجدد" : "ارزیابی اعتبار"}
        </button>
      </div>

      <CreditLimitEditor
        customerId={customerId}
        limit={limit.data}
        pending={limit.isPending}
        error={limit.isError ? ((limit.error as ApiError).message ?? null) : null}
        saving={setLimit.isPending}
        saveError={setLimit.isError ? ((setLimit.error as ApiError).message ?? null) : null}
        onSave={(v, reason) => setLimit.mutate({ limitToman: v, reason })}
      />

      <div className="mt-6">
        <div className="mb-2.5 text-[13px] font-bold text-(--ice)">تاریخچهٔ ارزیابی‌ها</div>
        {history.isPending && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}
        {history.isError && (
          <div className="text-[12.5px] text-(--ember)">{(history.error as ApiError).message ?? "خطا در بارگذاری تاریخچه"}</div>
        )}
        {history.data &&
          (history.data.length === 0 ? (
            <EmptyState icon="🕓" title="ارزیابی‌ای ثبت نشده." description="پس از نخستین ارزیابی، سابقهٔ آن اینجا نمایش داده میشود." />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["تاریخ", "امتیاز", "سطح ریسک", "تصمیم", "سقف اعتبار", "عوامل اصلی", "منبع"].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {history.data.map((h) => (
                  <Tr key={h.id}>
                    <Td className="py-2.75">{toJalaliDateTimeDisplay(h.calculatedAtUtc)}</Td>
                    <Td className="py-2.75 font-bold tabular-nums">{fa(h.score)}</Td>
                    <Td className="py-2.75 text-(--ice-3)">{h.riskLevel}</Td>
                    <Td className="py-2.75 text-(--ice-3)">{h.decision}</Td>
                    <Td className="py-2.75 tabular-nums">{money(h.creditLimitToman)}</Td>
                    <Td className="py-2.75 !text-[12px] text-(--ice-3)">{h.topFactors.join("، ") || "—"}</Td>
                    <Td className="py-2.75 text-(--ice-3)">{h.source}</Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          ))}
      </div>
    </div>
  );
}

/** The risk header (doc §25) — score, level, decision, limit and exposure, then the factor list
 * split into risk-increasing and risk-decreasing (doc §26): no score is ever shown without its
 * factors. */
function AssessmentCard({ assessment }: { assessment: RiskAssessmentDto }) {
  const level = RISK_LEVEL_STYLES[assessment.riskLevelKey];
  const decision = RISK_DECISION_STYLES[assessment.decisionKey];
  const negative = assessment.factors.filter((f) => f.impact < 0).sort((a, b) => a.impact - b.impact);
  const positive = assessment.factors.filter((f) => f.impact > 0).sort((a, b) => b.impact - a.impact);

  return (
    <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
      <div className="flex flex-wrap items-center gap-3">
        <div>
          <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">امتیاز اعتباری</div>
          <div className={`text-[26px] font-extrabold tabular-nums tracking-tight ${level.text}`}>{fa(assessment.score)}</div>
        </div>
        <span className={`rounded-full border px-3 py-1 text-[12px] font-semibold ${level.chip}`}>
          سطح ریسک: {level.label}
        </span>
        <span className={`rounded-full border px-3 py-1 text-[12px] font-semibold ${decision.chip}`}>
          تصمیم: {decision.label}
        </span>
        <span className="ms-auto text-[11px] text-(--ice-3)">
          {toJalaliDateTimeDisplay(assessment.calculatedAtUtc)} — {assessment.source}
        </span>
      </div>

      <div className="mt-4 grid grid-cols-4 gap-3">
        <Stat label="سقف اعتبار" value={money(assessment.creditLimitToman)} hint={assessment.creditLimitIsOverride ? "تنظیم دستی" : "پیشنهاد سیستم"} />
        <Stat label="بدهی جاری" value={money(assessment.creditExposureToman)} warn={assessment.creditExposureToman > 0} />
        <Stat label="مبلغ معوق" value={money(assessment.overdueAmountToman)} warn={assessment.overdueAmountToman > 0} />
        <Stat
          label="نرخ پرداخت به‌موقع"
          value={assessment.settledInstallmentCount === 0 ? "—" : `${fa(assessment.onTimeRatePercent)}٪`}
        />
      </div>

      <div className="mt-4 grid grid-cols-2 gap-4">
        <FactorList title="عوامل افزایش ریسک" items={negative} sign="-" tone="ember" empty="عامل افزایش ریسکی فعال نیست." />
        <FactorList title="عوامل کاهش ریسک" items={positive} sign="+" tone="mint" empty="عامل کاهش ریسکی فعال نیست." />
      </div>

      {assessment.triggeredRules.length > 0 && (
        <div className="mt-4">
          <div className="mb-2 text-[12.5px] font-bold text-(--ice)">قوانین فعال‌شده</div>
          <div className="flex flex-wrap gap-2">
            {assessment.triggeredRules.map((r) => (
              <span
                key={r.code}
                className={`rounded-full border px-3 py-1 text-[11.5px] font-semibold ${
                  r.isPositive
                    ? "border-(--mint)/40 bg-(--mint)/10 text-(--mint)"
                    : "border-(--ember)/40 bg-(--ember)/10 text-(--ember)"
                }`}
                title={r.description}
              >
                {r.name}
                {r.forcedLevel ? ` → ${r.forcedLevel}` : ""}
                {r.forcedDecision ? ` → ${r.forcedDecision}` : ""}
              </span>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

function Stat({ label, value, hint, warn }: { label: string; value: string; hint?: string; warn?: boolean }) {
  return (
    <div className="rounded-[14px] border border-(--edge-2) bg-(--fld) p-3">
      <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">{label}</div>
      <div className={`text-[16px] font-extrabold tabular-nums tracking-tight ${warn ? "text-(--ember)" : "text-(--ice)"}`}>{value}</div>
      {hint && <div className="mt-0.5 text-[10.5px] text-(--ice-3)">{hint}</div>}
    </div>
  );
}

function FactorList({
  title,
  items,
  sign,
  tone,
  empty,
}: {
  title: string;
  items: { code: string; title: string; detail: string; impact: number }[];
  sign: string;
  tone: "ember" | "mint";
  empty: string;
}) {
  return (
    <div>
      <div className="mb-2 text-[12.5px] font-bold text-(--ice)">{title}</div>
      {items.length === 0 ? (
        <div className="rounded-[10px] border border-(--edge) px-3 py-2 text-[12px] text-(--ice-3)">{empty}</div>
      ) : (
        <ul className="space-y-1.5">
          {items.map((f) => (
            <li key={f.code} className="rounded-[10px] border border-(--edge) px-3 py-2 text-[12px] leading-relaxed">
              <span className={`font-bold text-(--${tone}) tabular-nums`}>
                {sign}
                {fa(Math.abs(f.impact))}
              </span>{" "}
              <span className="font-semibold text-(--ice-2)">{f.title}</span>
              <span className="text-(--ice-3)"> — {f.detail}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function CreditLimitEditor({
  limit,
  pending,
  error,
  saving,
  saveError,
  onSave,
}: {
  customerId: string;
  limit: { recommendedToman: number | null; overrideToman: number | null; effectiveToman: number } | undefined;
  pending: boolean;
  error: string | null;
  saving: boolean;
  saveError: string | null;
  onSave: (limitToman: number, reason: string | null) => void;
}) {
  const [editing, setEditing] = useState(false);
  const [value, setValue] = useState("");
  const [reason, setReason] = useState("");

  if (pending) {
    return <div className="mt-5 text-[12.5px] text-(--ice-3)">در حال بارگذاری سقف اعتبار…</div>;
  }

  if (error) {
    return <div className="mt-5 text-[12.5px] text-(--ember)">{error}</div>;
  }

  return (
    <div className="mt-5 rounded-2xl border border-(--edge) bg-(--pane) p-4">
      <div className="flex flex-wrap items-center gap-2">
        <span className="text-[13px] font-bold text-(--ice)">حد اعتبار</span>
        <span className="rounded-full border border-(--edge-2) px-2.5 py-0.5 text-[11.5px] font-semibold tabular-nums text-(--ice-2)">
          مؤثر: {money(limit?.effectiveToman ?? 0)}
        </span>
        {limit?.recommendedToman != null && (
          <span className="text-[11.5px] text-(--ice-3)">پیشنهاد سیستم: {money(limit.recommendedToman)}</span>
        )}
        {limit?.overrideToman != null && (
          <span className="rounded-full bg-(--amber)/15 px-2.5 py-0.5 text-[11px] font-semibold text-(--amber)">
            تنظیم دستی: {money(limit.overrideToman)}
          </span>
        )}
        <button type="button" onClick={() => setEditing(!editing)} className={`${BTN_SECONDARY} ms-auto !px-3 !py-1.5 !text-[11.5px]`}>
          {editing ? "بستن" : "تنظیم دستی"}
        </button>
      </div>

      {editing && (
        <div className="mt-3 grid grid-cols-2 gap-3">
          <label className="block">
            <span className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">سقف اعتبار (تومان)</span>
            <input
              value={value}
              onChange={(e) => setValue(e.target.value.replace(/[^\d]/g, ""))}
              placeholder={limit?.overrideToman != null ? String(limit.overrideToman) : "0"}
              dir="ltr"
              className={`${INPUT_CLASS} tabular-nums`}
            />
          </label>
          <label className="block">
            <span className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">دلیل (اختیاری)</span>
            <input value={reason} onChange={(e) => setReason(e.target.value)} className={INPUT_CLASS} />
          </label>
          <div className="col-span-2">
            {saveError && <div className="mb-2 text-[12px] text-(--ember)">{saveError}</div>}
            <button
              type="button"
              onClick={() => onSave(Number(value || "0"), reason.trim() || null)}
              disabled={saving || value === ""}
              className={BTN_PRIMARY}
            >
              {saving ? "در حال ذخیره…" : "ذخیرهٔ سقف اعتبار"}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
