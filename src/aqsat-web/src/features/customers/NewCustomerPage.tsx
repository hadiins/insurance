import { useEffect, useState } from "react";
import { useTabKey } from "../shell/TabContext";
import { useTabsStore } from "../../app/store/tabsStore";
import { api, ApiError } from "../../lib/api";
import { fa, isValidNationalId, toLatinDigits } from "../../lib/persian";

interface CustomerLookupProfileDto {
  id: string;
  fullName: string;
  firstName: string | null;
  lastName: string | null;
  nationalId: string | null;
  mobile: string | null;
  emergencyMobile: string | null;
  address: string | null;
  postalCode: string | null;
  isProfileComplete: boolean;
}

interface CustomerLookupResultDto {
  found: boolean;
  customer: CustomerLookupProfileDto | null;
  policyCount: number;
}

interface FormState {
  firstName: string;
  lastName: string;
  nationalId: string;
  mobile: string;
  emergencyMobile: string;
  postalCode: string;
  address: string;
}

const EMPTY: FormState = {
  firstName: "",
  lastName: "",
  nationalId: "",
  mobile: "",
  emergencyMobile: "",
  postalCode: "",
  address: "",
};

const INPUT_CLASS =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)";

const BTN_PRIMARY =
  "rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50";

const BTN_SECONDARY =
  "rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)";

/** Owner decision 2026-09-03 — registering a brand-new customer BEFORE any policy exists, so a
 * pre-issuance credit-check portal link can be sent on the very first visit. After creation the
 * natural next stop is the customer file, whose portal section sends the link. */
