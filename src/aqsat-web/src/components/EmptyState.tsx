/** Rich empty state (FinSync's pattern) — icon in a muted circle, a heading, an explanation,
 * and an optional call-to-action. The third leg of the loading/empty/error triad every list
 * must render (CLAUDE.md rule 16): a plain "موردی یافت نشد" line tells the agent nothing
 * about what to do next. */
export function EmptyState({
  icon,
  title,
  description,
  action,
}: {
  icon: string;
  title: string;
  description?: string;
  action?: { label: string; onClick: () => void };
}) {
  return (
    <div className="rounded-2xl border border-(--edge) bg-(--pane) px-4 py-12 text-center">
      <div className="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-full bg-(--fld) text-2xl">
        {icon}
      </div>
      <div className="mb-1.5 text-[13.5px] font-semibold text-(--ice)">{title}</div>
      {description && <div className="mx-auto mb-5 max-w-sm text-[12.5px] leading-relaxed text-(--ice-3)">{description}</div>}
      {action && (
        <button
          type="button"
          onClick={action.onClick}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
        >
          {action.label}
        </button>
      )}
    </div>
  );
}
