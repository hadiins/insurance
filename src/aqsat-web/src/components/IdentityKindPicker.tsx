import { IdentificationCardIcon, SealCheckIcon, GlobeHemisphereEastIcon } from "@phosphor-icons/react";
import type { CustomerKind } from "../lib/persian";

/*
 * Identity-kind selector — the one control where the operator tells the system WHICH
 * rules apply to a human being (checksum inquiry / 996 inquiry / internal-only). A row
 * of anonymous pills is not enough for that: each option is a small card that states
 * its CONSEQUENCE, so choosing is reading, not recalling. Semantics stay a radiogroup.
 */

const KIND_META: {
  key: CustomerKind;
  title: string;
  sub: string;
  Icon: typeof IdentificationCardIcon;
}[] = [
  {
    key: "Iranian",
    title: "ایرانی",
    sub: "کد ملی ۱۰ رقمی — چک‌سام و استعلام کامل",
    Icon: IdentificationCardIcon,
  },
  {
    key: "ForeignResident",
    title: "اتباع دارای کد ملی",
    sub: "سری ۹۹۶ — استعلام با همین کد انجام میشود",
    Icon: SealCheckIcon,
  },
  {
    key: "ForeignPassportOnly",
    title: "اتباع بدون کد ملی",
    sub: "پاسپورت — استعلام خارجی اعمال نمیشود",
    Icon: GlobeHemisphereEastIcon,
  },
];

export function IdentityKindPicker({
  value,
  onChange,
  compact = false,
}: {
  value: CustomerKind;
  onChange: (kind: CustomerKind) => void;
  /** compact = one-line cards for the crowded wizard step; default = full cards. */
  compact?: boolean;
}) {
  return (
    <div role="radiogroup" aria-label="نوع هویت مشتری" className={compact ? "grid gap-2 sm:grid-cols-3" : "grid gap-2.5 sm:grid-cols-3"}>
      {KIND_META.map(({ key, title, sub, Icon }, i) => {
        const selected = value === key;
        return (
          <button
            key={key}
            type="button"
            role="radio"
            aria-checked={selected}
            onClick={() => onChange(key)}
            className={`aqsat-rise group relative flex items-start gap-2.5 rounded-(--r) border px-3 py-2.5 text-right transition-all ${
              selected
                ? "border-(--mint) bg-(--mint)/8 shadow-[var(--ring)]"
                : "border-(--edge-2) bg-(--btn-bg) hover:border-(--mint)/45 hover:bg-(--hov)"
            } ${compact ? "flex-col" : ""}`}
            style={{ animationDelay: `${i * 60}ms` }}
          >
            <span
              aria-hidden
              className={`mt-0.5 grid size-7 flex-none place-items-center rounded-(--r-sharp) transition-colors ${
                selected ? "bg-(--mint) text-(--on-mint)" : "bg-(--fld) text-(--ice-3) group-hover:text-(--mint)"
              }`}
            >
              <Icon weight={selected ? "fill" : "regular"} className="size-4" />
            </span>
            <span className="min-w-0">
              <span className={`block text-[12px] leading-5 ${selected ? "font-bold text-(--ice)" : "font-semibold text-(--ice-2)"}`}>
                {title}
              </span>
              <span className="mt-0.5 block text-[10.5px] leading-4 text-(--ice-3)">{sub}</span>
            </span>
          </button>
        );
      })}
    </div>
  );
}
