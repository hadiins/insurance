import { useRef } from "react";
import { toLatinDigits, fa } from "../../lib/persian";

export interface PlateParts {
  plateType: number;
  twoDigit: string;
  letter: string;
  threeDigit: string;
  iranCode: string;
}

export const EMPTY_PLATE: PlateParts = { plateType: 1, twoDigit: "", letter: "", threeDigit: "", iranCode: "" };

// docs/TASK-25-IDENTITY-VEHICLE.md §5.2 — code, label, border color, allowed letters (empty = all
// normal letters). Numeric codes match Aqsat.Domain.Enums.PlateType exactly.
const PLATE_TYPES: { code: number; label: string; border: string; letters: string[] | null }[] = [
  { code: 1, label: "شخصی", border: "border-(--edge-2)", letters: null },
  { code: 2, label: "عمومی / تاکسی", border: "border-yellow-500", letters: ["ت", "ع"] },
  { code: 3, label: "دولتی", border: "border-red-500", letters: ["الف"] },
  { code: 4, label: "نظامی", border: "border-green-700", letters: ["ز", "ث"] },
  { code: 5, label: "کشاورزی و عمرانی", border: "border-orange-500", letters: null },
  { code: 6, label: "معلولین و جانبازان", border: "border-blue-500", letters: ["ژ"] },
  { code: 7, label: "تشریفات / دیپلمات", border: "border-blue-500", letters: ["D", "S"] },
  { code: 8, label: "تجاری / منطقهٔ آزاد", border: "border-emerald-500", letters: null },
];

const NORMAL_LETTERS = [
  "ب", "پ", "ت", "ث", "ج", "د", "ز", "ژ", "س", "ش", "ص", "ط", "ع", "ف", "ق", "ک", "گ", "ل", "م", "ن", "و", "ه", "ی",
];

function lettersFor(plateType: number): string[] {
  const type = PLATE_TYPES.find((t) => t.code === plateType);
  return type?.letters ?? NORMAL_LETTERS;
}

function onlyDigits(v: string, maxLen: number): string {
  return toLatinDigits(v).replace(/\D/g, "").slice(0, maxLen);
}

/** docs/TASK-25-IDENTITY-VEHICLE.md §5 — structured plate input: two digits, a letter dropdown
 * (never free text), three digits, and the "ایران" code, with auto-advance between fields and a
 * border color driven by plate type. */
export function PlateField({ value, onChange }: { value: PlateParts; onChange: (v: PlateParts) => void }) {
  const letterRef = useRef<HTMLSelectElement>(null);
  const threeRef = useRef<HTMLInputElement>(null);
  const iranRef = useRef<HTMLInputElement>(null);

  const type = PLATE_TYPES.find((t) => t.code === value.plateType) ?? PLATE_TYPES[0];
  const allowedLetters = lettersFor(value.plateType);

  function setPlateType(code: number) {
    const allowed = lettersFor(code);
    onChange({
      ...value,
      plateType: code,
      letter: allowed.includes(value.letter) ? value.letter : "",
    });
  }

  return (
    <div>
      <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">شماره پلاک</label>
      <div className="mb-1.5">
        <select
          value={value.plateType}
          onChange={(e) => setPlateType(Number(e.target.value))}
          className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12px] text-(--ice) outline-none focus:border-(--mint)"
        >
          {PLATE_TYPES.map((t) => (
            <option key={t.code} value={t.code}>
              {t.label}
            </option>
          ))}
        </select>
      </div>
      <div className="flex items-stretch gap-1.5" dir="ltr">
        <input
          value={fa(value.twoDigit)}
          onChange={(e) => {
            const digits = onlyDigits(e.target.value, 2);
            onChange({ ...value, twoDigit: digits });
            if (digits.length === 2) {
              letterRef.current?.focus();
            }
          }}
          placeholder="۵۵"
          className={`w-12 rounded-[8px] border-2 ${type.border} bg-(--fld) px-2 py-2 text-center text-[14px] tabular-nums text-(--ice) outline-none`}
        />
        <select
          ref={letterRef}
          value={value.letter}
          onChange={(e) => {
            onChange({ ...value, letter: e.target.value });
            if (e.target.value) {
              threeRef.current?.focus();
            }
          }}
          className={`w-20 rounded-[8px] border-2 ${type.border} bg-(--fld) px-1 py-2 text-center text-[14px] text-(--ice) outline-none`}
        >
          <option value=""></option>
          {allowedLetters.map((l) => (
            <option key={l} value={l}>
              {l}
            </option>
          ))}
        </select>
        <input
          ref={threeRef}
          value={fa(value.threeDigit)}
          onChange={(e) => {
            const digits = onlyDigits(e.target.value, 3);
            onChange({ ...value, threeDigit: digits });
            if (digits.length === 3) {
              iranRef.current?.focus();
            }
          }}
          placeholder="۵۵۵"
          className={`w-16 rounded-[8px] border-2 ${type.border} bg-(--fld) px-2 py-2 text-center text-[14px] tabular-nums text-(--ice) outline-none`}
        />
        <div className="flex flex-col items-center justify-center rounded-[8px] bg-blue-900/40 px-2 text-[9px] leading-tight text-blue-200">
          <span>I.R.</span>
          <span>IRAN</span>
        </div>
        <input
          ref={iranRef}
          value={fa(value.iranCode)}
          onChange={(e) => onChange({ ...value, iranCode: onlyDigits(e.target.value, 2) })}
          placeholder="۵۵"
          className={`w-12 rounded-[8px] border-2 ${type.border} bg-(--fld) px-2 py-2 text-center text-[14px] tabular-nums text-(--ice) outline-none`}
        />
      </div>
    </div>
  );
}

export function isPlateFilled(v: PlateParts): boolean {
  return Boolean(v.twoDigit && v.letter && v.threeDigit && v.iranCode);
}
