export function SkeletonBlock({ className = "" }: { className?: string }) {
  return <div aria-hidden className={`skeleton ${className}`} />;
}

export function SkeletonCards({ count = 4 }: { count?: number }) {
  return (
    <div className="grid grid-cols-4 gap-3" role="status" aria-label="در حال بارگذاری">
      {Array.from({ length: count }, (_, i) => (
        <div key={i} className="rounded-(--r-lg) border border-(--edge) bg-(--pane) p-3.5">
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
