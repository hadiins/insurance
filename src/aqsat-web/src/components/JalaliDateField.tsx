import { useEffect, useState } from "react";
import { fa, toLatinDigits } from "../lib/persian";
import { isoToJalaliParts, isoToJalaliText, jalaliTextToIso, todayJalaliParts } from "../lib/jalali";
import { JalaliCalendarPopup } from "./JalaliCalendarPopup";

const inputClass =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] tabular-nums text-(--ice) outline-none focus:border-(--mint)";

/** docs/TASK-25-IDENTITY-VEHICLE.md §6.1 — "ورودی: انتخابگر شمسی + تایپ مستقیم 1405/05/23". Both
 * halves: direct typing in this field, or the calendar-icon button opens a month-grid popup. Either
 * way `value`/`onChange` carry the Gregorian ISO date string the rest of the app already uses. */
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
  const [open, setOpen] = useState(false);
  const [view, setView] = useState(() => isoToJalaliParts(value) ?? todayJalaliParts());

  useEffect(() => {
    setText(isoToJalaliText(value));
  }, [value]);

  function openPopup() {
    setView(isoToJalaliParts(value) ?? todayJalaliParts());
    setOpen(true);
  }

  return (
    <div className="relative">
      <div className="flex items-stretch gap-1">
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
        <button
          type="button"
          onClick={() => (open ? setOpen(false) : openPopup())}
          className="shrink-0 rounded-[10px] border border-(--edge-2) bg-(--fld) px-2.5 text-[14px] text-(--ice-3) transition-colors hover:bg-(--hov) hover:text-(--ice)"
          aria-label="نمایش تقویم"
        >
          📅
        </button>
      </div>
      {open && (
        <JalaliCalendarPopup
          viewYear={view.jy}
          viewMonth={view.jm}
          selected={isoToJalaliParts(value)}
          onNavigate={(jy, jm) => setView({ jy, jm, jd: view.jd })}
          onSelect={(iso) => {
            onChange(iso);
            setOpen(false);
          }}
          onClose={() => setOpen(false)}
        />
      )}
    </div>
  );
}
