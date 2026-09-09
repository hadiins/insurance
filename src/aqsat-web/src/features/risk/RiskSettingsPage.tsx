import { useEffect, useState, type ReactNode } from "react";
import { useRiskSettings, useUpdateRiskSettings } from "./riskApi";
import { fa, money } from "../../lib/persian";
import type { RiskSettingsDto } from "./riskTypes";

const INPUT_CLASS =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint) tabular-nums";

const GATE_MODES: { value: RiskSettingsDto["issuanceGateMode"]; label: string; description: string }[] = [
  { value: "Informational", label: "فقط اطلاع‌رسانی", description: "امتیاز و سطح ریسک نمایش داده می‌شود اما صدور بیمه‌نامه بلاک نمی‌شود." },
  { value: "SoftBlock", label: "بلاک نرم", description: "برای مشتری پرریسک هشدار نمایش داده می‌شود و صدور با تأیید دوبارهٔ کارشناس ممکن است." },
  { value: "HardBlock", label: "بلاک سخت", description: "صدور بیمه‌نامهٔ اقساطی برای مشتری ردشده غیرممکن است." },
];

/** «قوانین اعتبارسنجی» (docs Phase 2A §8–§14) — per-agency scoring configuration. Weights must
 * sum to exactly 1.00 and bands must stay ordered; both are enforced server-side too. */
