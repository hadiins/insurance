import { useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";

interface FormState {
  plate: string;
  nationalId: string;
  mobile: string;
  premium: string;
  count: string;
  collateral: string;
  desc: string;
}

const EMPTY: FormState = {
  plate: "",
  nationalId: "",
  mobile: "",
  premium: "",
  count: "۳",
  collateral: "چک صیادی",
  desc: "",
};

export function NewPolicyPage() {
  const tabKey = useTabKey();
  const setDirty = useTabsStore((s) => s.setDirty);
  const setTitle = useTabsStore((s) => s.setTitle);
  const [form, setForm] = useState<FormState>(EMPTY);

  function update<K extends keyof FormState>(field: K, value: string) {
    const next = { ...form, [field]: value };
    setForm(next);
    setDirty(tabKey, true);
    if (field === "plate") {
      setTitle(tabKey, value.trim() ? `بیمه‌نامه — ${value.trim()}` : "ثبت بیمه‌نامه");
    }
  }

  function save() {
    setDirty(tabKey, false);
  }

  function reset() {
    setForm(EMPTY);
    setDirty(tabKey, false);
    setTitle(tabKey, "ثبت بیمه‌نامه");
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        ثبت <em className="font-extralight not-italic text-(--ice-2)">بیمه‌نامهٔ جدید</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">فرم نیمه‌تمام هنگام جابه‌جایی بین تب‌ها حفظ می‌شود</div>

      <div className="mb-4.5 rounded-xl border border-(--mint)/22 bg-(--mint)/7 p-4 text-[12.5px] text-(--ice-2)">
        چند فیلد را پر کنید، بعد تب دیگری باز کنید و برگردید. <b className="font-bold text-(--mint)">مقادیر سرِ جایشان
        هستند</b> و نقطهٔ نارنجی روی تب یعنی ذخیره نشده.
      </div>

      <div className="rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="grid grid-cols-2 gap-3.5">
          <Field label="کد ملی بیمه‌گذار" value={form.nationalId} onChange={(v) => update("nationalId", v)} placeholder="۰۰۷۲۳۴۵۴۵۳" />
          <Field label="شمارهٔ همراه" value={form.mobile} onChange={(v) => update("mobile", v)} placeholder="۰۹۱۲۳۴۵۶۷۸۹" />
          <Field label="شماره پلاک" value={form.plate} onChange={(v) => update("plate", v)} placeholder="۷۴ ب ۳۲۱ ایران ۶۳" />
          <Field label="حق بیمهٔ کل (تومان)" value={form.premium} onChange={(v) => update("premium", v)} placeholder="۹٬۸۰۰٬۰۰۰" />
          <SelectField
            label="تعداد اقساط"
            value={form.count}
            onChange={(v) => update("count", v)}
            options={["۲", "۳", "۴", "۶"]}
          />
          <SelectField
            label="نوع وثیقه"
            value={form.collateral}
            onChange={(v) => update("collateral", v)}
            options={["چک صیادی", "سفته", "بدون وثیقه"]}
          />
        </div>
        <div className="mt-3.5">
          <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">توضیح بیمه‌نامه</label>
          <textarea
            value={form.desc}
            onChange={(e) => update("desc", e.target.value)}
            rows={2}
            placeholder="اختیاری"
            className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
          />
        </div>
        <div className="mt-4 flex gap-2">
          <button
            type="button"
            onClick={save}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105"
          >
            ذخیره
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
          در نسخهٔ واقعی، این فرم فیلد متن آزاد دربارهٔ <b className="font-bold">شخص</b> ندارد — فقط دربارهٔ
          بیمه‌نامه.
        </div>
      </div>
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

function SelectField({
  label,
  value,
  onChange,
  options,
}: {
  label: string;
  value: string;
  onChange: (v: string) => void;
  options: string[];
}) {
  return (
    <div>
      <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">{label}</label>
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
      >
        {options.map((o) => (
          <option key={o} value={o}>
            {o}
          </option>
        ))}
      </select>
    </div>
  );
}
