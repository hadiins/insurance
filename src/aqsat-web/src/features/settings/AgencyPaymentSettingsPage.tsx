import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";

/** Mirrors AgencyPaymentGatewayDto — the agency's own gateway, receiving ONLY its customers'
 * down payments and installments into the agency's own account. Inquiry fees are collected
 * through the owner's platform gateway (a separate account, configured by the owner). */
interface AgencyPaymentGatewayDto {
  name: string;
  code: string;
  paymentProvider: string;
  customerPortalEnabled: boolean;
  hasAgentMerchantId: boolean;
  agentMerchantIdMasked: string | null;
  portalInvitationTtlHours: number;
  paymentLinkTtlDays: number;
}

const PROVIDERS: Array<{ value: string; label: string; note: string }> = [
  { value: "Mock", label: "آزمایشی (Mock)", note: "درگاه شبیه‌سازیشده برای توسعه و تست — پول واقعی جابه‌جا نمی‌شود." },
  { value: "ZarinPal", label: "زرین‌پال", note: "درگاه واقعی — شناسهٔ پذیرندهٔ نمایندگی برای دریافت پیش‌پرداخت و اقساط الزامی است." },
];

export function AgencyPaymentSettingsPage() {
  const [form, setForm] = useState<AgencyPaymentGatewayDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [merchantIdInput, setMerchantIdInput] = useState("");

  useEffect(() => {
    api
      .get<AgencyPaymentGatewayDto>("/settings/agency/payment-gateway")
      .then(setForm)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری تنظیمات"));
  }, []);

  function update<K extends keyof AgencyPaymentGatewayDto>(key: K, value: AgencyPaymentGatewayDto[K]) {
    setForm((prev) => (prev ? { ...prev, [key]: value } : prev));
    setSaved(false);
  }

  async function save() {
    if (!form) return;
    setBusy(true);
    setError(null);
    setSaved(false);
    try {
      const updated = await api.put<AgencyPaymentGatewayDto>("/settings/agency/payment-gateway", {
        paymentProvider: form.paymentProvider,
        customerPortalEnabled: form.customerPortalEnabled,
        agentMerchantId: merchantIdInput.trim() || null,
        portalInvitationTtlHours: Number(form.portalInvitationTtlHours),
        paymentLinkTtlDays: Number(form.paymentLinkTtlDays),
      });
      setForm(updated);
      setSaved(true);
      setMerchantIdInput("");
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ذخیرهٔ تنظیمات ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  if (!form) {
    return (
      <div>
        <h2 className="mb-4 text-xl font-extrabold tracking-tight text-(--ice)">تنظیمات درگاه پرداخت</h2>
        {error ? (
          <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">{error}</div>
        ) : (
          <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
        )}
      </div>
    );
  }

  return (
    <div className="mx-auto max-w-3xl">
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        تنظیمات <em className="font-extralight not-italic text-(--ice-2)">درگاه پرداخت</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">{fa(form.name)} — کد {fa(form.code)}</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">{error}</div>
      )}
      {saved && (
        <div className="mb-4.5 rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-2 text-[12.5px] text-(--mint)">تنظیمات ذخیره شد.</div>
      )}

      <div className="mb-4.5 rounded-[10px] border border-(--edge) bg-(--pane) px-3 py-2 text-[11.5px] leading-relaxed text-(--ice-3)">
        درگاه پرداخت نمایندگی فقط برای دریافت <span className="font-semibold text-(--ice-2)">پیش‌پرداخت و اقساط مشتریان همین نمایندگی</span> استفاده می‌شود
        و مبالغ به حسابی که نمایندگی در درگاه تعریف کرده واریز می‌شود.
        کارمزد استعلام‌ها از طریق درگاه مالک دریافت و به حساب مالک واریز می‌شود — این دو درگاه مستقل‌اند و تداخلی با هم ندارند.
      </div>

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">درگاه پرداخت</div>
        <div className="mb-2 space-y-2">
          {PROVIDERS.map((p) => (
            <label key={p.value} className="flex cursor-pointer items-start gap-2.5 rounded-[10px] bg-(--fld) px-3 py-2">
              <input
                type="radio"
                name="agency-payment-provider"
                checked={form.paymentProvider === p.value}
                onChange={() => update("paymentProvider", p.value)}
                className="mt-1 h-4 w-4 shrink-0 accent-(--mint)"
              />
              <span>
                <span className="block text-[13.5px] font-semibold text-(--ice)">{p.label}</span>
                <span className="block text-[11.5px] text-(--ice-3)">{p.note}</span>
              </span>
            </label>
          ))}
        </div>

        <div className="mt-3 grid grid-cols-2 gap-3">
          <Field label="پورتال مشتری">
            <select
              value={form.customerPortalEnabled ? "yes" : "no"}
              onChange={(e) => update("customerPortalEnabled", e.target.value === "yes")}
              className={inputClass}
            >
              <option value="yes">فعال</option>
              <option value="no">غیرفعال</option>
            </select>
          </Field>
          <Field label="اعتبار لینک پورتال (ساعت)">
            <input
              value={form.portalInvitationTtlHours}
              onChange={(e) => update("portalInvitationTtlHours", Number(e.target.value) as never)}
              className={inputClass}
            />
          </Field>
        </div>

        <div className="mt-3">
          <Field label="اعتبار لینک پرداخت قسط در پیامک (روز)">
            <input
              value={form.paymentLinkTtlDays}
              onChange={(e) => update("paymentLinkTtlDays", Number(e.target.value) as never)}
              className={inputClass}
            />
          </Field>
          <div className="mt-1.5 text-[11px] leading-relaxed text-(--ice-3)">
            هر پیامک یادآوری قسط، اعتبار لینک پرداخت آنلاین مشتری را به همین تعداد روز تازه می‌کند.
          </div>
        </div>
      </div>

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-1 text-[12.5px] font-semibold text-(--ice-2)">شناسهٔ پذیرنده (Merchant ID)</div>
        <div className="mb-2 text-[11.5px] text-(--ice-3)">
          شناسهٔ حساب نمایندگی در درگاه پرداخت — پرداخت‌های مشتریان بابت پیش‌پرداخت و اقساط به همین حساب واریز می‌شود.
          برای حفظ مقدار فعلی خالی بگذارید؛ پس از ذخیره هرگز کامل نمایش داده نمی‌شود.
        </div>
        {form.hasAgentMerchantId && (
          <div className={`${inputClass} mb-2 bg-(--fld)/50 text-(--ice-3)`} dir="ltr">
            {form.agentMerchantIdMasked ?? "—"}
          </div>
        )}
        <input
          value={merchantIdInput}
          onChange={(e) => setMerchantIdInput(e.target.value)}
          placeholder={form.hasAgentMerchantId ? "برای تغییر، شناسهٔ جدید را وارد کنید (خالی = بدون تغییر)" : "شناسهٔ پذیرنده"}
          dir="ltr"
          className={inputClass}
        />
      </div>

      <button
        type="button"
        disabled={busy}
        onClick={save}
        className="rounded-[10px] border border-(--mint) bg-(--mint) px-5 py-2.5 text-[13.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
      >
        {busy ? "در حال ذخیره…" : "ذخیرهٔ تنظیمات"}
      </button>
    </div>
  );
}

const inputClass =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)";

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">{label}</label>
      {children}
    </div>
  );
}
