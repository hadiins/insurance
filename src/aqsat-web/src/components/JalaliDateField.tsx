import { useEffect, useState } from "react";
import { fa, toLatinDigits } from "../lib/persian";
import { isoToJalaliText, jalaliTextToIso } from "../lib/jalali";

const inputClass =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] tabular-nums text-(--ice) outline-none focus:border-(--mint)";

/** docs/TASK-25-IDENTITY-VEHICLE.md §6.1 — "ورودی: انتخابگر شمسی + تایپ مستقیم 1405/05/23".
 * Direct-entry only (no popup calendar grid — out of scope for this pass): the value shown and
 * typed is always Jalali with Persian digits; `value`/`onChange` still carry the Gregorian ISO
 * date string the rest of the app already stores and sends to the API. */
export function JalaliDateField({
  value,
  onChange,
  className,
  placeholder,
}: {
  value: string;
  onChange: (isoDate: string) => void;
  className?: string;
  placeholder?: string;
}) {
  const [text, setText] = useState(() => isoToJalaliText(value));

  useEffect(() => {
    setText(isoToJalaliText(value));
  }, [value]);

  return (
    <input
      value={fa(text)}
      onChange={(e) => {
        const latin = toLatinDigits(e.target.value).replace(/[^\d/-]/g, "");
        setText(latin);
        const iso = jalaliTextToIso(latin);
        if (iso) {
          onChange(iso);
        }
      }}
      onBlur={() => {
        // Snap back to the last valid value if the field was left mid-typing or invalid.
        setText(isoToJalaliText(value));
      }}
      placeholder={placeholder ?? "۱۴۰۵/۰۵/۲۳"}
      className={className ?? inputClass}
    />
  );
}