export function RiskSettingsPage() {
  const settings = useRiskSettings();
  const update = useUpdateRiskSettings();
  const [form, setForm] = useState<RiskSettingsDto | null>(null);

  useEffect(() => {
    if (settings.data) setForm(settings.data);
  }, [settings.data]);

  if (settings.isPending) {
    return (
      <div>
        <h2 className="mb-4 text-xl font-extrabold tracking-tight text-(--ice)">قوانین اعتبارسنجی</h2>
        <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
      </div>
    );
  }

  if (settings.isError || !form) {
    return (
      <div>
        <h2 className="mb-4 text-xl font-extrabold tracking-tight text-(--ice)">قوانین اعتبارسنجی</h2>
        <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {settings.error instanceof Error ? settings.error.message : "خطا در بارگذاری تنظیمات"}
          <button type="button" onClick={() => void settings.refetch()} className="ms-2 underline">
            تلاش مجدد
          </button>
        </div>
      </div>
    );
  }

  const weightSum =
    form.paymentHistoryWeight + form.currentDebtWeight + form.latePaymentWeight +
    form.returnedChequesWeight + form.customerTenureWeight + form.insuranceBehaviorWeight;

  function set<K extends keyof RiskSettingsDto>(key: K, value: RiskSettingsDto[K]) {
    setForm({ ...form!, [key]: value } as RiskSettingsDto);
  }

  function setNum(key: keyof RiskSettingsDto, raw: string, decimals: boolean) {
    const cleaned = decimals ? raw.replace(/[^\d.]/g, "") : raw.replace(/\D/g, "");
    const n = Number(cleaned === "" || cleaned === "." ? "0" : cleaned);
    if (Number.isNaN(n)) return;
    set(key, n as never);
  }

  return (
    <div className="mx-auto max-w-4xl">
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">قوانین اعتبارسنجی</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
        وزن عوامل، بازه‌های سطح ریسک، سقف اعتبار و آستانهٔ قوانین — مخصوص این نمایندگی
      </div>

      {update.isError && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] leading-relaxed text-(--ember)">
          {update.error instanceof Error ? update.error.message : "ذخیرهٔ تنظیمات ناموفق بود."}
        </div>
      )}
      {update.isSuccess && (
        <div className="mb-4.5 rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-2 text-[12.5px] text-(--mint)">
          تنظیمات ذخیره شد.
        </div>
      )}

      <Section title="دروازهٔ صدور" hint="رفتار سیستم هنگام صدور بیمه‌نامهٔ اقساطی برای مشتری پرریسک">
        <div className="space-y-1.5">
          {GATE_MODES.map((m) => (
            <label
              key={m.value}
              className={`flex cursor-pointer items-start gap-2.5 rounded-[10px] border px-3 py-2 transition-colors ${
                form.issuanceGateMode === m.value ? "border-(--mint)/50 bg-(--mint)/5" : "border-(--edge-2)"
              }`}
            >
              <input
                type="radio"
                name="gate-mode"
                checked={form.issuanceGateMode === m.value}
                onChange={() => set("issuanceGateMode", m.value)}
                className="mt-0.5 accent-(--mint)"
              />
              <span>
                <span className="block text-[13px] font-semibold text-(--ice)">{m.label}</span>
                <span className="block text-[11.5px] text-(--ice-3)">{m.description}</span>
              </span>
            </label>
          ))}
        </div>
      </Section>

      <Section
        title="وزن عوامل امتیاز"
        hint={`مجموع وزن‌ها باید دقیقاً ۱ باشد — مجموع فعلی: ${fa(weightSum.toFixed(2))}`}
        tone={Math.abs(weightSum - 1) > 0.0001 ? "ember" : "mint"}
      >
        <div className="grid grid-cols-3 gap-3">
          <NumField label="سابقهٔ پرداخت" value={form.paymentHistoryWeight} decimals step="0.05" onChange={(v) => setNum("paymentHistoryWeight", v, true)} />
          <NumField label="بدهی جاری" value={form.currentDebtWeight} decimals step="0.05" onChange={(v) => setNum("currentDebtWeight", v, true)} />
          <NumField label="تأخیر پرداخت" value={form.latePaymentWeight} decimals step="0.05" onChange={(v) => setNum("latePaymentWeight", v, true)} />
          <NumField label="چک برگشتی" value={form.returnedChequesWeight} decimals step="0.05" onChange={(v) => setNum("returnedChequesWeight", v, true)} />
          <NumField label="سابقهٔ همکاری" value={form.customerTenureWeight} decimals step="0.05" onChange={(v) => setNum("customerTenureWeight", v, true)} />
          <NumField label="رفتار بیمه‌ای" value={form.insuranceBehaviorWeight} decimals step="0.05" onChange={(v) => setNum("insuranceBehaviorWeight", v, true)} />
        </div>
      </Section>

      <Section title="بازه‌های امتیاز" hint="حداقل امتیاز هر سطح — باید صعودی و بین ۰ تا ۱۰۰۰ باشد">
        <div className="grid grid-cols-4 gap-3">
          <NumField label="خیلی پایین از" value={form.veryLowMinScore} onChange={(v) => setNum("veryLowMinScore", v, false)} />
          <NumField label="پایین از" value={form.lowMinScore} onChange={(v) => setNum("lowMinScore", v, false)} />
          <NumField label="متوسط از" value={form.mediumMinScore} onChange={(v) => setNum("mediumMinScore", v, false)} />
          <NumField label="بالا از" value={form.highMinScore} onChange={(v) => setNum("highMinScore", v, false)} />
        </div>
        <div className="mt-3 grid grid-cols-2 gap-3">
          <NumField label="حداقل امتیاز تأیید" value={form.approveMinScore} onChange={(v) => setNum("approveMinScore", v, false)} />
          <NumField label="رد زیر امتیاز" value={form.declineBelowScore} onChange={(v) => setNum("declineBelowScore", v, false)} />
        </div>
      </Section>

      <Section title="سقف اعتبار" hint={`سقف پایین فعلی: ${money(form.baseCreditLimitToman)} تومان — ضریب هر سطح در آن ضرب می‌شود`}>
        <div className="grid grid-cols-2 gap-3">
          <NumField label="سقف پایین (تومان)" value={form.baseCreditLimitToman} onChange={(v) => setNum("baseCreditLimitToman", v, false)} />
        </div>
        <div className="mt-3 grid grid-cols-5 gap-3">
          <NumField label="ضریب خیلی پایین" value={form.veryLowMultiplier} decimals step="0.05" onChange={(v) => setNum("veryLowMultiplier", v, true)} />
          <NumField label="ضریب پایین" value={form.lowMultiplier} decimals step="0.05" onChange={(v) => setNum("lowMultiplier", v, true)} />
          <NumField label="ضریب متوسط" value={form.mediumMultiplier} decimals step="0.05" onChange={(v) => setNum("mediumMultiplier", v, true)} />
          <NumField label="ضریب بالا" value={form.highMultiplier} decimals step="0.05" onChange={(v) => setNum("highMultiplier", v, true)} />
          <NumField label="ضریب بحرانی" value={form.criticalMultiplier} decimals step="0.05" onChange={(v) => setNum("criticalMultiplier", v, true)} />
        </div>
      </Section>

      <Section title="آستانهٔ قوانین" hint="قوانین ثابت سیستم — فقط آستانهٔ فعال‌شدن هر کدام اینجا تنظیم می‌شود">
        <div className="grid grid-cols-3 gap-3">
          <NumField label="چک برگشتی ≥ (تعداد)" value={form.bouncedChequeHighThreshold} onChange={(v) => setNum("bouncedChequeHighThreshold", v, false)} />
          <NumField label="معوق شدید ≥ (روز)" value={form.severeOverdueDays} onChange={(v) => setNum("severeOverdueDays", v, false)} />
          <NumField label="حداکثر تأخیر ≥ (روز)" value={form.maxLateDaysHighThreshold} onChange={(v) => setNum("maxLateDaysHighThreshold", v, false)} />
          <NumField label="پرداخت به‌موقع ≥ (٪)" value={form.onTimeRatePositivePercent} onChange={(v) => setNum("onTimeRatePositivePercent", v, false)} />
          <NumField label="افت امتیاز ≥ (امتیاز)" value={form.scoreDropWarningPoints} onChange={(v) => setNum("scoreDropWarningPoints", v, false)} />
          <NumField label="رشد بدهی ≥ (٪)" value={form.debtGrowthWarningPercent} onChange={(v) => setNum("debtGrowthWarningPercent", v, false)} />
          <NumField label="استفاده از سقف ≥ (٪)" value={form.creditLimitUtilizationWarningPercent} onChange={(v) => setNum("creditLimitUtilizationWarningPercent", v, false)} />
        </div>
      </Section>

      <div className="mt-5 flex items-center gap-3">
        <button
          type="button"
          onClick={() => update.mutate(form)}
          disabled={update.isPending}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-5 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {update.isPending ? "در حال ذخیره…" : "ذخیرهٔ تنظیمات"}
        </button>
        <button
          type="button"
          onClick={() => setForm(settings.data!)}
          disabled={update.isPending}
          className="rounded-[10px] border border-(--edge-2) px-5 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--hov) hover:text-(--ice) disabled:opacity-50"
        >
          بازگردانی
        </button>
      </div>
    </div>
  );
}

function Section({
  title,
  hint,
  tone,
  children,
}: {
  title: string;
  hint?: string;
  tone?: "mint" | "ember";
  children: ReactNode;
}) {
  return (
    <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
      <div className="mb-1 text-[13px] font-bold text-(--ice)">{title}</div>
      {hint && (
        <div className={`mb-3 text-[11.5px] ${tone === "ember" ? "font-semibold text-(--ember)" : tone === "mint" ? "text-(--mint)" : "text-(--ice-3)"}`}>
          {hint}
        </div>
      )}
      {children}
    </div>
  );
}

function NumField({
  label,
  value,
  decimals,
  step,
  onChange,
}: {
  label: string;
  value: number;
  decimals?: boolean;
  step?: string;
  onChange: (raw: string) => void;
}) {
  return (
    <label className="block">
      <span className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">{label}</span>
      <input
        type="number"
        inputMode={decimals ? "decimal" : "numeric"}
        step={step ?? "1"}
        min="0"
        value={decimals ? String(value) : String(Math.round(value))}
        onChange={(e) => onChange(e.target.value)}
        dir="ltr"
        className={INPUT_CLASS}
      />
    </label>
  );
}
