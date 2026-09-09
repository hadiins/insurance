import { useCallback, useEffect, useRef, useState } from "react";
import { fa, money } from "../lib/persian";

export interface TrendBarPoint {
  label: string;
  value: number;
}

/** Compact Persian amount for chart labels and axis ticks — the full figure stays in the tooltip. */
function moneyShort(n: number): string {
  const abs = Math.abs(n);
  const sign = n < 0 ? "−" : "";
  if (abs >= 1_000_000_000) return `${sign}${fa(trim(abs / 1_000_000_000))} میلیارد`;
  if (abs >= 1_000_000) return `${sign}${fa(trim(abs / 1_000_000))} میلیون`;
  if (abs >= 1_000) return `${sign}${fa(Math.round(abs / 1_000))} هزار`;
  return `${sign}${fa(Math.round(abs))}`;
}

function trim(v: number): string {
  return Number(v.toFixed(1)).toString().replace(".", "٫");
}

/** Path for a bar rounded only on its top edge (positive bars) or bottom edge (negative bars). */
function barPath(x: number, y: number, w: number, h: number, r: number, roundTop: boolean): string {
  const radius = Math.min(r, h, w / 2);
  const opposite = y + h;
  if (roundTop) {
    return `M${x},${opposite} V${y + radius} Q${x},${y} ${x + radius},${y} H${x + w - radius} Q${x + w},${y} ${x + w},${y + radius} V${opposite} Z`;
  }
  return `M${x + radius},${y} H${x + w - radius} Q${x + w},${y} ${x + w},${y + radius} V${opposite} Q${x + w},${opposite} ${x + w - radius},${opposite} H${x + radius} Q${x},${opposite} ${x},${opposite - radius} V${y + radius} Q${x},${y} ${x + radius},${y} Z`;
}

/** Dependency-free SVG column chart sized to its container (ResizeObserver, no fixed pixel width).
 * Positive columns grow in mint, negative ones hang in ember; month/year labels and value figures
 * thin themselves out as the slot width shrinks, and every bar keeps a native tooltip. */
export function TrendBars({ data, height = 240, title }: { data: TrendBarPoint[]; height?: number; title?: string }) {
  const observerRef = useRef<ResizeObserver | null>(null);
  const [width, setWidth] = useState(0);
  const [hovered, setHovered] = useState<number | null>(null);

  // The wrapper div is rendered by every branch below, but its identity can still change when the
  // component transitions between the empty-data and chart states. A callback ref (re)attaches the
  // observer to whatever node is live — a mount-time useEffect would run once, find no node in the
  // empty-data branch, and leave width frozen at 0: a blank chart forever after.
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

  const values = data.map((d) => d.value);
  const max = Math.max(...values, 0);
  const min = Math.min(...values, 0);
  const span = max - min || 1;
  const yFor = (v: number) => padTop + plotH * (1 - (v - min) / span);

  const slot = plotW / data.length;
  const barWidth = Math.min(48, Math.max(10, slot * 0.58));
  const monthEvery = Math.max(1, Math.ceil(34 / slot));
  const valueAlways = slot >= 42;

  const ticks = Array.from({ length: 4 }, (_, i) => min + (span * i) / 3);
  const zeroY = yFor(0);
  const hasNegative = min < 0;

  return (
    <div ref={attachRef} className="w-full" style={hasData ? { height } : undefined}>
      {hasData && width > 0 ? (
        <svg width={width} height={height} role="img" aria-label={title ?? "نمودار روند"}>
          {ticks.map((t, i) => (
            <g key={i}>
              <line
                x1={padLeft}
                x2={padLeft + plotW}
                y1={yFor(t)}
                y2={yFor(t)}
                className="stroke-(--edge)"
                strokeWidth={1}
                strokeDasharray={t === 0 && hasNegative ? undefined : "3 4"}
              />
              <text x={padLeft + plotW + 8} y={yFor(t) + 3.5} className="fill-(--ice-3) text-[10.5px] tabular-nums">
                {moneyShort(t)}
              </text>
            </g>
          ))}

          {hasNegative && (
            <line
              x1={padLeft}
              x2={padLeft + plotW}
              y1={zeroY}
              y2={zeroY}
              className="stroke-(--edge-2)"
              strokeWidth={1.5}
            />
          )}

          {data.map((d, i) => {
            const positive = d.value >= 0;
            const x = padLeft + i * slot + (slot - barWidth) / 2;
            const top = positive ? yFor(d.value) : zeroY;
            const barHeight = Math.max(Math.abs(yFor(d.value) - zeroY), d.value === 0 ? 0 : 1.5);
            const [monthName, year] = d.label.split(" ");
            const showMonth = i % monthEvery === 0;
            const showYear = showMonth && (i === 0 || data[i - 1].label.split(" ")[1] !== year);
            const showValue = valueAlways || hovered === i;
            const dim = hovered !== null && hovered !== i;

            return (
              <g
                key={`${d.label}-${i}`}
                onMouseEnter={() => setHovered(i)}
                onMouseLeave={() => setHovered(null)}
                className="cursor-default"
              >
                <title>{`${d.label}: ${money(d.value)}`}</title>
                {slot >= 18 && (
                  <rect x={padLeft + i * slot} y={padTop} width={slot} height={plotH} className="fill-transparent" />
                )}
                <path
                  d={barPath(x, top, barWidth, barHeight, 7, positive)}
                  className={positive ? "fill-(--mint)" : "fill-(--ember)"}
                  opacity={dim ? 0.45 : 0.92}
                />
                {showValue && (
                  <text
                    x={x + barWidth / 2}
                    y={positive ? top - 6 : top + barHeight + 12}
                    textAnchor="middle"
                    className={`text-[10.5px] font-semibold tabular-nums ${positive ? "fill-(--mint)" : "fill-(--ember)"}`}
                  >
                    {moneyShort(d.value)}
                  </text>
                )}
                {showMonth && (
                  <text
                    x={padLeft + i * slot + slot / 2}
                    y={height - 20}
                    textAnchor="middle"
                    className={`text-[10.5px] ${hovered === i ? "fill-(--ice) font-semibold" : "fill-(--ice-3)"}`}
                  >
                    {fa(monthName)}
                  </text>
                )}
                {showYear && (
                  <text
                    x={padLeft + i * slot + slot / 2}
                    y={height - 7}
                    textAnchor="middle"
                    className="fill-(--ice-3)/70 text-[10.5px] tabular-nums"
                  >
                    {fa(year)}
                  </text>
                )}
              </g>
            );
          })}
        </svg>
      ) : hasData ? null : (
        <div className="grid h-full place-items-center py-6 text-[12.5px] text-(--ice-3)">داده‌ای برای نمایش نیست.</div>
      )}
    </div>
  );
}
