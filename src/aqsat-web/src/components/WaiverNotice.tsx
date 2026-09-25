import { ProhibitIcon } from "@phosphor-icons/react";
import type { ReactNode } from "react";

/*
 * WaiverNotice — the single home of the «اعمال نشد» visual language. A deliberate
 * regulatory skip is NOT an error and NOT an empty result, so it must not borrow the
 * error red or the success green: it gets its own quiet amber panel with a diagonal
 * shield-slash mark. Every surface that reports the skip (verification step, risk
 * panel) renders through this component so the pattern stays identical everywhere.
 */
export function WaiverNotice({
  title,
  children,
  className = "",
}: {
  title: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <div
      role="note"
      className={`relative overflow-hidden rounded-(--r) border border-(--amber)/30 bg-(--amber)/8 px-3.5 py-3 ${className}`}
    >
      <div
        aria-hidden
        className="pointer-events-none absolute inset-y-0 right-0 w-[3px]"
        style={{ background: "linear-gradient(180deg, var(--amber), transparent)" }}
      />
      <div className="flex items-start gap-2.5">
        <ProhibitIcon aria-hidden weight="duotone" className="mt-0.5 size-4 flex-none text-(--amber)" />
        <div className="min-w-0 text-[12.5px] leading-relaxed text-(--amber)">
          <b className="font-bold">{title}</b> {children}
        </div>
      </div>
    </div>
  );
}
