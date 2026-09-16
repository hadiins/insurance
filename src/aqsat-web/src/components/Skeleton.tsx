export function SkeletonBlock({ className = "" }: { className?: string }) {
  return <div aria-hidden className={`skeleton ${className}`} />;
}

/** Shaped like a metric rail: one pane, four hairline-divided columns. Matches the
 * "no card per figure" layout the dashboard pages use. */
export function SkeletonRail({ cells = 4 }: { cells?: number }) {
  return (
    <div
      className="grid overflow-hidden rounded-(--r-lg) border border-(--edge) bg-(--pane)"
      style={{ gridTemplateColumns: `repeat(${cells}, minmax(0, 1fr))` }}
      role="status"
      aria-label="در حال بارگذاری"
    >
      {Array.from({ length: cells }, (_, i) => (
        <div key={i} className="border-s border-(--edge) p-3.5 first:border-s-0">
          <SkeletonBlock className="mb-2 h-2.5 w-16" />
          <SkeletonBlock className="mb-2 h-5 w-24" />
          <SkeletonBlock className="h-2.5 w-20" />
        </div>
      ))}
    </div>
  );
}

export function SkeletonRows({ rows = 5, cols = 4 }: { rows?: number; cols?: number }) {
  const widths = ["w-40", "w-20", "w-24", "w-20", "w-16"];
  return (
    <div className="rounded-(--r-lg) border border-(--edge) bg-(--pane) px-3" role="status" aria-label="در حال بارگذاری">
      {Array.from({ length: rows }, (_, r) => (
        <div key={r} className="flex items-center gap-4 border-t border-(--edge) py-3.5 first:border-t-0">
          {Array.from({ length: cols }, (_, c) => (
            <SkeletonBlock key={c} className={`h-3 ${widths[c % widths.length]}`} />
          ))}
        </div>
      ))}
    </div>
  );
}
