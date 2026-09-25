import { useCallback, useEffect, useRef, useState } from "react";
import { moneyShort } from "../../components/TrendBars";
import { isoToJalaliParts } from "../../lib/jalali";
import { fa, money } from "../../lib/persian";

export interface GroupedTrendDay {
  /** ISO civil date — the same day the backend bucketed the payments into. */
  date: string;
  collected: number;
  goalRemaining: number;
}

const SERIES = {
  collected: { label: "وصول‌شده", fill: "fill-(--mint)" },
  goal: { label: "باقی‌ماندهٔ هدف روز", fill: "fill-(--edge-2)" },
} as const;

/** Two columns per day: what was actually collected, and how much of that day's goal was still
 * outstanding. Grouped rather than stacked — the two are read against the same axis, and stacking
 * them would imply the goal bar sits on top of the collected one.
 *
 * When the agency has set no monthly goal there is no second series at all: `hasGoal` false draws
 * the collected bars alone. A zero-height goal bar behind every day would look like a target the
 * agency is failing, when in fact none has been configured. */
export function GroupedTrendBars({
  data,
  height = 240,
  hasGoal,
  title = "روند وصول چهارده روز گذشته",
}: {
  data: GroupedTrendDay[];
  height?: number;
  hasGoal: boolean;
  title?: string;
}) {
  const observerRef = useRef<ResizeObserver | null>(null);
  const [width, setWidth] = useState(0);
  const [hovered, setHovered] = useState<number | null>(null);

  useEffect(() => () => observerRef.current?.disconnect(), []);

  const attachRef = useCallback((el: HTMLDivElement | null) => {
    observerRef.current?.disconnect();
    if (el) {
      const observer = new ResizeObserver((entries) => setWidth(Math.floor(entries[0].contentRect.width)));
      observer.observe(el);
      observerRef.current = observer;
    } else {
      observerRef.current = null;
    }
  }, []);

  const hasData = data.length > 0;

  const padTop = 22;
  const padBottom = 34;
  const padRight = 52;
  const padLeft = 4;
  const plotW = Math.max(width - padLeft - padRight, 40);
  const plotH = height - padTop - padBottom;

  const max = Math.max(...data.flatMap((d) => [d.collected, hasGoal ? d.goalRemaining : 0]), 0);
  const span = max || 1;
  const yFor = (v: number) => padTop + plotH * (1 - v / span);

  const slot = plotW / data.length;
  const barsPerDay = hasGoal ? 2 : 1;
  const gap = 2;
  const groupW = Math.min(46, slot * 0.72);
  const barWidth = Math.max(3, (groupW - gap * (barsPerDay - 1)) / barsPerDay);
  const dayEvery = Math.max(1, Math.ceil(30 / slot));

  const ticks = Array.from({ length: 4 }, (_, i) => (span * i) / 3);

  return (
    <div ref={attachRef} className="w-full" style={hasData ? { height } : undefined}>
      {hasData && width > 0 ? (
        <svg width={width} height={height} role="img" aria-label={title}>
          {ticks.map((t, i) => (
            <g key={i}>
              <line
                x1={padLeft}
                x2={padLeft + plotW}
                y1={yFor(t)}
                y2={yFor(t)}
                className="stroke-(--edge)"
                strokeWidth={1}
                strokeDasharray="3 4"
              />
              <text x={padLeft + plotW + 8} y={yFor(t) + 3.5} className="fill-(--ice-3) text-[10.5px] tabular-nums">
                {moneyShort(t)}
              </text>
            </g>
          ))}

          {data.map((d, i) => {
            const groupStart = padLeft + i * slot + (slot - groupW) / 2;
            const dim = hovered !== null && hovered !== i;
            const jalali = isoToJalaliParts(d.date);
            const label = jalali ? `${fa(jalali.jd)} ${fa(jalali.jm)}` : d.date;

            const bars: { key: string; value: number; fill: string; x: number }[] = [
              { key: "collected", value: d.collected, fill: SERIES.collected.fill, x: groupStart },
            ];
            if (hasGoal) {
              bars.push({
                key: "goal",
                value: d.goalRemaining,
                fill: SERIES.goal.fill,
                x: groupStart + barWidth + gap,
              });
            }

            return (
              <g
                key={d.date}
                onMouseEnter={() => setHovered(i)}
                onMouseLeave={() => setHovered(null)}
                className="cursor-default"
              >
                <title>
                  {hasGoal
                    ? `${label}: وصول ${money(d.collected)} · باقی‌ماندهٔ هدف ${money(d.goalRemaining)}`
                    : `${label}: وصول ${money(d.collected)}`}
                </title>
                <rect x={padLeft + i * slot} y={padTop} width={slot} height={plotH} className="fill-transparent" />

                {bars.map((bar) => {
                  const barHeight = bar.value <= 0 ? 0 : Math.max(1.5, plotH * (bar.value / span));
                  return (
                    <rect
                      key={bar.key}
                      x={bar.x}
                      y={padTop + plotH - barHeight}
                      width={barWidth}
                      height={barHeight}
                      rx={Math.min(3, barWidth / 2)}
                      className={bar.fill}
                      opacity={dim ? 0.4 : 0.95}
                    />
                  );
                })}

                {i % dayEvery === 0 && (
                  <text
                    x={padLeft + i * slot + slot / 2}
                    y={height - 14}
                    textAnchor="middle"
                    className={`text-[10.5px] tabular-nums ${
                      hovered === i ? "fill-(--ice) font-semibold" : "fill-(--ice-3)"
                    }`}
                  >
                    {label}
                  </text>
                )}
              </g>
            );
          })}
        </svg>
      ) : hasData ? null : (
        <div className="grid h-full place-items-center py-6 text-[12.5px] text-(--ice-3)">
          در این بازه پرداختی ثبت نشده است.
        </div>
      )}

      {/* Two series, so identity can never rest on colour alone — the legend is always present when
          the goal series is drawn, and the tooltip names which figure is which. */}
      {hasGoal && (
        <div className="mt-1 flex flex-wrap items-center gap-4 text-[11px] text-(--ice-3)">
          <span className="flex items-center gap-1.5">
            <span className="inline-block h-2 w-2 rounded-[2px] bg-(--mint)" />
            {SERIES.collected.label}
          </span>
          <span className="flex items-center gap-1.5">
            <span className="inline-block h-2 w-2 rounded-[2px] bg-(--edge-2)" />
            {SERIES.goal.label}
          </span>
        </div>
      )}
    </div>
  );
}
