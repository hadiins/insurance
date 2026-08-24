import { fa, toLatinDigits } from "../lib/persian";

const inputClass =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] tabular-nums text-(--ice) outline-none focus:border-(--mint)";

/** CLAUDE.md — "Tabular numerals for every figure" / "Persian digits in all display". A plain
 * digit-only input is hard to read once amounts hit six or seven digits — this groups by
 * thousands (٬) with Persian digits while typing, same separator convention lib/persian's money()
 * already uses for read-only display, while `value`/`onChange` still carry the raw digit string
 * the rest of the app sends to the API. */
export function MoneyInput({
  value,
  onChange,
  placeholder,
  className,
}: {
  value: string;
  onChange: (v: string) => void;
  placeholder?: string;
  className?: string;
}) {
  const display = value ? fa(Number(value).toLocaleString("en-US")).replace(/,/g, "٬") : "";

  return (
    <input
      value={display}
      onChange={(e) => onChange(toLatinDigits(e.target.value).replace(/[^\d]/g, ""))}
      placeholder={placeholder}
      inputMode="numeric"
      className={className ?? inputClass}
    />
  );
}
