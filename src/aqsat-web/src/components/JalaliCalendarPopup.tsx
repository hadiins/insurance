import { fa } from "../lib/persian";
import { jalaaliMonthLength, jalaliFirstWeekdayOffset, jalaliPartsToIso, todayJalaliParts } from "../lib/jalali";

const MONTH_NAMES = [
  "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
  "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند",
];
const WEEKDAY_LABELS = ["ش", "ی", "د", "س", "چ", "پ", "ج"];

interface Props {
  viewYear: number;
  viewMonth: number;
  selected: { jy: number; jm: number; jd: number } | null;
  onNavigate: (jy: number, jm: number) => void;
  onSelect: (iso: string) => void;
  onClose: () => void;
}

/** docs/TASK-25-IDENTITY-VEHICLE.md §6.1 — the popup half of "انتخابگر شمسی + تایپ مستقیم".
 * A plain month grid, Saturday-first, with today and the selected day marked. */
export function JalaliCalendarPopup({ viewYear, viewMonth, selected, onNavigate, onSelect, onClose }: Props) {
  const monthLength = jalaaliMonthLength(viewYear, viewMonth);
  const offset = jalaliFirstWeekdayOffset(viewYear, viewMonth);
  const today = todayJalaliParts();

  function goPrevMonth() {
    if (viewMonth === 1) onNavigate(viewYear - 1, 12);
    else onNavigate(viewYear, viewMonth - 1);
  }

  function goNextMonth() {
    if (viewMonth === 12) onNavigate(viewYear + 1, 1);
    else onNavigate(viewYear, viewMonth + 1);
  }

  const cells: (number | null)[] = [...Array(offset).fill(null), ...Array.from({ length: monthLength }, (_, i) => i + 1)];

  return (
    <>
      <div className="fixed inset-0 z-40" onClick={onClose} />
      <div className="absolute top-full z-50 mt-1.5 w-64 rounded-[12px] border border-(--edge) bg-(--pane) p-3 shadow-xl">
        <div className="mb-2 flex items-center justify-between">
          <button type="button" onClick={goPrevMonth} className="rounded-[6px] px-2 py-1 text-[13.5px] text-(--ice-2) hover:bg-(--hov)">
            ›
          </button>
          <div className="text-[12.5px] font-semibold text-(--ice)">
            {MONTH_NAMES[viewMonth - 1]} {fa(viewYear)}
          </div>
          <button type="button" onClick={goNextMonth} className="rounded-[6px] px-2 py-1 text-[13.5px] text-(--ice-2) hover:bg-(--hov)">
            ‹
          </button>
        </div>
        <div className="grid grid-cols-7 gap-0.5 text-center">
          {WEEKDAY_LABELS.map((w) => (
            <div key={w} className="py-1 text-[10.5px] text-(--ice-3)">
              {w}
            </div>
          ))}
          {cells.map((day, i) => {
            if (day === null) {
              return <div key={`empty-${i}`} />;
            }
            const isToday = today.jy === viewYear && today.jm === viewMonth && today.jd === day;
            const isSelected = selected?.jy === viewYear && selected?.jm === viewMonth && selected?.jd === day;
            return (
              <button
                key={day}
                type="button"
                onClick={() => onSelect(jalaliPartsToIso(viewYear, viewMonth, day))}
                className={`rounded-[6px] py-1.5 text-[12.5px] tabular-nums transition-colors ${
                  isSelected
                    ? "bg-(--mint) font-bold text-(--on-mint)"
                    : isToday
                      ? "border border-(--mint)/50 text-(--mint)"
                      : "text-(--ice-2) hover:bg-(--hov)"
                }`}
              >
                {fa(day)}
              </button>
            );
          })}
        </div>
        <button
          type="button"
          onClick={() => onSelect(jalaliPartsToIso(today.jy, today.jm, today.jd))}
          className="mt-2 w-full rounded-[8px] border border-(--edge-2) bg-(--btn-bg) py-1.5 text-[11.5px] text-(--ice-2) transition-colors hover:bg-(--btn-hov)"
        >
          امروز
        </button>
      </div>
    </>
  );
}
