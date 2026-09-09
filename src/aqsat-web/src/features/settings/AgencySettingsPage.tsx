import { useEffect, useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { MoneyInput } from "../../components/MoneyInput";

interface AgencySettingsDto {
  code: string;
  name: string;
  city: string | null;
  insurerName: string | null;
  settlementDeadlineDays: number;
  lockScope: "Full" | "InstallmentOnly";
  dueDateRule: string;
  shiftOnHoliday: boolean;
  maxInstallments: number;
  reminderDaysBefore: string;
  maxOpenTabs: number;
  defaultServiceFee: number;
  serviceFeeMode: "Fixed" | "Percent";
  defaultWriteOffDays: number;
  renewalAutoWatchLeadDays: number;
  agencyCode: string | null;
  agencyCodeLocked: boolean;
  installmentContractText: string | null;
  dangerZoneManagerMobile: string | null;
}

export function AgencySettingsPage() {
  const [form, setForm] = useState<AgencySettingsDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);
  const [busy, setBusy] = useState(false);
  const [agencyCodeInput, setAgencyCodeInput] = useState("");
  const [agencyCodeBusy, setAgencyCodeBusy] = useState(false);
  const [agencyCodeError, setAgencyCodeError] = useState<string | null>(null);
  // The contract text follows backend keep/clear semantics (null keeps stored, empty clears to the
  // system default), so it is only sent when the operator actually edited it.
  const [contractTouched, setContractTouched] = useState(false);

  useEffect(() => {
    api
      .get<AgencySettingsDto>("/settings/agency")
      .then(setForm)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری تنظیمات"));
  }, []);

  function update<K extends keyof AgencySettingsDto>(key: K, value: AgencySettingsDto[K]) {
    setForm((prev) => (prev ? { ...prev, [key]: value } : prev));
    setSaved(false);
  }

  async function saveAgencyCode() {
    if (!agencyCodeInput.trim()) return;
    setAgencyCodeBusy(true);
    setAgencyCodeError(null);
    try {
      const updated = await api.put<AgencySettingsDto>("/settings/agency/agency-code", { agencyCode: agencyCodeInput.trim() });
      setForm(updated);
      setAgencyCodeInput("");
    } catch (err) {
      setAgencyCodeError(err instanceof ApiError ? err.message : "ذخیرهٔ کد نمایندگی ناموفق بود.");
    } finally {
      setAgencyCodeBusy(false);
    }
  }

  async function save() {
    if (!form) return;
    setBusy(true);
    setError(null);
    try {
      const updated = await api.put<AgencySettingsDto>("/settings/agency", {
        name: form.name,
        city: form.city,
        insurerName: form.insurerName,
        settlementDeadlineDays: Number(form.settlementDeadlineDays),
        lockScope: form.lockScope,
        shiftOnHoliday: form.shiftOnHoliday,
        maxInstallments: Number(form.maxInstallments),
        reminderDaysBefore: form.reminderDaysBefore,
        maxOpenTabs: Number(form.maxOpenTabs),
        defaultServiceFee: Number(form.defaultServiceFee),
        serviceFeeMode: form.serviceFeeMode,
        defaultWriteOffDays: Number(form.defaultWriteOffDays),
        renewalAutoWatchLeadDays: Number(form.renewalAutoWatchLeadDays),
        ...(contractTouched ? { installmentContractText: form.installmentContractText ?? "" } : {}),
        dangerZoneManagerMobile: form.dangerZoneManagerMobile ?? "",
      });
      setForm(updated);
      setContractTouched(false);
      setSaved(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ذخیرهٔ تنظیمات ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  if (!form) {
    return (
      <div>
        <h2 className="mb-4 text-xl font-extrabold tracking-tight text-(--ice)">مشخصات نمایندگی</h2>
        {error ? (
          <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">{error}</div>
        ) : (
          <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
        )}
      </div>
    );
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        مشخصات <em className="font-extralight not-italic text-(--ice-2)">نمایندگی</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">کد نمایندگی: {fa(form.code)}</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}
      {saved && (
        <div className="mb-4.5 rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-2 text-[12.5px] text-(--mint)">
          تنظیمات ذخیره شد.
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">هویت نمایندگی</div>
        <div className="grid grid-cols-3 gap-3">
          <Field label="نام نمایندگی">
            <input value={form.name} onChange={(e) => update("name", e.target.value)} className={inputClass} />
          </Field>
          <Field label="شهر">
            <input value={form.city ?? ""} onChange={(e) => update("city", e.target.value)} className={inputClass} />
          </Field>
          <Field label="شرکت بیمهٔ طرف قرارداد">
            <input value={form.insurerName ?? ""} onChange={(e) => update("insurerName", e.target.value)} className={inputClass} />
          </Field>
          <Field label="موبایل مدیر نمایندگی (ارسال کد عملیات حساس)">
            <input
              value={form.dangerZoneManagerMobile ?? ""}
              onChange={(e) => update("dangerZoneManagerMobile", e.target.value)}
              placeholder="09123456789"
              dir="ltr"
              className={inputClass}
            />
          </Field>
        </div>

        <div className="mt-3 border-t border-(--edge) pt-3">
          <Field label="کد نمایندگی (برای شمارهٔ بیمه‌نامه)">
            {form.agencyCodeLocked ? (
              <div className="flex items-center gap-2">
                <div className={`${inputClass} bg-(--fld)/50 text-(--ice-3)`}>{fa(form.agencyCode ?? "")}</div>
                <span className="shrink-0 text-[11.5px] text-(--ice-3)">🔒 پس از اولین بیمه‌نامه قفل شده</span>
              </div>
            ) : (
              <div className="flex items-center gap-2">
                <input
                  value={agencyCodeInput || form.agencyCode || ""}
                  onChange={(e) => setAgencyCodeInput(e.target.value)}
                  placeholder="۵۷۶۲۱۰"
                  className={inputClass}
                />
                <button
                  type="button"
                  disabled={agencyCodeBusy || !agencyCodeInput.trim()}
                  onClick={saveAgencyCode}
                  className="shrink-0 rounded-[10px] border border-(--mint) bg-(--mint) px-3 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
                >
                  ذخیره
                </button>
              </div>
            )}
            {agencyCodeError && <div className="mt-1.5 text-[11.5px] text-(--ember)">{agencyCodeError}</div>}
          </Field>
        </div>
      </div>

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">شمارش‌معکوس تسویه</div>
        <div className="grid grid-cols-3 gap-3">
          <Field label="مهلت تسویه (روز)">
            <input
              value={form.settlementDeadlineDays}
              onChange={(e) => update("settlementDeadlineDays", Number(e.target.value) as never)}
              className={inputClass}
            />
          </Field>
          <Field label="محدودهٔ قفل">
            <select value={form.lockScope} onChange={(e) => update("lockScope", e.target.value as AgencySettingsDto["lockScope"])} className={inputClass}>
              <option value="Full">کامل</option>
              <option value="InstallmentOnly">فقط قسط</option>
            </select>
          </Field>
          <Field label="جابه‌جایی مهلت در تعطیلات">
            <select
              value={form.shiftOnHoliday ? "yes" : "no"}
              onChange={(e) => update("shiftOnHoliday", e.target.value === "yes")}
              className={inputClass}
            >
              <option value="yes">فعال</option>
              <option value="no">غیرفعال</option>
            </select>
          </Field>
        </div>
      </div>

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">اقساط و یادآوری</div>
        <div className="grid grid-cols-3 gap-3">
          <Field label="سقف تعداد اقساط">
            <input value={form.maxInstallments} onChange={(e) => update("maxInstallments", Number(e.target.value) as never)} className={inputClass} />
          </Field>
          <Field label="روزهای یادآوری (با ویرگول)">
            <input value={form.reminderDaysBefore} onChange={(e) => update("reminderDaysBefore", e.target.value)} className={inputClass} />
          </Field>
          <Field label="سقف تب‌های باز">
            <input value={form.maxOpenTabs} onChange={(e) => update("maxOpenTabs", Number(e.target.value) as never)} className={inputClass} />
          </Field>
        </div>
      </div>

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3 text-[12.5px] font-semibold text-(--ice-2)">کارمزد خدمات و سود و زیان</div>
        <div className="grid grid-cols-4 gap-3">
          <Field label="کارمزد خدمات پیش‌فرض">
            <MoneyInput
              value={String(form.defaultServiceFee)}
              onChange={(v) => update("defaultServiceFee", Number(v) as never)}
              className={inputClass}
            />
          </Field>
          <Field label="حالت کارمزد">
            <select value={form.serviceFeeMode} onChange={(e) => update("serviceFeeMode", e.target.value as AgencySettingsDto["serviceFeeMode"])} className={inputClass}>
              <option value="Fixed">مبلغ ثابت</option>
              <option value="Percent">درصدی</option>
            </select>
          </Field>
          <Field label="آستانهٔ سوخت نکول (روز)">
            <input value={form.defaultWriteOffDays} onChange={(e) => update("defaultWriteOffDays", Number(e.target.value) as never)} className={inputClass} />
          </Field>
          <Field label="پیش‌آگهی تمدید (روز)">
            <input value={form.renewalAutoWatchLeadDays} onChange={(e) => update("renewalAutoWatchLeadDays", Number(e.target.value) as never)} className={inputClass} />
          </Field>
        </div>
      </div>

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-1 text-[12.5px] font-semibold text-(--ice-2)">متن قرارداد اقساط</div>
        <div className="mb-3 text-[11.5px] leading-relaxed text-(--ice-3)">
          متنی که مشتری هنگام تأیید قرارداد در پورتال می‌بیند و می‌پذیرد. خالی گذاشتن = بازگشت به متن پیش‌فرض سیستم.
        </div>
        <textarea
          value={form.installmentContractText ?? ""}
          onChange={(e) => {
            update("installmentContractText", e.target.value);
            setContractTouched(true);
          }}
          rows={7}
          dir="rtl"
          placeholder="متن پیش‌فرض سیستم فعال است — برای ویرایش، متن دلخواه را بنویسید."
          className={`${inputClass} min-h-36 resize-y leading-relaxed`}
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

      <DangerZone code={form.code} />
    </div>
  );
}

function DangerZone({ code }: { code: string }) {
  const [confirmText, setConfirmText] = useState("");
  const [otpSent, setOtpSent] = useState(false);
  const [otpCode, setOtpCode] = useState("");
  const [otpBusy, setOtpBusy] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  async function requestOtp() {
    setOtpBusy(true);
    setError(null);
    try {
      await api.post("/settings/agency/data/request-otp", {});
      setOtpSent(true);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ارسال کد تأیید ناموفق بود.");
    } finally {
      setOtpBusy(false);
    }
  }

  async function clearData() {
    setBusy(true);
    setError(null);
    try {
      await api.delete("/settings/agency/data", {
        confirmCode: confirmText.trim(),
        otpCode: otpCode.trim(),
      });
      setDone(true);
      setConfirmText("");
      setOtpCode("");
      setOtpSent(false);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "پاکسازی ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="mt-6 rounded-2xl border border-(--ember)/30 bg-(--ember)/5 p-5">
      <div className="mb-1 text-[12.5px] font-semibold text-(--ember)">منطقهٔ خطر</div>
      <div className="mb-3 text-[11.5px] text-(--ice-3)">
        پاک‌کردن کامل داده‌های این نمایندگی — همهٔ بیمه‌نامه‌ها، اقساط، پرداخت‌ها و وثیقه‌ها. مشتریان و کاربران دست‌نخورده می‌مانند. غیرقابل بازگشت از داخل برنامه. برای انجام، یک کد تأیید پیامکی به موبایل مدیر نمایندگی (در تنظیمات بالا) ارسال می‌شود.
      </div>
      {error && (
        <div className="mb-3 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">{error}</div>
      )}
      {done && (
        <div className="mb-3 rounded-[10px] border border-(--mint)/30 bg-(--mint)/10 px-3 py-2 text-[12.5px] text-(--mint)">داده‌ها پاک شدند.</div>
      )}
      <div className="flex flex-wrap items-center gap-2">
        <input
          value={confirmText}
          onChange={(e) => setConfirmText(e.target.value)}
          placeholder={`برای تأیید، «${code}» را تایپ کنید`}
          className="w-64 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--ember)"
        />
        <button
          type="button"
          disabled={otpBusy || confirmText.trim() !== code}
          onClick={requestOtp}
          className="rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) disabled:cursor-not-allowed disabled:opacity-50"
        >
          {otpBusy ? "در حال ارسال…" : otpSent ? "ارسال مجدد کد" : "ارسال کد تأیید پیامکی"}
        </button>
        {otpSent && (
          <input
            value={otpCode}
            onChange={(e) => setOtpCode(e.target.value)}
            placeholder="کد ۶ رقمی پیامک‌شده"
            dir="ltr"
            className="w-40 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] tabular-nums text-(--ice) outline-none focus:border-(--ember)"
          />
        )}
        <button
          type="button"
          disabled={busy || confirmText.trim() !== code || !otpSent || otpCode.trim().length < 6}
          onClick={clearData}
          className="rounded-[10px] border border-(--ember) bg-(--ember) px-4 py-2 text-[12.5px] font-semibold text-white transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {busy ? "در حال پاکسازی…" : "پاک‌کردن کامل داده‌ها"}
        </button>
      </div>
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
