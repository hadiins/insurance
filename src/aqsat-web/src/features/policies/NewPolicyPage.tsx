import { useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { api, ApiError } from "../../lib/api";

interface InsuranceLineDto {
  id: string;
  parentId: string | null;
  code: string;
  nameFa: string;
  requiresVehicle: boolean;
  requiresProperty: boolean;
  sortOrder: number;
}

interface CreatePolicyResultDto {
  policyId: string;
  policyNumber: string;
  customerId: string;
}

const TODAY = new Date().toISOString().slice(0, 10);

interface FormState {
  policyNumber: string;
  insuranceLineId: string;
  customerFullName: string;
  customerMobile: string;
  customerNationalId: string;
  vehiclePlate: string;
  propertyAddress: string;
  propertyPostalCode: string;
  netPremium: string;
  serviceFee: string;
  issueDate: string;
  startDate: string;
  endDate: string;
}

const EMPTY: FormState = {
  policyNumber: "",
  insuranceLineId: "",
  customerFullName: "",
  customerMobile: "",
  customerNationalId: "",
  vehiclePlate: "",
  propertyAddress: "",
  propertyPostalCode: "",
  netPremium: "",
  serviceFee: "",
  issueDate: TODAY,
  startDate: TODAY,
  endDate: "",
};

export function NewPolicyPage() {
  const tabKey = useTabKey();
  const setDirty = useTabsStore((s) => s.setDirty);
  const setTitle = useTabsStore((s) => s.setTitle);
  const [form, setForm] = useState<FormState>(EMPTY);
  const [lines, setLines] = useState<InsuranceLineDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [result, setResult] = useState<CreatePolicyResultDto | null>(null);

  useEffect(() => {
    api
      .get<InsuranceLineDto[]>("/insurance-lines")
      .then(setLines)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری رشته‌های بیمه"));
  }, []);

  const selectedLine = lines?.find((l) => l.id === form.insuranceLineId) ?? null;

  function update<K extends keyof FormState>(field: K, value: string) {
    const next = { ...form, [field]: value };
    setForm(next);
    setDirty(tabKey, true);
    if (field === "policyNumber") {
      setTitle(tabKey, value.trim() ? `بیمه‌نامه — ${value.trim()}` : "ثبت بیمه‌نامه");
    }
  }

  function reset() {
    setForm(EMPTY);
    setResult(null);
    setError(null);
    setDirty(tabKey, false);
    setTitle(tabKey, "ثبت بیمه‌نامه");
  }

  async function save() {
    if (!selectedLine) {
      setError("نوع بیمه‌نامه را انتخاب کنید.");
      return;
    }
    if (!form.customerFullName.trim()) {
      setError("نام بیمه‌گذار الزامی است.");
      return;
    }
    if (!form.endDate) {
      setError("تاریخ پایان الزامی است.");
      return;
    }

    setSaving(true);
    setError(null);
    try {
      const created = await api.post<CreatePolicyResultDto>("/policies", {
        policyNumber: form.policyNumber.trim(),
        insuranceLineId: form.insuranceLineId,
        customerId: null,
        customerFullName: form.customerFullName.trim(),
        customerMobile: form.customerMobile || null,
        customerNationalId: form.customerNationalId || null,
        vehicle: selectedLine.requiresVehicle || form.vehiclePlate
          ? { plate: form.vehiclePlate || null, vin: null, chassis: null, make: null, model: null, year: null }
          : null,
        property: selectedLine.requiresProperty
          ? { address: form.propertyAddress, postalCode: form.propertyPostalCode || null, type: null, value: null }
          : null,
        issueDate: form.issueDate,
        startDate: form.startDate,
        endDate: form.endDate,
        netPremium: Number(form.netPremium) || 0,
        serviceFee: Number(form.serviceFee) || 0,
        marketerId: null,
        previousInsurer: null,
        isRenewal: false,
      });
      setResult(created);
      setDirty(tabKey, false);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت بیمه‌نامه ناموفق بود.");
    } finally {
      setSaving(false);
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        ثبت <em className="font-extralight not-italic text-(--ice-2)">بیمه‌نامهٔ جدید</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">فرم نیمه‌تمام هنگام جابه‌جایی بین تب‌ها حفظ می‌شود</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {result ? (
        <div className="rounded-2xl border border-(--mint)/30 bg-(--mint)/8 p-5">
          <div className="mb-3 text-[14px] font-bold text-(--mint)">بیمه‌نامه با موفقیت ثبت شد</div>
          <div className="mb-4 text-[13px] text-(--ice-2)">
            شمارهٔ بیمه‌نامه: <b>{result.policyNumber}</b> — گام بعد، زمان‌بندی اقساط از فهرست «در انتظار زمان‌بندی» است.
          </div>
          <button
            type="button"
            onClick={reset}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105"
          >
            ثبت بیمه‌نامهٔ دیگر
          </button>
        </div>
      ) : (
        <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
          <div className="grid grid-cols-2 gap-3.5">
            <div>
              <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">نوع بیمه‌نامه</label>
              <select
                value={form.insuranceLineId}
                onChange={(e) => update("insuranceLineId", e.target.value)}
                className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
              >
                <option value="">انتخاب کنید…</option>
                {lines?.map((l) => (
                  <option key={l.id} value={l.id}>
                    {l.nameFa}
                  </option>
                ))}
              </select>
            </div>
            <Field label="شمارهٔ بیمه‌نامه" value={form.policyNumber} onChange={(v) => update("policyNumber", v)} />

            <Field label="نام بیمه‌گذار" value={form.customerFullName} onChange={(v) => update("customerFullName", v)} />
            <Field label="شمارهٔ همراه" value={form.customerMobile} onChange={(v) => update("customerMobile", v)} placeholder="۰۹۱۲۳۴۵۶۷۸۹" />
            <Field label="کد ملی بیمه‌گذار" value={form.customerNationalId} onChange={(v) => update("customerNationalId", v)} placeholder="۰۰۷۲۳۴۵۴۵۳" />

            {selectedLine?.requiresVehicle && (
              <Field label="شماره پلاک" value={form.vehiclePlate} onChange={(v) => update("vehiclePlate", v)} placeholder="۷۴ ب ۳۲۱ ایران ۶۳" />
            )}
            {selectedLine?.requiresProperty && (
              <>
                <Field label="نشانی ملک" value={form.propertyAddress} onChange={(v) => update("propertyAddress", v)} />
                <Field label="کد پستی" value={form.propertyPostalCode} onChange={(v) => update("propertyPostalCode", v)} />
              </>
            )}

            <Field label="حق بیمه (تومان)" value={form.netPremium} onChange={(v) => update("netPremium", v)} placeholder="۹٬۰۰۰٬۰۰۰" />
            <Field label="کارمزد خدمات (تومان)" value={form.serviceFee} onChange={(v) => update("serviceFee", v)} placeholder="۵۰۰٬۰۰۰" />

            <DateField label="تاریخ صدور" value={form.issueDate} onChange={(v) => update("issueDate", v)} />
            <DateField label="تاریخ شروع" value={form.startDate} onChange={(v) => update("startDate", v)} />
            <DateField label="تاریخ پایان" value={form.endDate} onChange={(v) => update("endDate", v)} />
          </div>

          <div className="mt-4 flex gap-2">
            <button
              type="button"
              onClick={save}
              disabled={saving || lines === null}
              className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
            >
              {saving ? "در حال ثبت…" : "ذخیره"}
            </button>
            <button
              type="button"
              onClick={reset}
              className="rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
            >
              پاک کردن
            </button>
          </div>
          <div className="mt-2.5 border-s-2 border-(--edge-2) ps-3 text-[11.5px] leading-loose text-(--ice-3)">
            این فرم فیلد متن آزاد دربارهٔ <b className="font-bold">شخص</b> ندارد — فقط دربارهٔ بیمه‌نامه.
          </div>
        </div>
      )}
    </div>
  );
}

function Field({
  label,
  value,
  onChange,
  placeholder,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
}) {
  return (
    <div>
      <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">{label}</label>
      <input
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
      />
    </div>
  );
}

function DateField({ label, value, onChange }: { label: string; value: string; onChange: (v: string) => void }) {
  return (
    <div>
      <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">{label}</label>
      <input
        type="date"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
      />
    </div>
  );
}
