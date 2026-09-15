/*
 * Shared form primitives — the single home of the control class strings that were
 * copy-pasted inline (and re-declared locally as INPUT_CLASS / BTN_*) across pages.
 * Two sizes: the default (forms, dialogs) and the -SM variants (dense rows, toolbars).
 * Tailwind picks these up from source text like any literal, so composing
 * `className={`${INPUT_CLASS} mb-3`}` keeps working.
 */

const inputBase =
  "w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-(--ice) outline-none focus:border-(--mint)";

export const INPUT_CLASS = `${inputBase} text-[13.5px]`;
export const INPUT_CLASS_SM = `${inputBase} text-[12.5px]`;

/* Same look without w-full — for controls that size themselves inside a flex row
 * or grid cell instead of stretching. */
export const INPUT_INLINE_SM =
  "rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)";

/* Compact filter select above list tables. No w-full — it sizes itself inside the
 * filter row. */
export const FILTER_SELECT =
  "rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)";

export const BTN_PRIMARY =
  "rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50";

export const BTN_PRIMARY_SM =
  "rounded-[10px] border border-(--mint) bg-(--mint) px-3.5 py-1.5 text-[11.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50";

export const BTN_SECONDARY =
  "rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)";

export const BTN_SECONDARY_SM =
  "rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-3.5 py-1.5 text-[11.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice) disabled:cursor-not-allowed disabled:opacity-50";

export const BTN_DANGER =
  "rounded-[10px] border border-(--ember)/60 bg-(--ember)/10 px-3.5 py-1.5 text-[11.5px] font-semibold text-(--ember) transition-colors hover:bg-(--ember)/20 disabled:cursor-not-allowed disabled:opacity-50";

/** Standard labelled-control wrapper: the label styling every form field shares. */
export function Field({
  label,
  htmlFor,
  className = "",
  children,
}: {
  label: string;
  htmlFor?: string;
  className?: string;
  children: React.ReactNode;
}) {
  return (
    <div className={className}>
      <label htmlFor={htmlFor} className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">
        {label}
      </label>
      {children}
    </div>
  );
}

/** The shared ember error box (role=alert so screen readers announce it). */
export function ErrorNote({ children }: { children: React.ReactNode }) {
  return (
    <div role="alert" className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
      {children}
    </div>
  );
}
