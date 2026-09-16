import type { Icon } from "@phosphor-icons/react";

/** Rich empty state (FinSync's pattern) — a heading, an explanation, and an optional
 * call-to-action. The third leg of the loading/empty/error triad every list must render
 * (CLAUDE.md rule 16): a plain "موردی یافت نشد" line tells the agent nothing about what
 * to do next.
 *
 * `icon` is a Phosphor icon component, not a glyph. The call site passes the component
 * itself (`icon={ReceiptIcon}`) so each page chunk carries only the icons it uses.
 * Omit it when there is nothing honest to put in the circle and let the heading carry
 * the state. */
export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
}: {
  icon?: Icon;
  title: string;
  description?: string;
  action?: { label: string; onClick: () => void };
}) {
  return (
    <div className="rounded-(--r-lg) border border-(--edge) bg-(--pane) px-4 py-12 text-center">
      {Icon && (
        <div className="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-full bg-(--fld)">
          <Icon size={26} className="text-(--ice-2)" />
        </div>
      )}
      <div className="mb-1.5 text-[13.5px] font-semibold text-(--ice)">{title}</div>
      {description && <div className="mx-auto mb-5 max-w-sm text-[12.5px] leading-relaxed text-(--ice-3)">{description}</div>}
      {action && (
        <button
          type="button"
          onClick={action.onClick}
          className="rounded-(--r) border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
        >
          {action.label}
        </button>
      )}
    </div>
  );
}
