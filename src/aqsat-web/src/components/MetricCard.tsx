import { fa, money } from "../lib/persian";

/** Metric card (TailAdmin's EcommerceMetrics pattern, on our tokens): icon tile + label +
 * formatted value + optional trend badge comparing against a previous value. Clickable when
 * onClick is given — a metric that navigates to its source list. */
export function MetricCard({
  icon,
  label,
  value,
  previousValue,
  previousLabel = "نسبت به دورهٔ قبل",
  invertTrend = false,
  tone = "neutral",
  onClick,
}: {
  icon: string;
  label: string;
  value: number;
  previousValue?: number;
  previousLabel?: string;
  /** When true, a value lower than the previous one reads as good (e.g. overdue balance). */
  invertTrend?: boolean;
  tone?: "mint" | "amber" | "ember" | "neutral";
  onClick?: () => void;
}) {
  const delta =
    previousValue !== undefined && previousValue !== 0 ? ((value - previousValue) / Math.abs(previousValue)) * 100 : undefined;
  const trendGood = delta === undefined ? false : invertTrend ? delta <= 0 : delta >= 0;
  const trendTone = delta === undefined ? "neutral" : trendGood ? "mint" : "ember";
  const trendText =
    delta === undefined
      ? ""
      : `${delta > 0 ? "▲" : delta < 0 ? "▼" : "▬"} ${fa(Math.abs(Math.round(delta)))}٪`;

  const valueTone =
    tone === "mint" ? "text-(--mint)" : tone === "amber" ? "text-(--amber)" : tone === "ember" ? "text-(--ember)" : "text-(--ice)";

  return (
    <div
      onClick={onClick}
      className={`rounded-2xl border border-(--edge) bg-(--pane) p-4.5 ${onClick ? "cursor-pointer transition-colors hover:bg-(--hov)" : ""}`}
    >
      <div className="mb-3 flex items-center justify-between">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-(--fld) text-lg">{icon}</div>
        {delta !== undefined && delta !== 0 && (
          <span
            className={`rounded-full px-2 py-0.5 text-[10.5px] font-semibold ${
              trendTone === "mint" ? "bg-(--mint)/12 text-(--mint)" : "bg-(--ember)/13 text-(--ember)"
            }`}
            title={`${fa(Math.round(delta))}٪ ${previousLabel}`}
          >
            {trendText}
          </span>
        )}
      </div>
      <div className="text-[10.5px] tracking-[0.14em] text-(--ice-3)">{label}</div>
      <div className={`mt-1.5 text-[20px] font-extrabold tabular-nums ${valueTone}`}>{money(value)}</div>
    </div>
  );
}