export function NewCustomerPage() {
  const tabKey = useTabKey();
  const setDirty = useTabsStore((s) => s.setDirty);
  const setTitle = useTabsStore((s) => s.setTitle);
  const openTab = useTabsStore((s) => s.openTab);

  const [form, setForm] = useState<FormState>(EMPTY);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [created, setCreated] = useState<CustomerLookupProfileDto | null>(null);

  useEffect(() => {
    setTitle(tabKey, created ? `مشتری — ${created.fullName}` : "مشتری جدید");
  }, [created, tabKey, setTitle]);

  function update<K extends keyof FormState>(field: K, value: string) {
    setForm((prev) => ({ ...prev, [field]: value }));
    setDirty(tabKey, true);
  }

  function validate(): string | null {
    if (!form.firstName.trim() || !form.lastName.trim()) {
      return "نام و نام خانوادگی الزامی است.";
    }
    if (!form.nationalId.trim() || !isValidNationalId(form.nationalId)) {
      return "کد ملی الزامی است و باید معتبر باشد.";
    }
    if (!form.mobile.trim()) {
      return "شمارهٔ همراه الزامی است.";
    }
    if (form.emergencyMobile.trim() && form.emergencyMobile.trim() === form.mobile.trim()) {
      return "موبایل اضطراری نباید با موبایل اصلی یکسان باشد.";
    }
    if (form.postalCode.trim() && toLatinDigits(form.postalCode.trim()).length !== 10) {
      return "کد پستی باید دقیقاً ۱۰ رقم باشد.";
    }
    if (form.address.trim() && form.address.trim().length < 10) {
      return "آدرس باید حداقل ۱۰ کاراکتر باشد.";
    }
    return null;
  }

  async function save() {
    const problem = validate();
    if (problem) {
      setError(problem);
      return;
    }

    setSaving(true);
    setError(null);
    try {
      const result = await api.post<CustomerLookupResultDto>("/customers", {
        firstName: form.firstName.trim(),
        lastName: form.lastName.trim(),
        nationalId: form.nationalId.trim(),
        mobile: form.mobile.trim(),
        emergencyMobile: form.emergencyMobile.trim() || null,
        postalCode: form.postalCode.trim() || null,
        address: form.address.trim() || null,
      });
      setCreated(result.customer);
      setDirty(tabKey, false);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "ثبت مشتری ناموفق بود.");
    } finally {
      setSaving(false);
    }
  }

  function openFile() {
    if (!created) return;
    openTab({
      navType: "customer-file",
      page: "customer-file",
      kind: "multi-record",
      recordId: created.id,
      title: created.fullName,
      payload: { customerId: created.id },
    });
  }

  function registerAnother() {
    setForm(EMPTY);
    setCreated(null);
    setError(null);
  }

  if (created) {
    return (
      <div>
        <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
          مشتری <em className="font-extralight not-italic text-(--ice-2)">جدید</em>
        </h2>
        <div className="mt-4 rounded-2xl border border-(--mint)/30 bg-(--mint)/8 p-5">
          <div className="text-[14px] font-bold text-(--ice)">
            {created.fullName} ثبت شد — کد ملی <b className="tabular-nums" dir="ltr">{fa(created.nationalId ?? "")}</b>
          </div>
          <div className="mt-1.5 text-[12.5px] leading-relaxed text-(--ice-2)">
            برای اعتبارسنجی قبل از صدور، پروندهٔ مشتری را باز کنید و از بخش پورتال «ارسال لینک پورتال» را بزنید.
            {created.isProfileComplete ? "" : " پرونده هنوز ناقص است و میتوانید بعداً از «تکمیل پروندهٔ مشتریان» کاملش کنید."}
          </div>
          <div className="mt-4 flex flex-wrap gap-2">
            <button type="button" onClick={openFile} className={BTN_PRIMARY}>
              باز کردن پروندهٔ مشتری
            </button>
            <button type="button" onClick={registerAnother} className={BTN_SECONDARY}>
              ثبت مشتری دیگر
            </button>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        مشتری <em className="font-extralight not-italic text-(--ice-2)">جدید</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
        ثبت مشتری قبل از صدور بیمه‌نامه — برای ارسال لینک اعتبارسنجی در نخستین مراجعه
      </div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="max-w-2xl rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="grid grid-cols-2 gap-3.5">
          <label className="block">
            <span className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">نام</span>
            <input value={form.firstName} onChange={(e) => update("firstName", e.target.value)} className={INPUT_CLASS} />
          </label>
          <label className="block">
            <span className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">نام خانوادگی</span>
            <input value={form.lastName} onChange={(e) => update("lastName", e.target.value)} className={INPUT_CLASS} />
          </label>
          <label className="block">
            <span className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">کد ملی</span>
            <input
              value={form.nationalId}
              onChange={(e) => update("nationalId", toLatinDigits(e.target.value).replace(/\D/g, "").slice(0, 10))}
              placeholder="۰۰۷۲۳۴۵۴۵۳"
              dir="ltr"
              className={`${INPUT_CLASS} tabular-nums`}
            />
          </label>
          <label className="block">
            <span className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">شمارهٔ همراه</span>
            <input
              value={form.mobile}
              onChange={(e) => update("mobile", toLatinDigits(e.target.value).replace(/\D/g, "").slice(0, 11))}
              placeholder="۰۹۱۲۳۴۵۶۷۸۹"
              dir="ltr"
              className={`${INPUT_CLASS} tabular-nums`}
            />
          </label>
          <label className="block">
            <span className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">شمارهٔ همراه اضطراری</span>
            <input
              value={form.emergencyMobile}
              onChange={(e) => update("emergencyMobile", toLatinDigits(e.target.value).replace(/\D/g, "").slice(0, 11))}
              placeholder="اختیاری"
              dir="ltr"
              className={`${INPUT_CLASS} tabular-nums`}
            />
          </label>
          <label className="block">
            <span className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">کد پستی</span>
            <input
              value={form.postalCode}
              onChange={(e) => update("postalCode", toLatinDigits(e.target.value).replace(/\D/g, "").slice(0, 10))}
              placeholder="اختیاری"
              dir="ltr"
              className={`${INPUT_CLASS} tabular-nums`}
            />
          </label>
          <label className="block col-span-2">
            <span className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">آدرس</span>
            <input value={form.address} onChange={(e) => update("address", e.target.value)} className={INPUT_CLASS} />
          </label>
        </div>
        <div className="mt-4">
          <button type="button" onClick={save} disabled={saving} className={BTN_PRIMARY}>
            {saving ? "در حال ثبت..." : "ثبت مشتری"}
          </button>
        </div>
      </div>
    </div>
  );
}
